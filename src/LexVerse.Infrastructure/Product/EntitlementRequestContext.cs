namespace LexVerse.Infrastructure.Product;

public sealed record EntitlementRequestContext(string SubjectId, string? AccessToken);

public interface IEntitlementRequestContextProvider
{
    Task<EntitlementRequestContext> GetContextAsync(CancellationToken cancellationToken = default);
}
