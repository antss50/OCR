using LexVerse.Core.Capture;
using LexVerse.Core.Geometry;

namespace LexVerse.Core.Imaging;

public sealed record CapturedFrame
{
    public CapturedFrame(
        int width,
        int height,
        int stride,
        PixelFormat pixelFormat,
        byte[] pixels,
        DateTimeOffset capturedAt,
        FrameGeometry? geometry = null,
        CaptureSourceInfo? source = null,
        Guid? frameId = null)
    {
        Width = width;
        Height = height;
        Stride = stride;
        PixelFormat = pixelFormat;
        Pixels = pixels;
        CapturedAt = capturedAt;
        Geometry = geometry ?? FrameGeometry.FrameLocal(new PixelSize(width, height));
        Source = source ?? CaptureSourceInfo.Unknown;
        FrameId = frameId ?? Guid.NewGuid();
    }

    public int Width { get; init; }

    public int Height { get; init; }

    public int Stride { get; init; }

    public PixelFormat PixelFormat { get; init; }

    public byte[] Pixels { get; init; }

    public DateTimeOffset CapturedAt { get; init; }

    public FrameGeometry Geometry { get; init; }

    public CaptureSourceInfo Source { get; init; }

    public Guid FrameId { get; init; }

    public int ExpectedByteCount => Stride * Height;
}
