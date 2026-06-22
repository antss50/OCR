using System.Runtime.InteropServices;
using LexVerse.Core.Geometry;

namespace LexVerse.Infrastructure.Windows;

internal static class Win32WindowGeometry
{
    private const int DwmwaExtendedFrameBounds = 9;

    public static ScreenRect GetExtendedFrameRect(IntPtr hwnd)
    {
        if (DwmGetWindowAttribute(
                hwnd,
                DwmwaExtendedFrameBounds,
                out Rect rect,
                Marshal.SizeOf<Rect>()) == 0)
        {
            return ToScreenRect(rect);
        }

        return GetWindowRect(hwnd, out rect)
            ? ToScreenRect(rect)
            : new ScreenRect(0, 0, 0, 0);
    }

    public static ScreenRect? TryGetClientRect(IntPtr hwnd)
    {
        if (!GetClientRect(hwnd, out var client))
        {
            return null;
        }

        var topLeft = new Point(client.Left, client.Top);
        var bottomRight = new Point(client.Right, client.Bottom);
        if (!ClientToScreen(hwnd, ref topLeft) || !ClientToScreen(hwnd, ref bottomRight))
        {
            return null;
        }

        return new ScreenRect(
            topLeft.X,
            topLeft.Y,
            bottomRight.X - topLeft.X,
            bottomRight.Y - topLeft.Y);
    }

    public static DpiInfo GetDpi(IntPtr hwnd)
    {
        try
        {
            var dpi = GetDpiForWindow(hwnd);
            return dpi > 0 ? new DpiInfo(dpi, dpi) : DpiInfo.Default;
        }
        catch (EntryPointNotFoundException)
        {
            return DpiInfo.Default;
        }
    }

    private static ScreenRect ToScreenRect(Rect rect)
    {
        return new ScreenRect(
            rect.Left,
            rect.Top,
            rect.Right - rect.Left,
            rect.Bottom - rect.Top);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public Point(int x, int y)
        {
            X = x;
            Y = y;
        }

        public int X;
        public int Y;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(
        IntPtr hwnd,
        int dwAttribute,
        out Rect pvAttribute,
        int cbAttribute);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr hWnd, out Rect lpRect);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetClientRect(IntPtr hWnd, out Rect lpRect);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ClientToScreen(IntPtr hWnd, ref Point lpPoint);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);
}
