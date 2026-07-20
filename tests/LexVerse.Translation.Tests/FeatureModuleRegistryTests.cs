using LexVerse.Application.Modules;
using LexVerse.Core.Product;
using Xunit;

namespace LexVerse.Translation.Tests;

public sealed class FeatureModuleRegistryTests
{
    [Fact]
    public void Registry_FindsModulesByIdAndOwnedFeature()
    {
        var popup = new StubModule("popup", ProductFeatures.PopupTranslation);
        var realtime = new StubModule(
            "realtime",
            ProductFeatures.RegionTranslation,
            ProductFeatures.FullScreenTranslation);
        var registry = new FeatureModuleRegistry([popup, realtime]);

        Assert.Same(popup, registry.FindById("POPUP"));
        Assert.Same(realtime, registry.FindByFeature(ProductFeatures.FullScreenTranslation));
        Assert.Equal(2, registry.Modules.Count);
    }

    [Fact]
    public void Registry_RejectsDuplicateModuleIds()
    {
        var modules = new IFeatureModule[]
        {
            new StubModule("reader", ProductFeatures.DocumentMode),
            new StubModule("READER", ProductFeatures.ComicMode)
        };

        Assert.Throws<ArgumentException>(() => new FeatureModuleRegistry(modules));
    }

    [Fact]
    public void Registry_RejectsDuplicateFeatureOwnership()
    {
        var modules = new IFeatureModule[]
        {
            new StubModule("region-basic", ProductFeatures.RegionTranslation),
            new StubModule("region-pro", ProductFeatures.RegionTranslation)
        };

        Assert.Throws<ArgumentException>(() => new FeatureModuleRegistry(modules));
    }

    [Fact]
    public void Descriptor_RejectsModulesWithoutFeatures()
    {
        Assert.Throws<ArgumentException>(() => new FeatureModuleDescriptor(
            "empty",
            "Feature.Empty",
            []));
    }

    private sealed class StubModule : IFeatureModule
    {
        public StubModule(string id, params FeatureKey[] features)
        {
            Descriptor = new FeatureModuleDescriptor(id, $"Feature.{id}", features);
        }

        public FeatureModuleDescriptor Descriptor { get; }
    }
}
