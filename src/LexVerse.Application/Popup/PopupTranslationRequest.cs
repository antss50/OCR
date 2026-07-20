using LexVerse.Core.Translation;

namespace LexVerse.Application.Popup;

public sealed record PopupTranslationRequest
{
    public PopupTranslationRequest(
        string targetLanguage,
        string? sourceLanguage = null,
        TranslationPromptOptions? promptOptions = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetLanguage);

        TargetLanguage = NormalizeLanguage(targetLanguage);
        SourceLanguage = string.IsNullOrWhiteSpace(sourceLanguage)
            ? null
            : NormalizeLanguage(sourceLanguage);
        PromptOptions = promptOptions ?? TranslationPromptOptions.Empty;
    }

    public string TargetLanguage { get; }

    public string? SourceLanguage { get; }

    public TranslationPromptOptions PromptOptions { get; }

    private static string NormalizeLanguage(string language)
    {
        var normalized = language.Trim();
        var separator = normalized.IndexOf('-');
        return separator <= 0 ? normalized : normalized[..separator];
    }
}
