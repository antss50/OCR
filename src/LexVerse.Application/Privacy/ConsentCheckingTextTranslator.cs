using LexVerse.Core.Translation;

namespace LexVerse.Application.Privacy;

/// <summary>Single enforcement point shared by Popup and every realtime translation mode.</summary>
public sealed class ConsentCheckingTextTranslator(
    ITextTranslator inner,
    IPrivacyPreferencesService preferences) : ITextTranslator
{
    public Task<TextTranslationResult> TranslateAsync(
        string text,
        string targetLanguage,
        string? sourceLanguage = null,
        CancellationToken cancellationToken = default)
    {
        EnsureConsent();
        return inner.TranslateAsync(text, targetLanguage, sourceLanguage, cancellationToken);
    }

    public Task<TextTranslationResult> TranslateAsync(
        string text,
        string targetLanguage,
        string? sourceLanguage,
        TranslationPromptOptions? promptOptions,
        CancellationToken cancellationToken = default)
    {
        EnsureConsent();
        return inner.TranslateAsync(text, targetLanguage, sourceLanguage, promptOptions, cancellationToken);
    }

    public Task<IReadOnlyList<TextTranslationResult>> TranslateBatchAsync(
        IReadOnlyList<string> texts,
        string targetLanguage,
        string? sourceLanguage = null,
        CancellationToken cancellationToken = default)
    {
        EnsureConsent();
        return inner.TranslateBatchAsync(texts, targetLanguage, sourceLanguage, cancellationToken);
    }

    public Task<IReadOnlyList<TextTranslationResult>> TranslateBatchAsync(
        IReadOnlyList<string> texts,
        string targetLanguage,
        string? sourceLanguage,
        TranslationPromptOptions? promptOptions,
        CancellationToken cancellationToken = default)
    {
        EnsureConsent();
        return inner.TranslateBatchAsync(
            texts, targetLanguage, sourceLanguage, promptOptions, cancellationToken);
    }

    private void EnsureConsent()
    {
        if (!preferences.Current.AllowRemoteTextProcessing)
        {
            throw new RemoteProcessingConsentRequiredException();
        }
    }
}
