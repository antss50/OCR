namespace LexVerse.Core.Product;

/// <summary>
/// Versioned plan catalog that can be replaced from remote configuration without rebuilding the app.
/// </summary>
public sealed class ProductCatalog
{
    private readonly IReadOnlyDictionary<string, ProductPlan> _plans;
    private readonly IReadOnlyDictionary<string, ProductOffer> _offers;

    public ProductCatalog(
        string revision,
        DateTimeOffset publishedAtUtc,
        IEnumerable<ProductPlan> plans,
        IEnumerable<ProductOffer>? offers = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(revision);
        ArgumentNullException.ThrowIfNull(plans);

        Revision = revision.Trim();
        PublishedAtUtc = publishedAtUtc;

        var planList = plans.ToArray();
        var duplicateId = planList
            .GroupBy(plan => plan.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1)?.Key;
        if (duplicateId is not null)
        {
            throw new ArgumentException($"Duplicate product plan id '{duplicateId}'.", nameof(plans));
        }

        _plans = planList.ToDictionary(plan => plan.Id, StringComparer.OrdinalIgnoreCase);

        var offerList = offers?.ToArray() ?? [];
        var duplicateOfferId = offerList
            .GroupBy(offer => offer.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1)?.Key;
        if (duplicateOfferId is not null)
        {
            throw new ArgumentException($"Duplicate product offer id '{duplicateOfferId}'.", nameof(offers));
        }

        var unknownPlanId = offerList
            .Select(offer => offer.PlanId)
            .FirstOrDefault(planId => !_plans.ContainsKey(planId));
        if (unknownPlanId is not null)
        {
            throw new ArgumentException($"Product offer references unknown plan '{unknownPlanId}'.", nameof(offers));
        }

        _offers = offerList.ToDictionary(offer => offer.Id, StringComparer.OrdinalIgnoreCase);
    }

    public string Revision { get; }

    public DateTimeOffset PublishedAtUtc { get; }

    public IReadOnlyCollection<ProductPlan> Plans => _plans.Values.ToArray();

    public IReadOnlyCollection<ProductOffer> Offers => _offers.Values.ToArray();

    public ProductPlan? FindPlan(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        return _plans.GetValueOrDefault(id.Trim());
    }

    public ProductOffer? FindOffer(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        return _offers.GetValueOrDefault(id.Trim());
    }

    public IReadOnlyList<ProductOffer> FindOffersForPlan(string planId)
    {
        if (string.IsNullOrWhiteSpace(planId))
        {
            return [];
        }

        return _offers.Values
            .Where(offer => offer.PlanId.Equals(planId.Trim(), StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }
}
