using LexVerse.Core.Imaging;

namespace LexVerse.Core.ScreenCapture;

public interface IFrameChangeDetector
{
    bool HasChanged(CapturedFrame currentFrame);
}
