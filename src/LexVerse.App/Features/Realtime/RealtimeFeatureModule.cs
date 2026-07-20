using LexVerse.Application.Modules;
using LexVerse.Application.Realtime;
using LexVerse.Core.Product;

namespace LexVerse.App.Features.Realtime;

public sealed class RealtimeFeatureModule(
    RealtimeTranslationCoordinator coordinator) : IFeatureModule, IAsyncDisposable
{
    public FeatureModuleDescriptor Descriptor { get; } = new(
        "realtime-translation",
        "Feature.RealtimeTranslation",
        [ProductFeatures.RegionTranslation, ProductFeatures.FullScreenTranslation]);

    public RealtimeTranslationCoordinator Coordinator { get; } =
        coordinator ?? throw new ArgumentNullException(nameof(coordinator));

    public ValueTask DisposeAsync() => Coordinator.DisposeAsync();
}
