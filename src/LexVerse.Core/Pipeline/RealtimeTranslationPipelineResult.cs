using LexVerse.Core.Overlay;

namespace LexVerse.Core.Pipeline;

public sealed record RealtimeTranslationPipelineResult(
    ScreenOcrPipelineResult Ocr,
    IReadOnlyList<TranslatedTextBlock> TranslatedBlocks,
    RealtimeTranslationPipelineTiming Timing,
    OverlayRenderFrame OverlayFrame)
{
    public bool UsedCachedTranslation { get; init; }
}
