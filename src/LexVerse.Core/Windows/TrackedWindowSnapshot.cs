using LexVerse.Core.Geometry;

namespace LexVerse.Core.Windows;

public sealed record TrackedWindowSnapshot(
    IntPtr Hwnd,
    ScreenRect WindowRect,
    ScreenRect ClientRect,
    bool IsVisible,
    bool IsMinimized,
    DpiInfo Dpi,
    long Version);
