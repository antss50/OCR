using LexVerse.Core.Imaging;
using LexVerse.Core.Ocr;
using LexVerse.Core.ScreenCapture;

namespace LexVerse.Core.Pipeline;

public sealed class ScreenOcrPipeline
{
    private readonly IScreenCaptureSession _captureSession;
    private readonly IFrameChangeDetector _changeDetector;
    private readonly IOcrService _ocrService;
    private readonly IOcrRegionProvider? _regionProvider;

    public ScreenOcrPipeline(
        IScreenCaptureSession captureSession,
        IFrameChangeDetector changeDetector,
        IOcrService ocrService,
        IOcrRegionProvider? regionProvider = null)
    {
        _captureSession = captureSession;
        _changeDetector = changeDetector;
        _ocrService = ocrService;
        _regionProvider = regionProvider;
    }

    public async Task<ScreenOcrPipelineResult> CaptureAndRecognizeAsync(CancellationToken cancellationToken = default)
    {
        var frame = await _captureSession.CaptureFrameAsync(cancellationToken);
        var changed = _changeDetector.HasChanged(frame);

        if (!changed)
        {
            return new ScreenOcrPipelineResult(frame, false, null);
        }

        var ocrResult = await RecognizeFrameOrRegionsAsync(frame, cancellationToken);
        return new ScreenOcrPipelineResult(frame, true, ocrResult);
    }

    private async Task<OcrResult> RecognizeFrameOrRegionsAsync(
        CapturedFrame frame,
        CancellationToken cancellationToken)
    {
        var regions = _regionProvider?.GetRegions(frame)
            .Where(region => region.Width > 0 && region.Height > 0)
            .ToArray();

        if (regions is null || regions.Length == 0)
        {
            return await _ocrService.RecognizeAsync(frame, cancellationToken);
        }

        var results = new List<OcrResult>();
        foreach (var region in regions)
        {
            var croppedFrame = CapturedFrameCropper.Crop(frame, region.X, region.Y, region.Width, region.Height);
            var regionResult = await _ocrService.RecognizeAsync(croppedFrame, cancellationToken);
            results.Add(OffsetResult(regionResult, region.X, region.Y));
        }

        return MergeResults(results);
    }

    private static OcrResult OffsetResult(OcrResult result, int offsetX, int offsetY)
    {
        var blocks = result.Blocks
            .Select(block =>
            {
                var words = block.Words
                    .Select(word => word with { Bounds = OffsetBounds(word.Bounds, offsetX, offsetY) })
                    .ToArray();

                return block with
                {
                    Bounds = OffsetBounds(block.Bounds, offsetX, offsetY),
                    Words = words
                };
            })
            .ToArray();

        return result with { Blocks = blocks };
    }

    private static OcrResult MergeResults(IReadOnlyList<OcrResult> results)
    {
        var blocks = results
            .SelectMany(result => result.Blocks)
            .OrderBy(block => block.Bounds.Y)
            .ThenBy(block => block.Bounds.X)
            .ToArray();

        var firstResult = results[0];
        return firstResult with
        {
            Blocks = blocks,
            RecognizedAt = DateTimeOffset.UtcNow
        };
    }

    private static BoundingBox OffsetBounds(BoundingBox bounds, int offsetX, int offsetY)
    {
        return new BoundingBox(bounds.X + offsetX, bounds.Y + offsetY, bounds.Width, bounds.Height);
    }
}
