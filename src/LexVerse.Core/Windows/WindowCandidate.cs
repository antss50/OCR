using LexVerse.Core.Geometry;

namespace LexVerse.Core.Windows;

public sealed record WindowCandidate(
    IntPtr Hwnd,
    string Title,
    string? ProcessName,
    ScreenRect WindowRect,
    ScreenRect? ClientRect,
    bool IsVisible,
    bool IsMinimized);
