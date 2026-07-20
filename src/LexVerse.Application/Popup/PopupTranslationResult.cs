using LexVerse.Core.Translation;

namespace LexVerse.Application.Popup;

public sealed record PopupTranslationResult
{
    private PopupTranslationResult(string? sourceText, TextTranslationResult? translation)
    {
        SourceText = sourceText;
        Translation = translation;
    }

    public static PopupTranslationResult NoSelection { get; } = new(null, null);

    public static PopupTranslationResult Success(string sourceText, TextTranslationResult translation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceText);
        ArgumentNullException.ThrowIfNull(translation);
        return new PopupTranslationResult(sourceText.Trim(), translation);
    }

    public string? SourceText { get; }

    public TextTranslationResult? Translation { get; }

    public bool HasSelection => Translation is not null;
}
