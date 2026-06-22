namespace LexVerse.Core.Pipeline;

public sealed record RealtimeTranslationPipelineTiming(
    TimeSpan Capture,
    TimeSpan ChangeDetection,
    TimeSpan RegionMask,
    TimeSpan Ocr,
    TimeSpan Translation,
    TimeSpan Total,
    int CacheHits,
    int CacheMisses);
