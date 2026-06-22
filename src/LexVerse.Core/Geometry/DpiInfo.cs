namespace LexVerse.Core.Geometry;

public readonly record struct DpiInfo(double DpiX, double DpiY)
{
    public double ScaleX => DpiX / 96.0;

    public double ScaleY => DpiY / 96.0;

    public static DpiInfo Default { get; } = new(96, 96);
}
