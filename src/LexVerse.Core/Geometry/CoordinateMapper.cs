namespace LexVerse.Core.Geometry;

public sealed class CoordinateMapper
{
    private readonly FrameGeometry _geometry;
    private readonly double _screenPerFrameX;
    private readonly double _screenPerFrameY;

    public CoordinateMapper(FrameGeometry geometry)
    {
        geometry.Validate();

        _geometry = geometry;
        _screenPerFrameX = geometry.SourceScreenRect.Width / geometry.FrameSize.Width;
        _screenPerFrameY = geometry.SourceScreenRect.Height / geometry.FrameSize.Height;
    }

    public ScreenRect FrameToScreen(FrameRect rect)
    {
        rect.Validate();

        return new ScreenRect(
            _geometry.SourceScreenRect.X + (rect.X * _screenPerFrameX),
            _geometry.SourceScreenRect.Y + (rect.Y * _screenPerFrameY),
            rect.Width * _screenPerFrameX,
            rect.Height * _screenPerFrameY);
    }

    public FrameRect ScreenToFrame(ScreenRect rect)
    {
        rect.Validate();

        return new FrameRect(
            (rect.X - _geometry.SourceScreenRect.X) / _screenPerFrameX,
            (rect.Y - _geometry.SourceScreenRect.Y) / _screenPerFrameY,
            rect.Width / _screenPerFrameX,
            rect.Height / _screenPerFrameY);
    }
}
