using LexVerse.Core.Imaging;
using LexVerse.Core.Ocr;

namespace LexVerse.Core.Pipeline;

public sealed record ScreenOcrPipelineResult(
    CapturedFrame Frame,
    bool Changed,
    OcrResult? OcrResult,
    ScreenOcrPipelineTiming Timing)
{
    public string? OcrInputFingerprint { get; init; }

    public bool UsedCachedOcrResult { get; init; }
}
