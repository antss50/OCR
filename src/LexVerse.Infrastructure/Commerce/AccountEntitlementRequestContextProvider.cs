using LexVerse.Application.Account;
using LexVerse.Infrastructure.Product;

namespace LexVerse.Infrastructure.Commerce;

public sealed class AccountEntitlementRequestContextProvider(IAccountSessionProvider accounts)
    : IEntitlementRequestContextProvider
{
    public async Task<EntitlementRequestContext> GetContextAsync(
        CancellationToken cancellationToken = default)
    {
        var session = await accounts.GetSessionAsync(cancellationToken)
            ?? throw new InvalidOperationException("Sign in is required for paid entitlements.");
        string? token = null;
        try
        {
            token = await accounts.GetAccessTokenAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // The signed cache remains usable offline and is still bound to this subject.
        }

        return new EntitlementRequestContext(session.SubjectId, token);
    }
}
