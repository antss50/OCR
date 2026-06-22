using System.Diagnostics;
using LexVerse.Core.Geometry;
using LexVerse.Core.Ocr;
using LexVerse.Core.Overlay;
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
        CancellationToken cancellationToken = default,
        Func<RealtimeTranslationPipelineResult, CancellationToken, Task>? partialResultHandler = null)
    {
        var totalTimer = Stopwatch.StartNew();
        var ocrResult = await _ocrPipeline.CaptureAndRecognizeAsync(cancellationToken);
        if (!ocrResult.Changed || ocrResult.OcrResult is null)
        {
            totalTimer.Stop();
            return new RealtimeTranslationPipelineResult(
                ocrResult,
                [],
                CreateTiming(ocrResult.Timing, TimeSpan.Zero, totalTimer.Elapsed, 0, 0),
                CreateOverlayFrame(ocrResult, [], TimeSpan.Zero, totalTimer.Elapsed, 0, 0));
        }

        var translationTimer = Stopwatch.StartNew();
        var translatedBlocks = await TranslateBlocksAsync(
            ocrResult,
            translationTimer,
            totalTimer,
            partialResultHandler,
            cancellationToken);
        translationTimer.Stop();
        totalTimer.Stop();

        return new RealtimeTranslationPipelineResult(
            ocrResult,
            translatedBlocks.Blocks,
            CreateTiming(
                ocrResult.Timing,
                translationTimer.Elapsed,
                totalTimer.Elapsed,
                translatedBlocks.CacheHits,
                translatedBlocks.CacheMisses),
            CreateOverlayFrame(
                ocrResult,
                translatedBlocks.Blocks,
                translationTimer.Elapsed,
                totalTimer.Elapsed,
                translatedBlocks.CacheHits,
                translatedBlocks.CacheMisses));
    }

    private async Task<TimedTranslationResult> TranslateBlocksAsync(
        ScreenOcrPipelineResult ocrResult,
        Stopwatch translationTimer,
        Stopwatch totalTimer,
        Func<RealtimeTranslationPipelineResult, CancellationToken, Task>? partialResultHandler,
        CancellationToken cancellationToken)
    {
        var batch = OcrTranslationBatchFactory.Create(ocrResult.OcrResult!, _options.TargetLanguage);
        var translatedByIndex = new SortedDictionary<int, TranslatedTextBlock>();
        var pendingByKey = new Dictionary<TranslationCacheKey, PendingTranslation>();
        var sourceLanguage = NormalizeSourceLanguage(batch.SourceLanguage, _options.SourceLanguage);
        var cacheHits = 0;
        var cacheMisses = 0;
        var gate = new object();
        using var emitGate = new SemaphoreSlim(1, 1);

        foreach (var (block, index) in batch.Blocks
                     .Select((block, index) => (block, index))
                     .Where(item => !string.IsNullOrWhiteSpace(item.block.SourceText)))
        {
            var normalizedText = NormalizeText(block.SourceText);
            var cacheKey = new TranslationCacheKey(
                normalizedText,
                sourceLanguage ?? "auto",
                batch.TargetLanguage,
                _options.Mode);

            if (ShouldUseSourceText(normalizedText, sourceLanguage, batch.TargetLanguage))
            {
                translatedByIndex[index] = CreateTranslatedBlock(block, normalizedText);
                continue;
            }

            if (_translationCache.TryGet(cacheKey, out var translatedText))
            {
                cacheHits++;
                translatedByIndex[index] = CreateTranslatedBlock(block, translatedText);
                continue;
            }

            cacheMisses++;
            if (!pendingByKey.TryGetValue(cacheKey, out var pending))
            {
                pending = new PendingTranslation(cacheKey, normalizedText, []);
                pendingByKey[cacheKey] = pending;
            }

            pending.Blocks.Add(new PendingTranslationBlock(index, block));
        }

        if (translatedByIndex.Count > 0)
        {
            await EmitPartialAsync();
        }

        var pendingItems = pendingByKey.Values.ToArray();
        if (pendingItems.Length > 0)
        {
            var batchSize = Math.Max(1, _options.MaxTranslationBatchSize);
            var maxParallel = Math.Max(1, _options.MaxParallelTranslationRequests);
            using var translationGate = new SemaphoreSlim(maxParallel, maxParallel);

            var tasks = pendingItems
                .Chunk(batchSize)
                .Select(async chunk =>
                {
                    await translationGate.WaitAsync(cancellationToken);
                    try
                    {
                        var translations = await _translator.TranslateBatchAsync(
                            chunk.Select(item => item.NormalizedText).ToArray(),
                            batch.TargetLanguage,
                            sourceLanguage,
                            cancellationToken);

                        lock (gate)
                        {
                            for (var i = 0; i < chunk.Length; i++)
                            {
                                var pending = chunk[i];
                                var translatedText = translations[i].TranslatedText;
                                _translationCache.Set(pending.CacheKey, translatedText);

                                foreach (var pendingBlock in pending.Blocks)
                                {
                                    translatedByIndex[pendingBlock.Index] = CreateTranslatedBlock(
                                        pendingBlock.Block,
                                        translatedText);
                                }
                            }
                        }

                        await EmitPartialAsync();
                    }
                    finally
                    {
                        translationGate.Release();
                    }
                });

            await Task.WhenAll(tasks);
        }

        return new TimedTranslationResult(
            translatedByIndex.Values.ToArray(),
            cacheHits,
            cacheMisses);

        async Task EmitPartialAsync()
        {
            if (partialResultHandler is null)
            {
                return;
            }

            await emitGate.WaitAsync(cancellationToken);
            try
            {
                IReadOnlyList<TranslatedTextBlock> snapshot;
                lock (gate)
                {
                    snapshot = translatedByIndex.Values.ToArray();
                }

                await partialResultHandler(
                    new RealtimeTranslationPipelineResult(
                        ocrResult,
                        snapshot,
                        CreateTiming(
                            ocrResult.Timing,
                            translationTimer.Elapsed,
                            totalTimer.Elapsed,
                            cacheHits,
                            cacheMisses),
                        CreateOverlayFrame(
                            ocrResult,
                            snapshot,
                            translationTimer.Elapsed,
                            totalTimer.Elapsed,
                            cacheHits,
                            cacheMisses)),
                    cancellationToken);
            }
            finally
            {
                emitGate.Release();
            }
        }
    }

    private static TranslatedTextBlock CreateTranslatedBlock(
        TranslationTextBlock block,
        string translatedText)
    {
        return new TranslatedTextBlock(
            block.Id,
            block.Bounds,
            block.SourceText,
            translatedText,
            block.FontSize);
    }

    private static RealtimeTranslationPipelineTiming CreateTiming(
        ScreenOcrPipelineTiming ocrTiming,
        TimeSpan translation,
        TimeSpan total,
        int cacheHits,
        int cacheMisses)
    {
        return new RealtimeTranslationPipelineTiming(
            ocrTiming.Capture,
            ocrTiming.ChangeDetection,
            ocrTiming.RegionMask,
            ocrTiming.Ocr,
            translation,
            total,
            cacheHits,
            cacheMisses);
    }

    private OverlayRenderFrame CreateOverlayFrame(
        ScreenOcrPipelineResult ocrResult,
        IReadOnlyList<TranslatedTextBlock> translatedBlocks,
        TimeSpan translation,
        TimeSpan total,
        int cacheHits,
        int cacheMisses)
    {
        var mapper = new CoordinateMapper(ocrResult.Frame.Geometry);
        var items = translatedBlocks
            .Select(block =>
            {
                var sourceRect = ClampToSource(mapper.FrameToScreen(new FrameRect(
                    block.Bounds.X,
                    block.Bounds.Y,
                    block.Bounds.Width,
                    block.Bounds.Height)), ocrResult.Frame.Geometry.SourceScreenRect);

                return new OverlayTextItem(
                    block.SourceText,
                    block.TranslatedText,
                    sourceRect,
                    1,
                    EstimateScreenFontSize(block, sourceRect),
                    $"{sourceRect.X:0},{sourceRect.Y:0} {sourceRect.Width:0}x{sourceRect.Height:0} v{ocrResult.Frame.Geometry.Version}",
                    sourceRect);
            })
            .ToArray();

        var trace = new PipelineTrace(
            ocrResult.Frame.FrameId,
            ocrResult.Frame.Source.Kind.ToString(),
            ocrResult.Frame.Geometry,
            ocrResult.Timing.Capture,
            ocrResult.Timing.ChangeDetection,
            ocrResult.Timing.Ocr,
            translation,
            TimeSpan.Zero,
            total,
            ocrResult.OcrResult?.Blocks.Count ?? 0,
            translatedBlocks.Count,
            cacheHits,
            cacheMisses,
            false,
            null);

        return new OverlayRenderFrame(
            items,
            trace,
            ocrResult.Frame.Geometry.Version,
            DateTimeOffset.UtcNow);
    }

    private static ScreenRect ClampToSource(ScreenRect rect, ScreenRect source)
    {
        var x = Math.Clamp(rect.X, source.X, source.Right);
        var y = Math.Clamp(rect.Y, source.Y, source.Bottom);
        var right = Math.Clamp(rect.Right, source.X, source.Right);
        var bottom = Math.Clamp(rect.Bottom, source.Y, source.Bottom);

        return new ScreenRect(
            x,
            y,
            Math.Max(1, right - x),
            Math.Max(1, bottom - y));
    }

    private static double EstimateScreenFontSize(TranslatedTextBlock block, ScreenRect sourceRect)
    {
        if (block.FontSize <= 0 || block.Bounds.Height <= 0)
        {
            return Math.Max(12, sourceRect.Height * 0.55);
        }

        var scaleY = sourceRect.Height / block.Bounds.Height;
        return block.FontSize * scaleY;
    }

    private static string NormalizeText(string text)
    {
        return string.Join(' ', text.Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    private static bool ShouldUseSourceText(
        string normalizedText,
        string? sourceLanguage,
        string targetLanguage)
    {
        if (!ContainsLetter(normalizedText))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(sourceLanguage)
            && sourceLanguage.Equals(targetLanguage, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsLetter(string text)
    {
        foreach (var character in text)
        {
            if (char.IsLetter(character))
            {
                return true;
            }
        }

        return false;
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

    private sealed record TimedTranslationResult(
        IReadOnlyList<TranslatedTextBlock> Blocks,
        int CacheHits,
        int CacheMisses);

    private sealed record PendingTranslation(
        TranslationCacheKey CacheKey,
        string NormalizedText,
        List<PendingTranslationBlock> Blocks);

    private sealed record PendingTranslationBlock(
        int Index,
        TranslationTextBlock Block);
}
