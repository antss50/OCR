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

        var maskedFrame = CapturedFrameRegionMasker.KeepRegions(
            frame,
            regions
                .Select(region => new RegionBounds(region.X, region.Y, region.Width, region.Height))
                .ToArray());

        return await _ocrService.RecognizeAsync(maskedFrame, cancellationToken);
    }
}
