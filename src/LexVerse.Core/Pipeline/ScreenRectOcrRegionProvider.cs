using LexVerse.Core.Geometry;
using LexVerse.Core.Imaging;

namespace LexVerse.Core.Pipeline;

/// <summary>
/// Maps a physical-screen selection into the local pixel coordinates of each captured frame.
/// </summary>
public sealed class ScreenRectOcrRegionProvider : IOcrRegionProvider
{
    private readonly ScreenRect _screenRegion;
    private readonly OcrProcessingMode _mode;

    public ScreenRectOcrRegionProvider(ScreenRect screenRegion, OcrProcessingMode mode)
    {
        screenRegion.Validate();
        _screenRegion = screenRegion;
        _mode = mode;
    }

    public IReadOnlyList<OcrRegion> GetRegions(CapturedFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        var source = frame.Geometry.SourceScreenRect;
        var clipped = Intersect(_screenRegion, source);
        if (clipped.Width <= 0 || clipped.Height <= 0)
        {
            return [];
        }

        var scaleX = frame.Width / source.Width;
        var scaleY = frame.Height / source.Height;
        var x = (int)Math.Round((clipped.X - source.X) * scaleX);
        var y = (int)Math.Round((clipped.Y - source.Y) * scaleY);
        var width = (int)Math.Round(clipped.Width * scaleX);
        var height = (int)Math.Round(clipped.Height * scaleY);

        return width <= 0 || height <= 0
            ? []
            : [new OcrRegion("selected-region", x, y, width, height, _mode)];
    }

    private static ScreenRect Intersect(ScreenRect left, ScreenRect right)
    {
        var x = Math.Max(left.X, right.X);
        var y = Math.Max(left.Y, right.Y);
        var intersectionRight = Math.Min(left.Right, right.Right);
        var intersectionBottom = Math.Min(left.Bottom, right.Bottom);
        return new ScreenRect(
            x,
            y,
            Math.Max(0, intersectionRight - x),
            Math.Max(0, intersectionBottom - y));
    }
}
