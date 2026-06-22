namespace LexVerse.Core.Geometry;

public readonly record struct ScreenRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;

    public double Bottom => Y + Height;

    public void Validate()
    {
        if (Width <= 0 || Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ScreenRect), "Screen rect width and height must be greater than zero.");
        }
    }
}
