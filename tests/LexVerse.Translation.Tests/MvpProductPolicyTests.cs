using LexVerse.Application.Product;
using LexVerse.Core.Product;
using Xunit;

namespace LexVerse.Translation.Tests;

public sealed class MvpProductPolicyTests
{
    [Fact]
    public void FreeFeatures_ContainOnlyProductionMvpWorkflows()
    {
        FeatureKey[] expected = [
            ProductFeatures.PopupTranslation,
            ProductFeatures.RegionTranslation,
            ProductFeatures.DocumentMode
        ];

        Assert.Equal(expected, MvpProductPolicy.FreeFeatures);
        Assert.DoesNotContain(ProductFeatures.FullScreenTranslation, MvpProductPolicy.FreeFeatures);
        Assert.DoesNotContain(ProductFeatures.ComicMode, MvpProductPolicy.FreeFeatures);
    }
}
