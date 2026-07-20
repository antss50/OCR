using LexVerse.Core.Product;

namespace LexVerse.Application.Modules;

public sealed record FeatureModuleDescriptor
{
    public FeatureModuleDescriptor(
        string id,
        string displayNameResourceKey,
        IEnumerable<FeatureKey> ownedFeatures)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayNameResourceKey);
        ArgumentNullException.ThrowIfNull(ownedFeatures);

        var normalizedId = id.Trim().ToLowerInvariant();
        if (normalizedId.Any(character =>
                !(char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_')))
        {
            throw new ArgumentException(
                "Module ids may only contain ASCII letters, digits, '.', '-' and '_'.",
                nameof(id));
        }

        var features = ownedFeatures.ToHashSet();
        if (features.Count == 0)
        {
            throw new ArgumentException("A feature module must own at least one feature.", nameof(ownedFeatures));
        }

        Id = normalizedId;
        DisplayNameResourceKey = displayNameResourceKey.Trim();
        OwnedFeatures = features;
    }

    public string Id { get; }

    public string DisplayNameResourceKey { get; }

    public IReadOnlySet<FeatureKey> OwnedFeatures { get; }
}
