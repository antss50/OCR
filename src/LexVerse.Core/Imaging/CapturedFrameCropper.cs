using LexVerse.Core.Geometry;

namespace LexVerse.Core.Imaging;

public static class CapturedFrameCropper
{
    private const int Bgra8BytesPerPixel = 4;

    public static CapturedFrame Crop(
        CapturedFrame frame,
        int x,
        int y,
        int width,
        int height)
    {
        ArgumentNullException.ThrowIfNull(frame);

        if (frame.PixelFormat != PixelFormat.Bgra8)
        {
            throw new NotSupportedException($"Unsupported crop pixel format: {frame.PixelFormat}.");
        }

        var cropX = Math.Clamp(x, 0, frame.Width);
        var cropY = Math.Clamp(y, 0, frame.Height);
        var cropRight = Math.Clamp(x + width, cropX, frame.Width);
        var cropBottom = Math.Clamp(y + height, cropY, frame.Height);
        var cropWidth = cropRight - cropX;
        var cropHeight = cropBottom - cropY;

        if (cropWidth <= 0 || cropHeight <= 0)
        {
            throw new ArgumentException("OCR region must overlap the captured frame.");
        }

        var cropStride = cropWidth * Bgra8BytesPerPixel;
        var cropPixels = new byte[cropStride * cropHeight];

        for (var row = 0; row < cropHeight; row++)
        {
            var sourceOffset = ((cropY + row) * frame.Stride) + (cropX * Bgra8BytesPerPixel);
            var destinationOffset = row * cropStride;
            Buffer.BlockCopy(frame.Pixels, sourceOffset, cropPixels, destinationOffset, cropStride);
        }

        return new CapturedFrame(
            cropWidth,
            cropHeight,
            cropStride,
            frame.PixelFormat,
            cropPixels,
            frame.CapturedAt,
            CreateCropGeometry(frame, cropX, cropY, cropWidth, cropHeight),
            frame.Source,
            frame.FrameId);
    }

    private static FrameGeometry CreateCropGeometry(
        CapturedFrame frame,
        int cropX,
        int cropY,
        int cropWidth,
        int cropHeight)
    {
        var cropScreenRect = new CoordinateMapper(frame.Geometry)
            .FrameToScreen(new FrameRect(cropX, cropY, cropWidth, cropHeight));

        return new FrameGeometry(
                new PixelSize(cropWidth, cropHeight),
                cropScreenRect,
                frame.Geometry.FrameCoordinateSpace,
                frame.Geometry.DpiScaleX,
                frame.Geometry.DpiScaleY,
                frame.Geometry.Version);
    }
}
