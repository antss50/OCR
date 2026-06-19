namespace LexVerse.Core.Translation;

public interface ITextTranslator
{
    Task<TextTranslationResult> TranslateAsync(
        string text,
        string targetLanguage,
        string? sourceLanguage = null,
        CancellationToken cancellationToken = default);
}
