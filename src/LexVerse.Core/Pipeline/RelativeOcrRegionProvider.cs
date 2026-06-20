using LexVerse.Core.Imaging;

namespace LexVerse.Core.Pipeline;

public sealed class RelativeOcrRegionProvider(IReadOnlyList<RelativeOcrRegion> regions) : IOcrRegionProvider
{
    public IReadOnlyList<OcrRegion> GetRegions(CapturedFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        return regions
            .Select(region => new OcrRegion(
                region.Id,
                (int)Math.Round(frame.Width * region.X),
                (int)Math.Round(frame.Height * region.Y),
                (int)Math.Round(frame.Width * region.Width),
                (int)Math.Round(frame.Height * region.Height),
                region.Mode))
            .Where(region => region.Width > 0 && region.Height > 0)
            .ToArray();
    }
}

public sealed record RelativeOcrRegion(
    string Id,
    double X,
    double Y,
    double Width,
    double Height,
    OcrProcessingMode Mode);
