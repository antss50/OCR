using System.Security.Cryptography;
using System.Text;

namespace LexVerse.Core.Translation;

public sealed record TranslationPromptOptions
{
    public static TranslationPromptOptions Empty { get; } = new();

    public TranslationPromptOptions(string? instruction = null)
    {
        Instruction = instruction?.Trim() ?? string.Empty;
        CacheKey = string.IsNullOrWhiteSpace(Instruction)
            ? string.Empty
            : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(NormalizeForCache(Instruction))));
    }

    public string Instruction { get; }

    public string CacheKey { get; }

    public bool HasInstruction => Instruction.Length > 0;

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
