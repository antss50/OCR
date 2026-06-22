using LexVerse.Core.Geometry;

namespace LexVerse.Core.Overlay;

public sealed record OverlayTextItem(
    string SourceText,
    string TranslatedText,
    ScreenRect TargetRect,
    double Confidence,
    double FontSize,
    string? DebugText = null,
    ScreenRect? SourceRect = null);
