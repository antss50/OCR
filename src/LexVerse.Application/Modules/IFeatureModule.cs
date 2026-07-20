namespace LexVerse.Application.Modules;

/// <summary>
/// Marker and metadata contract for a separately registered product module.
/// Runtime behavior remains behind module-specific application use-case interfaces.
/// </summary>
public interface IFeatureModule
{
    FeatureModuleDescriptor Descriptor { get; }
}
