namespace LexVerse.Core.Translation;

public sealed record OcrTranslationBatch(
    string SourceLanguage,
    string TargetLanguage,
    IReadOnlyList<TranslationTextBlock> Blocks);
