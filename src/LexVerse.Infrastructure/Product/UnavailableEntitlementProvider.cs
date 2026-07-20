using LexVerse.Core.Product;

namespace LexVerse.Infrastructure.Product;

public sealed class UnavailableEntitlementProvider : IEntitlementProvider
{
    public Task<EntitlementSnapshot> GetEntitlementsAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromException<EntitlementSnapshot>(new EntitlementProviderUnavailableException());
}
