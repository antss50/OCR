using System.Security.Cryptography;
using System.Text;

namespace LexVerse.Core.Translation;

public sealed record TranslationPromptOptions
{
    public static TranslationPromptOptions Empty { get; } = new();

    public TranslationPromptOptions(
        string? instruction = null,
        IEnumerable<string>? ignoredTerms = null,
        bool preserveAcronymsAndTechnicalTerms = false)
    {
        Instruction = instruction?.Trim() ?? string.Empty;
        IgnoredTerms = NormalizeIgnoredTerms(ignoredTerms);
        PreserveAcronymsAndTechnicalTerms = preserveAcronymsAndTechnicalTerms;

        var cacheInput = BuildCacheInput();
        CacheKey = string.IsNullOrWhiteSpace(cacheInput)
            ? string.Empty
            : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(cacheInput)));
    }

    public string Instruction { get; }

    public IReadOnlyList<string> IgnoredTerms { get; }

    public bool PreserveAcronymsAndTechnicalTerms { get; }

    public string CacheKey { get; }

    public bool HasInstruction => Instruction.Length > 0;

    public bool HasIgnoredTerms => IgnoredTerms.Count > 0;

    public bool HasTermPreservation => HasIgnoredTerms || PreserveAcronymsAndTechnicalTerms;

    private string BuildCacheInput()
    {
        if (!HasInstruction && !HasTermPreservation)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        builder.Append("instruction=");
        builder.Append(NormalizeForCache(Instruction));
        builder.Append('\n');
        builder.Append("preserveAcronyms=");
        builder.Append(PreserveAcronymsAndTechnicalTerms ? "1" : "0");

        foreach (var term in IgnoredTerms.Order(StringComparer.OrdinalIgnoreCase))
        {
            builder.Append('\n');
            builder.Append("term=");
            builder.Append(NormalizeForCache(term).ToUpperInvariant());
        }

        return builder.ToString();
    }

    private static IReadOnlyList<string> NormalizeIgnoredTerms(IEnumerable<string>? ignoredTerms)
    {
        if (ignoredTerms is null)
        {
            return [];
        }

        return ignoredTerms
            .Select(NormalizeIgnoredTerm)
            .Where(term => term.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string NormalizeIgnoredTerm(string value)
    {
        var normalized = NormalizeForCache(value);
        if (normalized.EndsWith("...", StringComparison.Ordinal))
        {
            normalized = normalized[..^3].TrimEnd();
        }

        return normalized;
    }

    private static string NormalizeForCache(string value)
    {
        var builder = new StringBuilder(value.Length);
        var previousWasWhitespace = false;

        foreach (var character in value)
        {
            if (char.IsWhiteSpace(character))
            {
                if (!previousWasWhitespace)
                {
                    builder.Append(' ');
                    previousWasWhitespace = true;
                }

                continue;
            }

            builder.Append(character);
            previousWasWhitespace = false;
        }

        return builder.ToString().Trim();
    }
}
