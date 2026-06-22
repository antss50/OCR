using LexVerse.Core.Geometry;

namespace LexVerse.Core.Pipeline;

public sealed record PipelineTrace(
    Guid FrameId,
    string CaptureMode,
    FrameGeometry Geometry,
    TimeSpan Capture,
    TimeSpan FrameDiff,
    TimeSpan Ocr,
    TimeSpan Translate,
    TimeSpan Render,
    TimeSpan Total,
    int OcrBlockCount,
    int TranslateRequestCount,
    int CacheHitCount,
    int CacheMissCount,
    bool WasCancelled,
    string? Error);
