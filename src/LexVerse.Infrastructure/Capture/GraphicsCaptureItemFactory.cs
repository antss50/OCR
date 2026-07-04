using System.Runtime.InteropServices;
using WinRT;
using Windows.Graphics.Capture;

namespace LexVerse.Infrastructure.Capture;

public static class GraphicsCaptureItemFactory
{
    private static readonly Guid GraphicsCaptureItemGuid = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");

    public static GraphicsCaptureItem CreateForWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            throw new ArgumentException("Window handle must not be zero.", nameof(hwnd));
        }

        var interop = GraphicsCaptureItem.As<IGraphicsCaptureItemInterop>();
        interop.CreateForWindow(hwnd, GraphicsCaptureItemGuid, out var itemPointer).ThrowOnFailure();

        try
        {
            return MarshalInterface<GraphicsCaptureItem>.FromAbi(itemPointer);
        }
        finally
        {
            Marshal.Release(itemPointer);
        }
    }

    public static GraphicsCaptureItem CreateForMonitor(IntPtr monitor)
    {
        if (monitor == IntPtr.Zero)
        {
            throw new ArgumentException("Monitor handle must not be zero.", nameof(monitor));
        }

        var interop = GraphicsCaptureItem.As<IGraphicsCaptureItemInterop>();
        interop.CreateForMonitor(monitor, GraphicsCaptureItemGuid, out var itemPointer).ThrowOnFailure();

        try
        {
            return MarshalInterface<GraphicsCaptureItem>.FromAbi(itemPointer);
        }
        finally
        {
            Marshal.Release(itemPointer);
        }
    }

    [ComImport]
    [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        int CreateForWindow(IntPtr window, in Guid iid, out IntPtr result);

        int CreateForMonitor(IntPtr monitor, in Guid iid, out IntPtr result);
    }
}
