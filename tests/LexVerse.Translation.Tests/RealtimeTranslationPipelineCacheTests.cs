using LexVerse.Core.Capture;
using LexVerse.Core.Geometry;
using LexVerse.Core.Imaging;
using LexVerse.Core.Ocr;
using LexVerse.Core.Pipeline;
using LexVerse.Core.ScreenCapture;
using LexVerse.Core.Translation;
using Xunit;

namespace LexVerse.Translation.Tests;

public sealed class RealtimeTranslationPipelineCacheTests
{
    [Fact]
    public async Task CaptureRecognizeAndTranslateAsync_WhenFrameRepeats_ReusesOcrAndTranslation()
    {
        var firstFrame = CreateFrame();
        var repeatedFrame = CreateFrame();
        var captureSession = new FakeCaptureSession(firstFrame, repeatedFrame);
        var ocrService = new FakeOcrService();
        var translator = new FakeTextTranslator();
        var pipeline = new RealtimeTranslationPipeline(
            captureSession,
            new ExactFrameChangeDetector(),
            ocrService,
            translator,
            new InMemoryTranslationCache(),
            RealtimeTranslationOptions.GameDialogue);

        var firstResult = await pipeline.CaptureRecognizeAndTranslateAsync(TestContext.Current.CancellationToken);
        var secondResult = await pipeline.CaptureRecognizeAndTranslateAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, ocrService.CallCount);
        Assert.Equal(1, translator.CallCount);
        Assert.True(secondResult.Ocr.UsedCachedOcrResult);
        Assert.True(secondResult.UsedCachedTranslation);
        Assert.False(secondResult.Ocr.Changed);
        Assert.Single(firstResult.OverlayFrame.Items);
        Assert.Single(secondResult.OverlayFrame.Items);
        Assert.Equal("xin chao", secondResult.OverlayFrame.Items[0].TranslatedText);
    }

    private static CapturedFrame CreateFrame()
    {
        var width = 16;
        var height = 8;
        var stride = width * 4;
        var pixels = new byte[stride * height];

        for (var index = 0; index < pixels.Length; index += 4)
        {
            pixels[index] = 32;
            pixels[index + 1] = 64;
            pixels[index + 2] = 128;
            pixels[index + 3] = 255;
        }

        return new CapturedFrame(
            width,
            height,
            stride,
            PixelFormat.Bgra8,
            pixels,
            DateTimeOffset.UtcNow,
            new FrameGeometry(
                new PixelSize(width, height),
                new ScreenRect(0, 0, width, height),
                CoordinateSpace.FrameLocal,
                1,
                1,
                0),
            new CaptureSourceInfo(CaptureSourceKind.Picker, "test"));
    }

    private sealed class FakeCaptureSession(params CapturedFrame[] frames) : IScreenCaptureSession
    {
        private readonly Queue<CapturedFrame> _frames = new(frames);

        public Task<CapturedFrame> CaptureFrameAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_frames.Dequeue());
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeOcrService : IOcrService
    {
        public int CallCount { get; private set; }

        public Task<OcrResult> RecognizeAsync(CapturedFrame frame, CancellationToken cancellationToken = default)
        {
            CallCount++;
            var bounds = new BoundingBox(1, 1, 8, 3);
            var word = new OcrWord("hello", bounds, 12);
            var result = new OcrResult(
                [new OcrTextBlock("hello", bounds, [word], 12)],
                "en-US",
                0,
                DateTimeOffset.UtcNow);

            return Task.FromResult(result);
        }
    }

    private sealed class FakeTextTranslator : ITextTranslator
    {
        public int CallCount { get; private set; }

        public Task<TextTranslationResult> TranslateAsync(
            string text,
            string targetLanguage,
            string? sourceLanguage = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(new TextTranslationResult(text, "xin chao", targetLanguage, sourceLanguage));
        }
    }
}
