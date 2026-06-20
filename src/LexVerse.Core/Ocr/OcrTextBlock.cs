namespace LexVerse.Core.Ocr;

public sealed record OcrTextBlock(
    string Text,
    BoundingBox Bounds,
    IReadOnlyList<OcrWord> Words,
    double FontSize);
