namespace LexVerse.Core.Product;

public interface IProductCatalogProvider
{
    Task<ProductCatalog> GetCatalogAsync(CancellationToken cancellationToken = default);
}
