namespace LexVerse.Core.Translation;

public interface ITextTranslator
{
    Task<TextTranslationResult> TranslateAsync(
        string text,
        string targetLanguage,
        string? sourceLanguage = null,
        CancellationToken cancellationToken = default);

    Task<TextTranslationResult> TranslateAsync(
        string text,
        string targetLanguage,
        string? sourceLanguage,
        TranslationPromptOptions? promptOptions,
        CancellationToken cancellationToken = default)
    {
        return TranslateAsync(text, targetLanguage, sourceLanguage, cancellationToken);
    }

    async Task<IReadOnlyList<TextTranslationResult>> TranslateBatchAsync(
        IReadOnlyList<string> texts,
        string targetLanguage,
        string? sourceLanguage = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(texts);

        var results = new List<TextTranslationResult>(texts.Count);
        foreach (var text in texts)
        {
            results.Add(await TranslateAsync(text, targetLanguage, sourceLanguage, cancellationToken));
        }

        return results;
    }

    async Task<IReadOnlyList<TextTranslationResult>> TranslateBatchAsync(
        IReadOnlyList<string> texts,
        string targetLanguage,
        string? sourceLanguage,
        TranslationPromptOptions? promptOptions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(texts);

        var results = new List<TextTranslationResult>(texts.Count);
        foreach (var text in texts)
        {
            results.Add(await TranslateAsync(
                text,
                targetLanguage,
                sourceLanguage,
                promptOptions,
                cancellationToken));
        }

        return results;
    }
}
