namespace LexVerse.Core.Translation;

public sealed record TextTranslationResult(
    string SourceText,
    string TranslatedText,
    string TargetLanguage,
    string? SourceLanguage);
