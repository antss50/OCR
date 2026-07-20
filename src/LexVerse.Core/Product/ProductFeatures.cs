namespace LexVerse.Core.Product;

/// <summary>
/// Product capabilities that can be assigned to commercial plans.
/// New capabilities should receive a new stable key instead of reusing an old key.
/// </summary>
public static class ProductFeatures
{
    public static FeatureKey PopupTranslation { get; } = new("translation.popup");

    public static FeatureKey RegionTranslation { get; } = new("translation.region");

    public static FeatureKey FullScreenTranslation { get; } = new("translation.full-screen");

    public static FeatureKey DocumentMode { get; } = new("mode.document");

    public static FeatureKey ComicMode { get; } = new("mode.comic");

    public static FeatureKey CustomTranslationStyle { get; } = new("translation.custom-style");
}
