using LexVerse.Core.Ocr;

namespace LexVerse.Core.Pipeline;

public sealed record TranslatedTextBlock(
    string Id,
    BoundingBox Bounds,
    string SourceText,
    string TranslatedText,
    double FontSize);
