using LexVerse.Core.Product;

namespace LexVerse.Application.Modules;

/// <summary>
/// Immutable module registry built by the app composition root.
/// Duplicate module ids or capability ownership fail during startup.
/// </summary>
public sealed class FeatureModuleRegistry
{
    private readonly IReadOnlyDictionary<string, IFeatureModule> _modulesById;
    private readonly IReadOnlyDictionary<FeatureKey, IFeatureModule> _modulesByFeature;

    public FeatureModuleRegistry(IEnumerable<IFeatureModule> modules)
    {
        ArgumentNullException.ThrowIfNull(modules);

        var moduleList = modules.ToArray();
        var duplicateModuleId = moduleList
            .GroupBy(module => module.Descriptor.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1)?.Key;
        if (duplicateModuleId is not null)
        {
            throw new ArgumentException($"Duplicate feature module id '{duplicateModuleId}'.", nameof(modules));
        }

        var featureOwners = moduleList
            .SelectMany(module => module.Descriptor.OwnedFeatures.Select(feature => (Feature: feature, Module: module)))
            .ToArray();
        var duplicateFeature = featureOwners
            .GroupBy(item => item.Feature)
            .FirstOrDefault(group => group.Count() > 1)?.Key;
        if (duplicateFeature is not null)
        {
            throw new ArgumentException(
                $"Feature '{duplicateFeature}' is owned by more than one module.",
                nameof(modules));
        }

        _modulesById = moduleList.ToDictionary(
            module => module.Descriptor.Id,
            StringComparer.OrdinalIgnoreCase);
        _modulesByFeature = featureOwners.ToDictionary(item => item.Feature, item => item.Module);
    }

    public IReadOnlyCollection<IFeatureModule> Modules => _modulesById.Values.ToArray();

    public IFeatureModule? FindById(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        return _modulesById.GetValueOrDefault(id.Trim());
    }

    public IFeatureModule? FindByFeature(FeatureKey feature)
    {
        ArgumentNullException.ThrowIfNull(feature);
        return _modulesByFeature.GetValueOrDefault(feature);
    }
}
