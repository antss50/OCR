namespace LexVerse.Core.Geometry;

public readonly record struct PixelSize(int Width, int Height)
{
    public void Validate()
    {
        if (Width <= 0 || Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(PixelSize), "Pixel size must be greater than zero.");
        }
    }
}
