using LexVerse.Core.Ocr;
using LexVerse.Core.ScreenCapture;
using LexVerse.Core.Translation;

namespace LexVerse.Core.Pipeline;

public sealed class RealtimeTranslationPipeline
{
    private readonly ScreenOcrPipeline _ocrPipeline;
    private readonly ITextTranslator _translator;
    private readonly ITranslationCache _translationCache;
    private readonly RealtimeTranslationOptions _options;

    public RealtimeTranslationPipeline(
        IScreenCaptureSession captureSession,
        IFrameChangeDetector changeDetector,
        IOcrService ocrService,
        ITextTranslator translator,
        ITranslationCache translationCache,
        RealtimeTranslationOptions options,
        IOcrRegionProvider? regionProvider = null)
    {
        _ocrPipeline = new ScreenOcrPipeline(captureSession, changeDetector, ocrService, regionProvider);
        _translator = translator;
        _translationCache = translationCache;
        _options = options;
    }

    public async Task<RealtimeTranslationPipelineResult> CaptureRecognizeAndTranslateAsync(
        CancellationToken cancellationToken = default)
    {
        var ocrResult = await _ocrPipeline.CaptureAndRecognizeAsync(cancellationToken);
        if (!ocrResult.Changed || ocrResult.OcrResult is null)
        {
            return new RealtimeTranslationPipelineResult(ocrResult, []);
        }

        var translatedBlocks = await TranslateBlocksAsync(ocrResult.OcrResult, cancellationToken);
        return new RealtimeTranslationPipelineResult(ocrResult, translatedBlocks);
    }

    private async Task<IReadOnlyList<TranslatedTextBlock>> TranslateBlocksAsync(
        OcrResult ocrResult,
        CancellationToken cancellationToken)
    {
        var batch = OcrTranslationBatchFactory.Create(ocrResult, _options.TargetLanguage);
        var translatedBlocks = new List<TranslatedTextBlock>();
        var sourceLanguage = NormalizeSourceLanguage(batch.SourceLanguage, _options.SourceLanguage);

        foreach (var block in batch.Blocks.Where(block => !string.IsNullOrWhiteSpace(block.SourceText)))
        {
            var normalizedText = NormalizeText(block.SourceText);
            var cacheKey = new TranslationCacheKey(
                normalizedText,
                sourceLanguage ?? "auto",
                batch.TargetLanguage,
                _options.Mode);

            if (!_translationCache.TryGet(cacheKey, out var translatedText))
            {
                var translation = await _translator.TranslateAsync(
                    normalizedText,
                    batch.TargetLanguage,
                    sourceLanguage,
                    cancellationToken);

                translatedText = translation.TranslatedText;
                _translationCache.Set(cacheKey, translatedText);
            }

            translatedBlocks.Add(new TranslatedTextBlock(
                block.Id,
                block.Bounds,
                block.SourceText,
                translatedText,
                block.FontSize));
        }

        return translatedBlocks;
    }

    private static string NormalizeText(string text)
    {
        return string.Join(' ', text.Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    private static string? NormalizeSourceLanguage(string ocrLanguage, string configuredSourceLanguage)
    {
        if (configuredSourceLanguage.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var sourceLanguage = string.IsNullOrWhiteSpace(configuredSourceLanguage)
            ? ocrLanguage
            : configuredSourceLanguage;

        var separatorIndex = sourceLanguage.IndexOf('-');
        return separatorIndex > 0
            ? sourceLanguage[..separatorIndex]
            : sourceLanguage;
    }
}
