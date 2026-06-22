namespace LexVerse.Core.Capture;

public sealed record CaptureSourceInfo(
    CaptureSourceKind Kind,
    string DisplayName,
    IntPtr? Hwnd = null,
    string? MonitorDeviceName = null)
{
    public static CaptureSourceInfo Unknown { get; } = new(CaptureSourceKind.Picker, "Unknown");
}
