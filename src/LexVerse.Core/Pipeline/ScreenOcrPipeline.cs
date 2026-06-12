using LexVerse.Core.Ocr;
using LexVerse.Core.ScreenCapture;

namespace LexVerse.Core.Pipeline;

public sealed class ScreenOcrPipeline(
    IScreenCaptureSession captureSession,
    IFrameChangeDetector changeDetector,
    IOcrService ocrService)
{
    public async Task<ScreenOcrPipelineResult> CaptureAndRecognizeAsync(CancellationToken cancellationToken = default)
    {
        var frame = await captureSession.CaptureFrameAsync(cancellationToken);
        var changed = changeDetector.HasChanged(frame);

        if (!changed)
        {
            return new ScreenOcrPipelineResult(frame, false, null);
        }

        var ocrResult = await ocrService.RecognizeAsync(frame, cancellationToken);
        return new ScreenOcrPipelineResult(frame, true, ocrResult);
    }
}
