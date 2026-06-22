using System.Runtime.InteropServices;
using LexVerse.Core.Geometry;
using LexVerse.Core.Windows;

namespace LexVerse.Infrastructure.Windows;

public sealed class Win32WindowTracker : IWindowTracker
{
    private readonly Dictionary<IntPtr, TrackedWindowSnapshot> _lastSnapshots = [];

    public TrackedWindowSnapshot GetSnapshot(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            throw new ArgumentException("Window handle must not be zero.", nameof(hwnd));
        }

        var windowRect = Win32WindowGeometry.GetExtendedFrameRect(hwnd);
        var clientRect = Win32WindowGeometry.TryGetClientRect(hwnd) ?? windowRect;
        var isVisible = IsWindowVisible(hwnd);
        var isMinimized = IsIconic(hwnd);
        var dpi = Win32WindowGeometry.GetDpi(hwnd);
        var version = GetNextVersion(hwnd, windowRect, clientRect, isVisible, isMinimized);

        var snapshot = new TrackedWindowSnapshot(
            hwnd,
            windowRect,
            clientRect,
            isVisible,
            isMinimized,
            dpi,
            version);

        _lastSnapshots[hwnd] = snapshot;
        return snapshot;
    }

    private long GetNextVersion(
        IntPtr hwnd,
        ScreenRect windowRect,
        ScreenRect clientRect,
        bool isVisible,
        bool isMinimized)
    {
        if (!_lastSnapshots.TryGetValue(hwnd, out var previous))
        {
            return 0;
        }

        return previous.WindowRect.Equals(windowRect)
            && previous.ClientRect.Equals(clientRect)
            && previous.IsVisible == isVisible
            && previous.IsMinimized == isMinimized
            ? previous.Version
            : previous.Version + 1;
    }

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);
}
