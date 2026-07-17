using System.Text;
using System.Text.RegularExpressions;

namespace LexVerse.Core.Translation;

public static class TranslationTermProtector
{
    private static readonly Regex AcronymRegex = new(
        @"(?<![\p{L}\p{N}_])[A-Z][A-Z0-9]*(?:[/-][A-Z0-9]+)*(?![\p{L}\p{N}_])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex UppercaseWordRegex = new(
        @"(?<![\p{L}\p{N}_])[A-Z]+(?:['’][A-Z]+)?(?![\p{L}\p{N}_])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    public static ProtectedTranslationText Protect(
        string text,
        TranslationPromptOptions? options)
    {
        options ??= TranslationPromptOptions.Empty;
        if (string.IsNullOrWhiteSpace(text) || !options.HasTermPreservation)
        {
            return new ProtectedTranslationText(text, new Dictionary<string, string>());
        }

        var matches = SelectNonOverlappingMatches(FindMatches(text, options));
        if (matches.Count == 0)
        {
            return new ProtectedTranslationText(text, new Dictionary<string, string>());
        }

        var builder = new StringBuilder(text.Length);
        var replacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var cursor = 0;
        var tokenIndex = 0;

        foreach (var match in matches)
        {
            if (match.Start < cursor)
            {
                continue;
            }

            builder.Append(text, cursor, match.Start - cursor);

            var token = CreateToken(tokenIndex++);
            builder.Append(token);
            replacements[token] = text.Substring(match.Start, match.Length);
            cursor = match.Start + match.Length;
        }

        builder.Append(text, cursor, text.Length - cursor);
        return new ProtectedTranslationText(builder.ToString(), replacements);
    }

    public static bool ShouldIgnoreDetectedText(
        string text,
        TranslationPromptOptions? options)
    {
        options ??= TranslationPromptOptions.Empty;
        if (string.IsNullOrWhiteSpace(text) || !options.HasTermPreservation)
        {
            return false;
        }

        var matches = SelectNonOverlappingMatches(FindIgnoredTermMatches(text, options));
        if (matches.Count == 0)
        {
            return false;
        }

        var cursor = 0;
        foreach (var match in matches)
        {
            if (ContainsLetterOutsideIgnoredTerms(text.AsSpan(cursor, match.Start - cursor)))
            {
                return false;
            }

            cursor = match.Start + match.Length;
        }

        return !ContainsLetterOutsideIgnoredTerms(text.AsSpan(cursor));
    }

    private static IReadOnlyList<TermMatch> FindMatches(
        string text,
        TranslationPromptOptions options)
    {
        var candidates = new List<TermMatch>(FindIgnoredTermMatches(text, options));

        if (options.PreserveAcronymsAndTechnicalTerms && !LooksLikeAllCapsProse(text))
        {
            foreach (Match match in AcronymRegex.Matches(text))
            {
                if (ShouldPreserveAcronym(match.Value))
                {
                    candidates.Add(new TermMatch(match.Index, match.Length, 1));
                }
            }
        }

        return candidates
            .OrderBy(match => match.Start)
            .ThenBy(match => match.Priority)
            .ThenByDescending(match => match.Length)
            .ToArray();
    }

    private static IReadOnlyList<TermMatch> FindIgnoredTermMatches(
        string text,
        TranslationPromptOptions options)
    {
        var candidates = new List<TermMatch>();

        foreach (var term in options.IgnoredTerms.OrderByDescending(term => term.Length))
        {
            AddIgnoredTermMatches(text, term, candidates);
        }

        return candidates
            .OrderBy(match => match.Start)
            .ThenBy(match => match.Priority)
            .ThenByDescending(match => match.Length)
            .ToArray();
    }

    private static IReadOnlyList<TermMatch> SelectNonOverlappingMatches(IReadOnlyList<TermMatch> matches)
    {
        if (matches.Count == 0)
        {
            return Array.Empty<TermMatch>();
        }

        var selected = new List<TermMatch>(matches.Count);
        var cursor = 0;
        foreach (var match in matches)
        {
            if (match.Start < cursor)
            {
                continue;
            }

            selected.Add(match);
            cursor = match.Start + match.Length;
        }

        return selected;
    }

    private static void AddIgnoredTermMatches(
        string text,
        string term,
        List<TermMatch> candidates)
    {
        if (string.IsNullOrWhiteSpace(term))
        {
            return;
        }

        var pattern = $@"(?<![\p{{L}}\p{{N}}_]){Regex.Escape(term)}(?![\p{{L}}\p{{N}}_])";
        foreach (Match match in Regex.Matches(
                     text,
                     pattern,
                     RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            candidates.Add(new TermMatch(match.Index, match.Length, 0));
        }
    }

    private static bool ShouldPreserveAcronym(string value)
    {
        var letters = value.Where(char.IsLetter).ToArray();
        if (letters.Length < 2)
        {
            return false;
        }

        return value.Length >= 2 && value.Any(char.IsLetter);
    }

    private static bool LooksLikeAllCapsProse(string text)
    {
        var letters = 0;
        var lowercaseLetters = 0;
        foreach (var character in text)
        {
            if (!char.IsLetter(character))
            {
                continue;
            }

            letters++;
            if (char.IsLower(character))
            {
                lowercaseLetters++;
            }
        }

        if (letters < 6 || lowercaseLetters > 0)
        {
            return false;
        }

        var wordCount = 0;
        var hasLongWord = false;
        foreach (Match match in UppercaseWordRegex.Matches(text))
        {
            var letterCount = match.Value.Count(char.IsLetter);
            if (letterCount == 0)
            {
                continue;
            }

            wordCount++;
            hasLongWord |= letterCount >= 4;
        }

        return wordCount >= 2 && hasLongWord;
    }

    private static bool ContainsLetterOutsideIgnoredTerms(ReadOnlySpan<char> value)
    {
        foreach (var character in value)
        {
            if (char.IsLetter(character))
            {
                return true;
            }
        }

        return false;
    }

    private static string CreateToken(int index)
    {
        return $"LXVERSEKEEP{index}TOKEN";
    }

    private sealed record TermMatch(int Start, int Length, int Priority);
}

public sealed record ProtectedTranslationText(
    string Text,
    IReadOnlyDictionary<string, string> Replacements)
{
    public TextTranslationResult Restore(TextTranslationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result with
        {
            SourceText = Restore(result.SourceText),
            TranslatedText = Restore(result.TranslatedText)
        };
    }

    public string Restore(string text)
    {
        if (Replacements.Count == 0 || string.IsNullOrEmpty(text))
        {
            return text;
        }

        var restored = text;
        foreach (var (token, value) in Replacements)
        {
            restored = Regex.Replace(
                restored,
                Regex.Escape(token),
                _ => value,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        return restored;
    }
}
