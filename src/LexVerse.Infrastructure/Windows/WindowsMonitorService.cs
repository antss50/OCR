using System.Runtime.InteropServices;
using LexVerse.Core.Geometry;

namespace LexVerse.Infrastructure.Windows;

public sealed class WindowsMonitorService
{
    private const uint MonitorDefaultToNearest = 0x00000002;

    public ScreenRect GetNearestMonitorBounds(IntPtr window)
    {
        if (window == IntPtr.Zero)
        {
            throw new ArgumentException("Window handle must not be zero.", nameof(window));
        }

        return GetMonitorBounds(MonitorFromWindow(window, MonitorDefaultToNearest));
    }

    public ScreenRect GetNearestMonitorBounds(ScreenRect screenRect) =>
        GetMonitorBounds(GetNearestMonitor(screenRect));

    public IntPtr GetNearestMonitor(ScreenRect screenRect)
    {
        screenRect.Validate();
        var center = new NativePoint
        {
            X = (int)Math.Round(screenRect.X + screenRect.Width / 2),
            Y = (int)Math.Round(screenRect.Y + screenRect.Height / 2)
        };

        var monitor = MonitorFromPoint(center, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero)
        {
            throw new InvalidOperationException("Could not find a monitor for capture.");
        }

        return monitor;
    }

    public ScreenRect GetMonitorBounds(IntPtr monitor)
    {
        if (monitor == IntPtr.Zero)
        {
            throw new ArgumentException("Monitor handle must not be zero.", nameof(monitor));
        }

        var info = new MonitorInfo
        {
            Size = Marshal.SizeOf<MonitorInfo>()
        };
        if (!GetMonitorInfo(monitor, ref info))
        {
            throw new InvalidOperationException("Could not read monitor bounds.");
        }

        return new ScreenRect(
            info.Monitor.Left,
            info.Monitor.Top,
            info.Monitor.Right - info.Monitor.Left,
            info.Monitor.Bottom - info.Monitor.Top);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect WorkArea;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
