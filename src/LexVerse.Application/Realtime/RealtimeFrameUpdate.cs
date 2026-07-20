using LexVerse.Core.Overlay;
using LexVerse.Core.Pipeline;

namespace LexVerse.Application.Realtime;

public sealed record RealtimeFrameUpdate(
    OverlayRenderFrame OverlayFrame,
    RealtimeTranslationPipelineTiming Timing,
    int OcrBlockCount,
    int TranslatedBlockCount,
    bool FrameChanged,
    bool UsedCachedTranslation)
{
    public bool PerformanceBudgetExceeded { get; init; }
}
