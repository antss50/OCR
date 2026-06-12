using LexVerse.Core.Imaging;

namespace LexVerse.Core.ScreenCapture;

public sealed class ExactFrameChangeDetector : IFrameChangeDetector
{
    private CapturedFrame? _previousFrame;

    public bool HasChanged(CapturedFrame currentFrame)
    {
        var previousFrame = _previousFrame;
        _previousFrame = currentFrame;

        if (previousFrame is null)
        {
            return true;
        }

        if (previousFrame.Width != currentFrame.Width ||
            previousFrame.Height != currentFrame.Height ||
            previousFrame.Stride != currentFrame.Stride ||
            previousFrame.PixelFormat != currentFrame.PixelFormat ||
            previousFrame.Pixels.Length != currentFrame.Pixels.Length)
        {
            return true;
        }

        return !previousFrame.Pixels.AsSpan().SequenceEqual(currentFrame.Pixels);
    }
}
