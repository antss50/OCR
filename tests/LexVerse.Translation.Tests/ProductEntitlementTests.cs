using LexVerse.Core.Product;
using Xunit;

namespace LexVerse.Translation.Tests;

public sealed class ProductEntitlementTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 20, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void FeatureKey_NormalizesStableIdentifiers()
    {
        var key = new FeatureKey(" Translation.Popup ");

        Assert.Equal("translation.popup", key.Value);
        Assert.Equal(key, ProductFeatures.PopupTranslation);
    }

    [Theory]
    [InlineData("")]
    [InlineData("translation popup")]
    [InlineData("translation/popup")]
    public void FeatureKey_RejectsInvalidIdentifiers(string value)
    {
        Assert.ThrowsAny<ArgumentException>(() => new FeatureKey(value));
    }

    [Fact]
    public void Evaluate_AllowsAnActiveGrantAndReturnsItsExpiry()
    {
        var expiry = Now.AddDays(30);
        var snapshot = CreateSnapshot(new FeatureEntitlement(
            ProductFeatures.RegionTranslation,
            startsAtUtc: Now.AddDays(-1),
            endsAtUtc: expiry,
            source: "subscription"));

        var decision = snapshot.Evaluate(ProductFeatures.RegionTranslation, Now);

        Assert.True(decision.IsAllowed);
        Assert.Equal(FeatureAccessDenialReason.None, decision.DenialReason);
        Assert.Equal(expiry, decision.ExpiresAtUtc);
    }

    [Fact]
    public void Evaluate_PermanentGrantWinsOverExpiringGrant()
    {
        var snapshot = CreateSnapshot(
            new FeatureEntitlement(ProductFeatures.DocumentMode, endsAtUtc: Now.AddDays(1)),
            new FeatureEntitlement(ProductFeatures.DocumentMode));

        var decision = snapshot.Evaluate(ProductFeatures.DocumentMode, Now);

        Assert.True(decision.IsAllowed);
        Assert.Null(decision.ExpiresAtUtc);
    }

    [Fact]
    public void Evaluate_DistinguishesFutureExpiredAndMissingGrants()
    {
        var snapshot = CreateSnapshot(
            new FeatureEntitlement(ProductFeatures.ComicMode, startsAtUtc: Now.AddHours(1)),
            new FeatureEntitlement(ProductFeatures.FullScreenTranslation, endsAtUtc: Now));

        Assert.Equal(
            FeatureAccessDenialReason.NotYetActive,
            snapshot.Evaluate(ProductFeatures.ComicMode, Now).DenialReason);
        Assert.Equal(
            FeatureAccessDenialReason.Expired,
            snapshot.Evaluate(ProductFeatures.FullScreenTranslation, Now).DenialReason);
        Assert.Equal(
            FeatureAccessDenialReason.NotEntitled,
            snapshot.Evaluate(ProductFeatures.PopupTranslation, Now).DenialReason);
    }

    [Fact]
    public void Catalog_RejectsDuplicatePlanIdsCaseInsensitively()
    {
        var plans = new[]
        {
            new ProductPlan("pro", "Pro", [ProductFeatures.DocumentMode]),
            new ProductPlan("PRO", "Pro duplicate", [ProductFeatures.ComicMode])
        };

        Assert.Throws<ArgumentException>(() => new ProductCatalog("v1", Now, plans));
    }

    [Fact]
    public void Catalog_FindsPlansCaseInsensitivelyAndDeduplicatesFeatures()
    {
        var plan = new ProductPlan(
            "reader-pro",
            "Reader Pro",
            [ProductFeatures.DocumentMode, ProductFeatures.DocumentMode, ProductFeatures.ComicMode]);
        var catalog = new ProductCatalog("v1", Now, [plan]);

        var found = catalog.FindPlan("READER-PRO")
            ?? throw new Xunit.Sdk.XunitException("Expected plan to be found.");

        Assert.Same(plan, found);
        Assert.Equal(2, found.Features.Count);
    }

    [Fact]
    public void Catalog_SupportsRemotePricesBillingPeriodsAndTrials()
    {
        var plan = new ProductPlan("reader-pro", "Reader Pro", [ProductFeatures.DocumentMode]);
        var monthly = new ProductOffer(
            "reader-pro-monthly-vnd",
            plan.Id,
            new Money(99_000m, "vnd"),
            new SubscriptionPeriod(1, SubscriptionPeriodUnit.Month),
            new SubscriptionPeriod(7, SubscriptionPeriodUnit.Day));
        var catalog = new ProductCatalog("v2", Now, [plan], [monthly]);

        var offer = Assert.Single(catalog.FindOffersForPlan("READER-PRO"));

        Assert.Same(monthly, offer);
        Assert.Equal("VND", offer.Price.CurrencyCode);
        Assert.Equal(99_000m, offer.Price.Amount);
        Assert.Equal(SubscriptionPeriodUnit.Month, offer.BillingPeriod?.Unit);
        Assert.Equal(7, offer.TrialPeriod?.Count);
    }

    [Fact]
    public void Catalog_RejectsOffersForUnknownPlans()
    {
        var offer = new ProductOffer(
            "missing-monthly",
            "missing-plan",
            new Money(5m, "usd"),
            new SubscriptionPeriod(1, SubscriptionPeriodUnit.Month));

        Assert.Throws<ArgumentException>(() => new ProductCatalog("v1", Now, [], [offer]));
    }

    private static EntitlementSnapshot CreateSnapshot(params FeatureEntitlement[] entitlements) =>
        new("customer-1", "revision-1", Now.AddMinutes(-5), entitlements);
}
