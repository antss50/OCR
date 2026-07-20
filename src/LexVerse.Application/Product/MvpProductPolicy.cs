using LexVerse.Core.Product;

namespace LexVerse.Application.Product;

/// <summary>
/// Launch scope for the production MVP. Commercial capabilities remain available as
/// extension points, but these core workflows never require an account or purchase.
/// </summary>
public static class MvpProductPolicy
{
    public static IReadOnlyCollection<FeatureKey> FreeFeatures { get; } = Array.AsReadOnly<FeatureKey>([
        ProductFeatures.PopupTranslation,
        ProductFeatures.RegionTranslation,
        ProductFeatures.DocumentMode
    ]);
}
