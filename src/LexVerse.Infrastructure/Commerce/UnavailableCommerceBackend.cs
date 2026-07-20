using LexVerse.Application.Commerce;

namespace LexVerse.Infrastructure.Commerce;

public sealed class UnavailableCommerceBackend : ICommerceBackend
{
    public bool IsConfigured => false;

    public Task<CheckoutSession> CreateCheckoutAsync(
        string offerId,
        CancellationToken cancellationToken = default) =>
        Task.FromException<CheckoutSession>(new InvalidOperationException("Commerce is not configured."));

    public Task RestorePurchasesAsync(CancellationToken cancellationToken = default) =>
        Task.FromException(new InvalidOperationException("Commerce is not configured."));
}
