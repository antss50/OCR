using LexVerse.Core.Imaging;

namespace LexVerse.Core.ScreenCapture;

public interface IScreenCaptureSession : IAsyncDisposable
{
    Task<CapturedFrame> CaptureFrameAsync(CancellationToken cancellationToken = default);
}
