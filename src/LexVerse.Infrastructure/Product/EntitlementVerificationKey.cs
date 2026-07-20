namespace LexVerse.Infrastructure.Product;

public sealed record EntitlementVerificationKey
{
    public EntitlementVerificationKey(string keyId, string publicKeyPem)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyId);
        ArgumentException.ThrowIfNullOrWhiteSpace(publicKeyPem);
        if (keyId.Length > 64 || keyId.Any(character =>
                !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.')))
        {
            throw new ArgumentException("Key IDs must be at most 64 safe ASCII characters.", nameof(keyId));
        }

        KeyId = keyId;
        PublicKeyPem = publicKeyPem;
    }

    public string KeyId { get; }

    public string PublicKeyPem { get; }
}
