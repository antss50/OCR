namespace LexVerse.Core.Pipeline;

public sealed record RealtimeTranslationPipelineResult(
    ScreenOcrPipelineResult Ocr,
    IReadOnlyList<TranslatedTextBlock> TranslatedBlocks);
