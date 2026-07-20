namespace LexVerse.Core.Product;

/// <summary>
/// Displayable plan metadata. The server/store remains authoritative for checkout price,
/// renewal dates and entitlement issuance.
/// </summary>
public sealed record ProductPlan
{
    public ProductPlan(
        string id,
        string displayName,
        IEnumerable<FeatureKey> features,
        bool isPublic = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentNullException.ThrowIfNull(features);

        Id = id.Trim().ToLowerInvariant();
        DisplayName = displayName.Trim();
        Features = features.ToHashSet();
        IsPublic = isPublic;
    }

    public string Id { get; }

    public string DisplayName { get; }

    public IReadOnlySet<FeatureKey> Features { get; }

    public bool IsPublic { get; }
}
