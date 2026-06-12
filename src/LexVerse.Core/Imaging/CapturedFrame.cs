namespace LexVerse.Core.Imaging;

public sealed record CapturedFrame(
    int Width,
    int Height,
    int Stride,
    PixelFormat PixelFormat,
    byte[] Pixels,
    DateTimeOffset CapturedAt)
{
    public int ExpectedByteCount => Stride * Height;
}
