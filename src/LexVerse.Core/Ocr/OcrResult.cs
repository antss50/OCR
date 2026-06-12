namespace LexVerse.Core.Ocr;

public sealed record OcrResult(
    IReadOnlyList<OcrTextBlock> Blocks,
    string Language,
    double TextAngle,
    DateTimeOffset RecognizedAt);
