using LexVerse.Application.Modules;
using LexVerse.Application.Popup;
using LexVerse.Core.Product;

namespace LexVerse.App.Features.Popup;

public sealed class PopupFeatureModule(PopupTranslationUseCase useCase) : IFeatureModule
{
    public FeatureModuleDescriptor Descriptor { get; } = new(
        "popup-translation",
        "Feature.PopupTranslation",
        [ProductFeatures.PopupTranslation]);

    public PopupTranslationUseCase UseCase { get; } =
        useCase ?? throw new ArgumentNullException(nameof(useCase));
}
