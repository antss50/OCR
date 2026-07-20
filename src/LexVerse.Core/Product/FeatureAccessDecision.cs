namespace LexVerse.Core.Product;

public enum FeatureAccessDenialReason
{
    None,
    NotEntitled,
    NotYetActive,
    Expired,
    EntitlementsUnavailable
}

public sealed record FeatureAccessDecision(
    FeatureKey Feature,
    bool IsAllowed,
    FeatureAccessDenialReason DenialReason,
    DateTimeOffset? ExpiresAtUtc = null)
{
    public static FeatureAccessDecision Allowed(FeatureKey feature, DateTimeOffset? expiresAtUtc) =>
        new(feature, true, FeatureAccessDenialReason.None, expiresAtUtc);

    public static FeatureAccessDecision Denied(FeatureKey feature, FeatureAccessDenialReason reason) =>
        new(feature, false, reason);
}
