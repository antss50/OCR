namespace LexVerse.Core.Imaging;

public static class CapturedFrameRegionMasker
{
    private const int Bgra8BytesPerPixel = 4;
    private const byte OpaqueAlpha = 255;

    public static CapturedFrame KeepRegions(
        CapturedFrame frame,
        IReadOnlyList<RegionBounds> regions)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(regions);

        if (frame.PixelFormat != PixelFormat.Bgra8)
        {
            throw new NotSupportedException($"Unsupported mask pixel format: {frame.PixelFormat}.");
        }

        var pixels = new byte[frame.Stride * frame.Height];
        FillOpaqueBlack(pixels);

        foreach (var region in regions)
        {
            CopyRegion(frame, pixels, region);
        }

        return new CapturedFrame(
            frame.Width,
            frame.Height,
            frame.Stride,
            frame.PixelFormat,
            pixels,
            frame.CapturedAt);
    }

    private static void FillOpaqueBlack(byte[] pixels)
    {
        for (var index = Bgra8BytesPerPixel - 1; index < pixels.Length; index += Bgra8BytesPerPixel)
        {
            pixels[index] = OpaqueAlpha;
        }
    }

    private static void CopyRegion(CapturedFrame sourceFrame, byte[] destinationPixels, RegionBounds region)
    {
        var x = Math.Clamp(region.X, 0, sourceFrame.Width);
        var y = Math.Clamp(region.Y, 0, sourceFrame.Height);
        var right = Math.Clamp(region.X + region.Width, x, sourceFrame.Width);
        var bottom = Math.Clamp(region.Y + region.Height, y, sourceFrame.Height);
        var width = right - x;
        var height = bottom - y;

        if (width <= 0 || height <= 0)
        {
            return;
        }

        var rowByteCount = width * Bgra8BytesPerPixel;
        for (var row = 0; row < height; row++)
        {
            var offset = ((y + row) * sourceFrame.Stride) + (x * Bgra8BytesPerPixel);
            Buffer.BlockCopy(sourceFrame.Pixels, offset, destinationPixels, offset, rowByteCount);
        }
    }
}

public sealed record RegionBounds(int X, int Y, int Width, int Height);
