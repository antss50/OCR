namespace LexVerse.Core.Product;

/// <summary>
/// Stable identifier used by product plans and runtime entitlement checks.
/// Keep keys independent from UI labels and implementation type names.
/// </summary>
public sealed record FeatureKey
{
    public FeatureKey(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var normalized = value.Trim().ToLowerInvariant();
        if (normalized.Any(character =>
                !(char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_')))
        {
            throw new ArgumentException(
                "Feature keys may only contain ASCII letters, digits, '.', '-' and '_'.",
                nameof(value));
        }

        Value = normalized;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
