using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using LexVerse.Core.Geometry;
using LexVerse.Core.Windows;

namespace LexVerse.Infrastructure.Windows;

public sealed class Win32WindowEnumerator : IWindowEnumerator
{
    private readonly int _currentProcessId = Environment.ProcessId;

    public IReadOnlyList<WindowCandidate> EnumerateWindows()
    {
        var windows = new List<WindowCandidate>();

        EnumWindows((hwnd, _) =>
        {
            if (!ShouldInclude(hwnd))
            {
                return true;
            }

            var title = GetTitle(hwnd);
            if (string.IsNullOrWhiteSpace(title))
            {
                return true;
            }

            var windowRect = Win32WindowGeometry.GetExtendedFrameRect(hwnd);
            if (windowRect.Width <= 0 || windowRect.Height <= 0)
            {
                return true;
            }

            windows.Add(new WindowCandidate(
                hwnd,
                title,
                GetProcessName(hwnd),
                windowRect,
                Win32WindowGeometry.TryGetClientRect(hwnd),
                IsWindowVisible(hwnd),
                IsIconic(hwnd)));

            return true;
        }, IntPtr.Zero);

        return windows
            .OrderBy(window => window.ProcessName)
            .ThenBy(window => window.Title)
            .ToArray();
    }

    private bool ShouldInclude(IntPtr hwnd)
    {
        if (!IsWindowVisible(hwnd) || IsIconic(hwnd))
        {
            return false;
        }

        _ = GetWindowThreadProcessId(hwnd, out var processId);
        if (processId == _currentProcessId)
        {
            return false;
        }

        var exStyle = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        return (exStyle & WS_EX_TOOLWINDOW) == 0;
    }

    private static string GetTitle(IntPtr hwnd)
    {
        var length = GetWindowTextLength(hwnd);
        if (length <= 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(length + 1);
        _ = GetWindowText(hwnd, builder, builder.Capacity);
        return builder.ToString();
    }

    private static string? GetProcessName(IntPtr hwnd)
    {
        try
        {
            _ = GetWindowThreadProcessId(hwnd, out var processId);
            return Process.GetProcessById((int)processId).ProcessName;
        }
        catch
        {
            return null;
        }
    }

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TOOLWINDOW = 0x80;

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);
}
