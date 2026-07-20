using LexVerse.Application.Realtime;
using LexVerse.Core.Pipeline;

namespace LexVerse.Application.Diagnostics;

/// <summary>
/// Privacy-safe operational data. This contract intentionally has no arbitrary text,
/// OCR content, translation content, screen coordinates, window titles, or user identity.
/// </summary>
public sealed record OperationalEvent
{
    public required OperationalEventKind Kind { get; init; }

    public required OperationalOutcome Outcome { get; init; }

    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;

    public Guid? CorrelationId { get; init; }

    public TimeSpan? Duration { get; init; }

    public TimeSpan? CaptureDuration { get; init; }

    public TimeSpan? OcrDuration { get; init; }

    public TimeSpan? TranslationDuration { get; init; }

    public RealtimeCaptureKind? CaptureKind { get; init; }

    public OcrProcessingMode? ProcessingMode { get; init; }

    public PopupInputKind? PopupInputKind { get; init; }

    public OperationalFailureCode? FailureCode { get; init; }

    public int OcrBlockCount { get; init; }

    public int TranslatedBlockCount { get; init; }

    public int CacheHitCount { get; init; }

    public int CacheMissCount { get; init; }

    public int ModuleCount { get; init; }

    public int FrameCount { get; init; }

    public int PerformanceBudgetExceededCount { get; init; }

    public long WorkingSetBytes { get; init; }

    public bool FrameChanged { get; init; }

    public bool UsedCachedTranslation { get; init; }

    public bool PerformanceBudgetExceeded { get; init; }
}
