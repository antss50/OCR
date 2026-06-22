using System.Diagnostics;
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
        var totalTimer = Stopwatch.StartNew();
        var captureTimer = Stopwatch.StartNew();
        var frame = await _captureSession.CaptureFrameAsync(cancellationToken);
        captureTimer.Stop();

        var changeDetectionTimer = Stopwatch.StartNew();
        var changed = _changeDetector.HasChanged(frame);
        changeDetectionTimer.Stop();

        if (!changed)
        {
            totalTimer.Stop();
            return new ScreenOcrPipelineResult(
                frame,
                false,
                null,
                new ScreenOcrPipelineTiming(
                    captureTimer.Elapsed,
                    changeDetectionTimer.Elapsed,
                    TimeSpan.Zero,
                    TimeSpan.Zero,
                    totalTimer.Elapsed));
        }

        var ocrResult = await RecognizeFrameOrRegionsAsync(frame, cancellationToken);
        totalTimer.Stop();

        return new ScreenOcrPipelineResult(
            frame,
            true,
            ocrResult.Result,
            new ScreenOcrPipelineTiming(
                captureTimer.Elapsed,
                changeDetectionTimer.Elapsed,
                ocrResult.RegionMask,
                ocrResult.Ocr,
                totalTimer.Elapsed));
    }

    private async Task<TimedOcrResult> RecognizeFrameOrRegionsAsync(
        CapturedFrame frame,
        CancellationToken cancellationToken)
    {
        var regions = _regionProvider?.GetRegions(frame)
            .Where(region => region.Width > 0 && region.Height > 0)
            .ToArray();

        if (regions is null || regions.Length == 0)
        {
            var fullFrameOcrTimer = Stopwatch.StartNew();
            var fullFrameOcrResult = await _ocrService.RecognizeAsync(frame, cancellationToken);
            fullFrameOcrTimer.Stop();
            return new TimedOcrResult(fullFrameOcrResult, TimeSpan.Zero, fullFrameOcrTimer.Elapsed);
        }

        var regionMaskTimer = Stopwatch.StartNew();
        var maskedFrame = CapturedFrameRegionMasker.KeepRegions(
            frame,
            regions
                .Select(region => new RegionBounds(region.X, region.Y, region.Width, region.Height))
                .ToArray());
        regionMaskTimer.Stop();

        var ocrTimer = Stopwatch.StartNew();
        var ocrResult = await _ocrService.RecognizeAsync(maskedFrame, cancellationToken);
        ocrTimer.Stop();

        return new TimedOcrResult(ocrResult, regionMaskTimer.Elapsed, ocrTimer.Elapsed);
    }

    private sealed record TimedOcrResult(OcrResult Result, TimeSpan RegionMask, TimeSpan Ocr);
}
