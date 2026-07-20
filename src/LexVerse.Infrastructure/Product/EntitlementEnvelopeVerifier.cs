using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using LexVerse.Core.Product;

namespace LexVerse.Infrastructure.Product;

/// <summary>Verifies a bounded ES256 envelope before any entitlement claims are trusted.</summary>
public sealed class EntitlementEnvelopeVerifier
{
    public const int MaximumEnvelopeBytes = 256 * 1024;
    private const int Es256SignatureBytes = 64;
    private const string EnvelopeSchema = "lexverse.entitlements.v1";
    private const string Algorithm = "ES256";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    private readonly IReadOnlyDictionary<string, EntitlementVerificationKey> _keys;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _clockSkew;
    private readonly TimeSpan _maximumLifetime;

    public EntitlementEnvelopeVerifier(
        IEnumerable<EntitlementVerificationKey> keys,
        TimeProvider? timeProvider = null,
        TimeSpan? clockSkew = null,
        TimeSpan? maximumLifetime = null)
    {
        ArgumentNullException.ThrowIfNull(keys);
        _keys = keys.ToDictionary(key => key.KeyId, StringComparer.Ordinal);
        if (_keys.Count == 0)
        {
            throw new ArgumentException("At least one entitlement verification key is required.", nameof(keys));
        }

        _timeProvider = timeProvider ?? TimeProvider.System;
        _clockSkew = ValidateNonNegative(clockSkew ?? TimeSpan.FromMinutes(2), nameof(clockSkew));
        _maximumLifetime = ValidatePositive(maximumLifetime ?? TimeSpan.FromDays(3), nameof(maximumLifetime));
    }

    public EntitlementSnapshot Verify(ReadOnlySpan<byte> envelopeJson, string expectedSubjectId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedSubjectId);
        if (envelopeJson.IsEmpty || envelopeJson.Length > MaximumEnvelopeBytes)
        {
            throw new EntitlementVerificationException("The entitlement envelope size is invalid.");
        }

        try
        {
            var envelope = JsonSerializer.Deserialize<EnvelopeWire>(envelopeJson, JsonOptions)
                ?? throw new EntitlementVerificationException("The entitlement envelope is empty.");
            if (!string.Equals(envelope.Schema, EnvelopeSchema, StringComparison.Ordinal) ||
                !string.Equals(envelope.Algorithm, Algorithm, StringComparison.Ordinal))
            {
                throw new EntitlementVerificationException("The entitlement envelope format is unsupported.");
            }

            if (!_keys.TryGetValue(envelope.KeyId, out var key))
            {
                throw new EntitlementVerificationException("The entitlement signing key is unknown.");
            }

            var payloadBytes = DecodeBase64Url(envelope.Payload, MaximumEnvelopeBytes);
            var signatureBytes = DecodeBase64Url(envelope.Signature, Es256SignatureBytes);
            if (signatureBytes.Length != Es256SignatureBytes)
            {
                throw new EntitlementVerificationException("The entitlement signature length is invalid.");
            }

            using var ecdsa = ECDsa.Create();
            ecdsa.ImportFromPem(key.PublicKeyPem);
            if (ecdsa.KeySize != 256 || !ecdsa.VerifyData(
                    payloadBytes,
                    signatureBytes,
                    HashAlgorithmName.SHA256,
                    DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
            {
                throw new EntitlementVerificationException("The entitlement signature is invalid.");
            }

            var payload = JsonSerializer.Deserialize<PayloadWire>(payloadBytes, JsonOptions)
                ?? throw new EntitlementVerificationException("The entitlement payload is empty.");
            return ValidatePayload(payload, envelope.KeyId, expectedSubjectId.Trim());
        }
        catch (EntitlementVerificationException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or FormatException or CryptographicException or ArgumentException)
        {
            throw new EntitlementVerificationException("The entitlement envelope could not be verified.", exception);
        }
    }

    private EntitlementSnapshot ValidatePayload(PayloadWire payload, string keyId, string expectedSubjectId)
    {
        if (payload.SchemaVersion != 1 ||
            string.IsNullOrWhiteSpace(payload.SubjectId) ||
            string.IsNullOrWhiteSpace(payload.Revision) ||
            payload.Revision.Length > 128 ||
            !string.Equals(payload.SubjectId, expectedSubjectId, StringComparison.Ordinal))
        {
            throw new EntitlementVerificationException("The entitlement payload identity is invalid.");
        }

        var now = _timeProvider.GetUtcNow();
        if (payload.IssuedAtUtc > now.Add(_clockSkew) ||
            payload.NotBeforeUtc > now.Add(_clockSkew) ||
            payload.ExpiresAtUtc <= now ||
            payload.ExpiresAtUtc <= payload.NotBeforeUtc ||
            payload.ExpiresAtUtc <= payload.IssuedAtUtc ||
            payload.ExpiresAtUtc - payload.IssuedAtUtc > _maximumLifetime)
        {
            throw new EntitlementVerificationException("The entitlement payload validity period is invalid.");
        }

        if (payload.Grants is null || payload.Grants.Count > 256)
        {
            throw new EntitlementVerificationException("The entitlement grant collection is invalid.");
        }

        var grants = new List<FeatureEntitlement>(payload.Grants.Count);
        foreach (var grant in payload.Grants)
        {
            if (string.IsNullOrWhiteSpace(grant.Feature) || grant.Feature.Length > 128 || grant.Source?.Length > 128)
            {
                throw new EntitlementVerificationException("An entitlement grant is invalid.");
            }

            var startsAtUtc = grant.StartsAtUtc ?? payload.NotBeforeUtc;
            var endsAtUtc = grant.EndsAtUtc is { } grantEnd && grantEnd < payload.ExpiresAtUtc
                ? grantEnd
                : payload.ExpiresAtUtc;
            if (endsAtUtc <= startsAtUtc)
            {
                throw new EntitlementVerificationException("An entitlement grant validity period is invalid.");
            }

            grants.Add(new FeatureEntitlement(
                new FeatureKey(grant.Feature),
                startsAtUtc,
                endsAtUtc,
                grant.Source));
        }

        return new EntitlementSnapshot(
            payload.SubjectId,
            payload.Revision,
            payload.IssuedAtUtc,
            grants,
            keyId);
    }

    private static byte[] DecodeBase64Url(string value, int maximumDecodedBytes)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > ((maximumDecodedBytes + 2) / 3 * 4))
        {
            throw new EntitlementVerificationException("An entitlement envelope field is invalid.");
        }

        if (value.Any(character =>
                !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_')))
        {
            throw new EntitlementVerificationException("An entitlement envelope field is not base64url.");
        }

        var base64 = value.Replace('-', '+').Replace('_', '/');
        base64 += (base64.Length % 4) switch
        {
            2 => "==",
            3 => "=",
            0 => string.Empty,
            _ => throw new FormatException()
        };
        var decoded = Convert.FromBase64String(base64);
        if (decoded.Length > maximumDecodedBytes)
        {
            throw new EntitlementVerificationException("An entitlement envelope field is too large.");
        }

        return decoded;
    }

    private static TimeSpan ValidateNonNegative(TimeSpan value, string name) =>
        value < TimeSpan.Zero ? throw new ArgumentOutOfRangeException(name) : value;

    private static TimeSpan ValidatePositive(TimeSpan value, string name) =>
        value <= TimeSpan.Zero ? throw new ArgumentOutOfRangeException(name) : value;

    private sealed class EnvelopeWire
    {
        public required string Schema { get; init; }
        public required string KeyId { get; init; }
        public required string Algorithm { get; init; }
        public required string Payload { get; init; }
        public required string Signature { get; init; }
    }

    private sealed class PayloadWire
    {
        public required int SchemaVersion { get; init; }
        public required string SubjectId { get; init; }
        public required string Revision { get; init; }
        public required DateTimeOffset IssuedAtUtc { get; init; }
        public required DateTimeOffset NotBeforeUtc { get; init; }
        public required DateTimeOffset ExpiresAtUtc { get; init; }
        public required List<GrantWire> Grants { get; init; }
    }

    private sealed class GrantWire
    {
        public required string Feature { get; init; }
        public DateTimeOffset? StartsAtUtc { get; init; }
        public DateTimeOffset? EndsAtUtc { get; init; }
        public string? Source { get; init; }
    }
}
