namespace LexVerse.Core.Pipeline;

public sealed record OcrRegion(
    string Id,
    int X,
    int Y,
    int Width,
    int Height,
    OcrProcessingMode Mode);
