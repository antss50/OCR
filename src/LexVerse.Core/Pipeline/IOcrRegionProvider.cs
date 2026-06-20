using LexVerse.Core.Imaging;

namespace LexVerse.Core.Pipeline;

public interface IOcrRegionProvider
{
    IReadOnlyList<OcrRegion> GetRegions(CapturedFrame frame);
}
