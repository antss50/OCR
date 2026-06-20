using LexVerse.Core.Ocr;

namespace LexVerse.Core.Translation;

public sealed record TranslationTextBlock(
    string Id,
    BoundingBox Bounds,
    string SourceText,
    double FontSize);
