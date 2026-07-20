namespace LexVerse.Core.Product;

/// <summary>
/// Immutable, already-verified view of the capabilities granted to a customer.
/// Signature verification and refresh belong to the infrastructure provider.
/// </summary>
public sealed class EntitlementSnapshot
{
    private readonly IReadOnlyDictionary<FeatureKey, IReadOnlyList<FeatureEntitlement>> _entitlements;

    public EntitlementSnapshot(
        string subjectId,
        string revision,
        DateTimeOffset issuedAtUtc,
        IEnumerable<FeatureEntitlement> entitlements,
        string? verifiedByKeyId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(revision);
        ArgumentNullException.ThrowIfNull(entitlements);

        SubjectId = subjectId.Trim();
        Revision = revision.Trim();
        IssuedAtUtc = issuedAtUtc;
        VerifiedByKeyId = verifiedByKeyId?.Trim();
        Entitlements = entitlements.ToArray();
        _entitlements = Entitlements
            .GroupBy(entitlement => entitlement.Feature)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<FeatureEntitlement>)group.ToArray());
    }

    public string SubjectId { get; }

    public string Revision { get; }

    public DateTimeOffset IssuedAtUtc { get; }

    public string? VerifiedByKeyId { get; }

    public IReadOnlyList<FeatureEntitlement> Entitlements { get; }

    public FeatureAccessDecision Evaluate(FeatureKey feature, DateTimeOffset utcNow)
    {
        if (!_entitlements.TryGetValue(feature, out var grants) || grants.Count == 0)
        {
            return FeatureAccessDecision.Denied(feature, FeatureAccessDenialReason.NotEntitled);
        }

        var activeGrants = grants.Where(grant => grant.IsActiveAt(utcNow)).ToArray();
        if (activeGrants.Length > 0)
        {
            var hasPermanentGrant = activeGrants.Any(grant => grant.EndsAtUtc is null);
            var expiry = hasPermanentGrant
                ? null
                : activeGrants.Max(grant => grant.EndsAtUtc);

            return FeatureAccessDecision.Allowed(feature, expiry);
        }

        var hasFutureGrant = grants.Any(grant => grant.StartsAtUtc > utcNow);
        return FeatureAccessDecision.Denied(
            feature,
            hasFutureGrant
                ? FeatureAccessDenialReason.NotYetActive
                : FeatureAccessDenialReason.Expired);
    }
}
