using System.Windows;
using System.Windows.Media;
using LexVerse.Core.Geometry;

namespace LexVerse.Overlay;

public sealed class PhysicalPixelToDipConverter
{
    private readonly Visual _visual;
    private readonly ScreenPoint _origin;

    public PhysicalPixelToDipConverter(Visual visual, ScreenPoint origin)
    {
        _visual = visual ?? throw new ArgumentNullException(nameof(visual));
        _origin = origin;
    }

    public Rect ToLocalDip(ScreenRect physicalRect)
    {
        var dpi = VisualTreeHelper.GetDpi(_visual);

        return new Rect(
            (physicalRect.X - _origin.X) / dpi.DpiScaleX,
            (physicalRect.Y - _origin.Y) / dpi.DpiScaleY,
            physicalRect.Width / dpi.DpiScaleX,
            physicalRect.Height / dpi.DpiScaleY);
    }
}
