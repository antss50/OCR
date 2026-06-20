namespace LexVerse.Core.Translation;

public interface ITranslationService
{
    Task<string> TranslateAsync(
        string text,
        string sourceLanguage,
        string targetLanguage,
        CancellationToken cancellationToken = default);
}
