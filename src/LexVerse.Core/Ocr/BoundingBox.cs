namespace LexVerse.Core.Ocr;

public readonly record struct BoundingBox(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;

    public int Bottom => Y + Height;

    public static BoundingBox Union(BoundingBox left, BoundingBox right)
    {
        var x1 = Math.Min(left.X, right.X);
        var y1 = Math.Min(left.Y, right.Y);
        var x2 = Math.Max(left.Right, right.Right);
        var y2 = Math.Max(left.Bottom, right.Bottom);

        return new BoundingBox(x1, y1, x2 - x1, y2 - y1);
    }
}
