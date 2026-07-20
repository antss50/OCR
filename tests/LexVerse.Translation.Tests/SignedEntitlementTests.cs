using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LexVerse.Application.Product;
using LexVerse.Core.Product;
using LexVerse.Infrastructure.Product;
using Xunit;

namespace LexVerse.Translation.Tests;

public sealed class SignedEntitlementTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 20, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Verify_AcceptsValidEs256EnvelopeAndClampsPermanentGrant()
    {
        using var signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var envelope = CreateEnvelope(signingKey, "customer-1", Now, Now.AddHours(24));
        var verifier = CreateVerifier(signingKey, Now);

        var snapshot = verifier.Verify(envelope, "customer-1");

        Assert.Equal("key-2026-07", snapshot.VerifiedByKeyId);
        var grant = Assert.Single(snapshot.Entitlements);
        Assert.Equal(Now.AddHours(24), grant.EndsAtUtc);
        Assert.True(snapshot.Evaluate(ProductFeatures.RegionTranslation, Now).IsAllowed);
        Assert.False(snapshot.Evaluate(ProductFeatures.RegionTranslation, Now.AddHours(24)).IsAllowed);
    }

    [Fact]
    public void Verify_RejectsTamperingWrongSubjectAndExpiredClaims()
    {
        using var signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var valid = CreateEnvelope(signingKey, "customer-1", Now, Now.AddHours(24));
        var verifier = CreateVerifier(signingKey, Now);
        var tampered = valid.ToArray();
        tampered[^8] ^= 1;

        Assert.Throws<EntitlementVerificationException>(() => verifier.Verify(tampered, "customer-1"));
        Assert.Throws<EntitlementVerificationException>(() => verifier.Verify(valid, "customer-2"));

        var expired = CreateEnvelope(signingKey, "customer-1", Now.AddDays(-2), Now.AddMinutes(-1));
        Assert.Throws<EntitlementVerificationException>(() => verifier.Verify(expired, "customer-1"));
    }

    [Fact]
    public void Verify_RejectsUnknownKeyAndExcessiveOfflineLifetime()
    {
        using var signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var otherKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var envelope = CreateEnvelope(signingKey, "customer-1", Now, Now.AddDays(4));
        var verifier = new EntitlementEnvelopeVerifier(
            [new EntitlementVerificationKey("other-key", otherKey.ExportSubjectPublicKeyInfoPem())],
            new FixedTimeProvider(Now));

        Assert.Throws<EntitlementVerificationException>(() => verifier.Verify(envelope, "customer-1"));

        var matchingVerifier = CreateVerifier(signingKey, Now);
        Assert.Throws<EntitlementVerificationException>(() => matchingVerifier.Verify(envelope, "customer-1"));
    }

    [Fact]
    public async Task RemoteProvider_UsesOnlyAStillVerifiedExactCacheWhenOffline()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var envelope = CreateEnvelope(signingKey, "customer-1", Now, Now.AddHours(24));
        var handler = new SequenceHandler(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(envelope) },
            new HttpRequestException("offline"));
        using var client = new HttpClient(handler);
        var directory = Path.Combine(Path.GetTempPath(), $"lexverse-entitlements-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "cache.json");
        using var cache = new SignedEntitlementFileCache(path);
        var provider = new RemoteSignedEntitlementProvider(
            client,
            new Uri("https://entitlements.example.test/v1/me"),
            "customer-1",
            CreateVerifier(signingKey, Now),
            cache);

        var remote = await provider.GetEntitlementsAsync(cancellationToken);
        var offline = await provider.GetEntitlementsAsync(cancellationToken);

        Assert.Equal(remote.Revision, offline.Revision);
        Assert.Equal(envelope, await File.ReadAllBytesAsync(path, cancellationToken));
        Directory.Delete(directory, recursive: true);
    }

    [Fact]
    public async Task FreeTier_DoesNotGrantPaidFeaturesWhenProviderIsUnavailable()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var paid = new FeatureAccessService(new UnavailableEntitlementProvider());
        var access = new FreeTierFeatureAccessService(paid, [ProductFeatures.PopupTranslation]);

        var popup = await access.GetAccessAsync(ProductFeatures.PopupTranslation, cancellationToken);
        var region = await access.GetAccessAsync(ProductFeatures.RegionTranslation, cancellationToken);

        Assert.True(popup.IsAllowed);
        Assert.False(region.IsAllowed);
        Assert.Equal(FeatureAccessDenialReason.EntitlementsUnavailable, region.DenialReason);
    }

    [Fact]
    public async Task RemoteProvider_RejectsTamperedOfflineCache()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var envelope = CreateEnvelope(signingKey, "customer-1", Now, Now.AddHours(24));
        envelope[^8] ^= 1;
        var directory = Path.Combine(Path.GetTempPath(), $"lexverse-entitlements-{Guid.NewGuid():N}");
        using var cache = new SignedEntitlementFileCache(Path.Combine(directory, "cache.json"));
        await cache.WriteAsync(envelope, cancellationToken);
        using var client = new HttpClient(new SequenceHandler(new HttpRequestException("offline")));
        var provider = new RemoteSignedEntitlementProvider(
            client,
            new Uri("https://entitlements.example.test/v1/me"),
            "customer-1",
            CreateVerifier(signingKey, Now),
            cache);

        await Assert.ThrowsAsync<EntitlementProviderUnavailableException>(
            () => provider.GetEntitlementsAsync(cancellationToken));

        Directory.Delete(directory, recursive: true);
    }

    private static EntitlementEnvelopeVerifier CreateVerifier(ECDsa key, DateTimeOffset now) =>
        new(
            [new EntitlementVerificationKey("key-2026-07", key.ExportSubjectPublicKeyInfoPem())],
            new FixedTimeProvider(now));

    private static byte[] CreateEnvelope(
        ECDsa signingKey,
        string subjectId,
        DateTimeOffset issuedAtUtc,
        DateTimeOffset expiresAtUtc)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 1,
            subjectId,
            revision = "revision-42",
            issuedAtUtc,
            notBeforeUtc = issuedAtUtc,
            expiresAtUtc,
            grants = new[]
            {
                new
                {
                    feature = ProductFeatures.RegionTranslation.Value,
                    startsAtUtc = (DateTimeOffset?)null,
                    endsAtUtc = (DateTimeOffset?)null,
                    source = "subscription"
                }
            }
        });
        var signature = signingKey.SignData(
            payload,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return JsonSerializer.SerializeToUtf8Bytes(new
        {
            schema = "lexverse.entitlements.v1",
            keyId = "key-2026-07",
            algorithm = "ES256",
            payload = Base64Url(payload),
            signature = Base64Url(signature)
        });
    }

    private static string Base64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class SequenceHandler(params object[] results) : HttpMessageHandler
    {
        private int _index;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var result = results[Math.Min(_index++, results.Length - 1)];
            return result is HttpResponseMessage response
                ? Task.FromResult(response)
                : Task.FromException<HttpResponseMessage>((Exception)result);
        }
    }
}
