namespace LexVerse.Core.Product;

/// <summary>
/// Loads a verified entitlement snapshot from a remote service or an offline signed cache.
/// Implementations must fail closed when authenticity cannot be established.
/// </summary>
public interface IEntitlementProvider
{
    Task<EntitlementSnapshot> GetEntitlementsAsync(CancellationToken cancellationToken = default);
}
