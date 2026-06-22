namespace LexVerse.Core.Pipeline;

public sealed record ScreenOcrPipelineTiming(
    TimeSpan Capture,
    TimeSpan ChangeDetection,
    TimeSpan RegionMask,
    TimeSpan Ocr,
    TimeSpan Total);
