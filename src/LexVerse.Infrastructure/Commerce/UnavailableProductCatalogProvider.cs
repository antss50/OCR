using LexVerse.Core.Product;

namespace LexVerse.Infrastructure.Commerce;

public sealed class UnavailableProductCatalogProvider : IProductCatalogProvider
{
    public Task<ProductCatalog> GetCatalogAsync(CancellationToken cancellationToken = default) =>
        Task.FromException<ProductCatalog>(new InvalidOperationException("The product catalog is not configured."));
}
