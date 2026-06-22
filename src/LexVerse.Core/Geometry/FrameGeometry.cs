namespace LexVerse.Core.Geometry;

public sealed record FrameGeometry(
    PixelSize FrameSize,
    ScreenRect SourceScreenRect,
    CoordinateSpace FrameCoordinateSpace,
    double DpiScaleX,
    double DpiScaleY,
    long Version)
{
    public void Validate()
    {
        FrameSize.Validate();
        SourceScreenRect.Validate();

        if (DpiScaleX <= 0 || DpiScaleY <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(FrameGeometry), "DPI scale must be greater than zero.");
        }
    }

    public static FrameGeometry FrameLocal(PixelSize frameSize, long version = 0)
    {
        frameSize.Validate();

        return new FrameGeometry(
            frameSize,
            new ScreenRect(0, 0, frameSize.Width, frameSize.Height),
            CoordinateSpace.FrameLocal,
            1,
            1,
            version);
    }
}
