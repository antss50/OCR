namespace LexVerse.Core.Product;

/// <summary>
/// Remotely supplied display offer. Checkout must resolve the offer id again at the
/// authoritative store/backend and must never trust the client-side amount.
/// </summary>
public sealed record ProductOffer
{
    public ProductOffer(
        string id,
        string planId,
        Money price,
        SubscriptionPeriod? billingPeriod = null,
        SubscriptionPeriod? trialPeriod = null,
        bool isPublic = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(planId);
        ArgumentNullException.ThrowIfNull(price);

        Id = id.Trim().ToLowerInvariant();
        PlanId = planId.Trim().ToLowerInvariant();
        Price = price;
        BillingPeriod = billingPeriod;
        TrialPeriod = trialPeriod;
        IsPublic = isPublic;
    }

    public string Id { get; }

    public string PlanId { get; }

    public Money Price { get; }

    /// <summary>Null means a one-time purchase.</summary>
    public SubscriptionPeriod? BillingPeriod { get; }

    public SubscriptionPeriod? TrialPeriod { get; }

    public bool IsPublic { get; }
}
