namespace LexVerse.Core.Product;

public sealed record FeatureEntitlement
{
    public FeatureEntitlement(
        FeatureKey feature,
        DateTimeOffset? startsAtUtc = null,
        DateTimeOffset? endsAtUtc = null,
        string? source = null)
    {
        ArgumentNullException.ThrowIfNull(feature);
        if (startsAtUtc is not null && endsAtUtc is not null && endsAtUtc <= startsAtUtc)
        {
            throw new ArgumentException("Entitlement end must be later than its start.", nameof(endsAtUtc));
        }

        Feature = feature;
        StartsAtUtc = startsAtUtc;
        EndsAtUtc = endsAtUtc;
        Source = source?.Trim();
    }

    public FeatureKey Feature { get; }

    public DateTimeOffset? StartsAtUtc { get; }

    public DateTimeOffset? EndsAtUtc { get; }

    public string? Source { get; }

    public bool IsActiveAt(DateTimeOffset utcNow) =>
        (StartsAtUtc is null || StartsAtUtc <= utcNow) &&
        (EndsAtUtc is null || utcNow < EndsAtUtc);
}
