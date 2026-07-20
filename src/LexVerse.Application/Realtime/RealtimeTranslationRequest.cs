using LexVerse.Core.Geometry;
using LexVerse.Core.Pipeline;
using LexVerse.Core.Product;

namespace LexVerse.Application.Realtime;

public sealed record RealtimeTranslationRequest
{
    public RealtimeTranslationRequest(
        RealtimeCaptureKind captureKind,
        ScreenRect targetScreenRect,
        string ocrLanguageTag,
        RealtimeTranslationOptions options)
    {
        targetScreenRect.Validate();
        ArgumentException.ThrowIfNullOrWhiteSpace(ocrLanguageTag);
        ArgumentNullException.ThrowIfNull(options);
        if (options.OcrInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "OCR interval must be positive.");
        }

        CaptureKind = captureKind;
        TargetScreenRect = targetScreenRect;
        OcrLanguageTag = ocrLanguageTag.Trim();
        Options = options;
    }

    public RealtimeCaptureKind CaptureKind { get; }

    public ScreenRect TargetScreenRect { get; }

    public string OcrLanguageTag { get; }

    public RealtimeTranslationOptions Options { get; }

    public FeatureKey RequiredFeature => CaptureKind == RealtimeCaptureKind.Region
        ? ProductFeatures.RegionTranslation
        : ProductFeatures.FullScreenTranslation;
}
