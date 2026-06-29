using System.Diagnostics;
using System.Security.Cryptography;
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
    private OcrCacheEntry? _lastOcrCacheEntry;

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
                _lastOcrCacheEntry?.Result,
                new ScreenOcrPipelineTiming(
                    captureTimer.Elapsed,
                    changeDetectionTimer.Elapsed,
                    TimeSpan.Zero,
                    TimeSpan.Zero,
                    totalTimer.Elapsed))
            {
                OcrInputFingerprint = _lastOcrCacheEntry?.Fingerprint,
                UsedCachedOcrResult = _lastOcrCacheEntry is not null
            };
        }

        var ocrResult = await RecognizeFrameOrRegionsAsync(frame, cancellationToken);
        totalTimer.Stop();

        return new ScreenOcrPipelineResult(
            frame,
            !ocrResult.UsedCachedResult,
            ocrResult.Result,
            new ScreenOcrPipelineTiming(
                captureTimer.Elapsed,
                changeDetectionTimer.Elapsed,
                ocrResult.RegionMask,
                ocrResult.Ocr,
                totalTimer.Elapsed))
        {
            OcrInputFingerprint = ocrResult.OcrInputFingerprint,
            UsedCachedOcrResult = ocrResult.UsedCachedResult
        };
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
            return await RecognizePreparedFrameAsync(frame, TimeSpan.Zero, cancellationToken);
        }

        var regionMaskTimer = Stopwatch.StartNew();
        var maskedFrame = CapturedFrameRegionMasker.KeepRegions(
            frame,
            regions
                .Select(region => new RegionBounds(region.X, region.Y, region.Width, region.Height))
                .ToArray());
        regionMaskTimer.Stop();

        return await RecognizePreparedFrameAsync(maskedFrame, regionMaskTimer.Elapsed, cancellationToken);
    }

    private async Task<TimedOcrResult> RecognizePreparedFrameAsync(
        CapturedFrame ocrInputFrame,
        TimeSpan regionMask,
        CancellationToken cancellationToken)
    {
        var fingerprint = CreateOcrInputFingerprint(ocrInputFrame);
        if (_lastOcrCacheEntry is not null && _lastOcrCacheEntry.Matches(ocrInputFrame, fingerprint))
        {
            return new TimedOcrResult(
                _lastOcrCacheEntry.Result,
                regionMask,
                TimeSpan.Zero,
                true,
                fingerprint);
        }

        var ocrTimer = Stopwatch.StartNew();
        var ocrResult = await _ocrService.RecognizeAsync(ocrInputFrame, cancellationToken);
        ocrTimer.Stop();

        _lastOcrCacheEntry = OcrCacheEntry.Create(ocrInputFrame, fingerprint, ocrResult);

        return new TimedOcrResult(ocrResult, regionMask, ocrTimer.Elapsed, false, fingerprint);
    }

    private static string CreateOcrInputFingerprint(CapturedFrame frame)
    {
        if (frame.Pixels.Length < frame.ExpectedByteCount)
        {
            throw new ArgumentException("Frame pixel buffer is smaller than width/height/stride metadata.", nameof(frame));
        }

        var hash = SHA256.HashData(frame.Pixels.AsSpan(0, frame.ExpectedByteCount));
        return Convert.ToHexString(hash);
    }

    private sealed record TimedOcrResult(
        OcrResult Result,
        TimeSpan RegionMask,
        TimeSpan Ocr,
        bool UsedCachedResult,
        string OcrInputFingerprint);

    private sealed record OcrCacheEntry(
        string Fingerprint,
        int Width,
        int Height,
        int Stride,
        PixelFormat PixelFormat,
        OcrResult Result)
    {
        public static OcrCacheEntry Create(CapturedFrame frame, string fingerprint, OcrResult result)
        {
            return new OcrCacheEntry(
                fingerprint,
                frame.Width,
                frame.Height,
                frame.Stride,
                frame.PixelFormat,
                result);
        }

        public bool Matches(CapturedFrame frame, string fingerprint)
        {
            return Fingerprint.Equals(fingerprint, StringComparison.Ordinal)
                && Width == frame.Width
                && Height == frame.Height
                && Stride == frame.Stride
                && PixelFormat == frame.PixelFormat;
        }
    }
}
