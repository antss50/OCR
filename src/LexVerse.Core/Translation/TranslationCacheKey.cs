using LexVerse.Core.Pipeline;

namespace LexVerse.Core.Translation;

public sealed record TranslationCacheKey(
    string NormalizedSourceText,
    string SourceLanguage,
    string TargetLanguage,
    OcrProcessingMode Mode,
    string TranslationPromptKey = "");
