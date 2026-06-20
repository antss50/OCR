using LexVerse.Core.Imaging;

namespace LexVerse.Core.Pipeline;

public interface IOcrTriggerPolicy
{
    bool ShouldRunOcr(OcrRegion region, CapturedFrame currentFrame);
}
