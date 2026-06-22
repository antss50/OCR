using LexVerse.Core.Ocr;

namespace LexVerse.OCR;

public sealed record OcrLayoutDebugResult(
    IReadOnlyList<OcrTextBlock> LineBlocksBeforeGrouping,
    OcrResult Result);
