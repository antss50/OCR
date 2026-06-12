using System.Runtime.InteropServices;
using Vortice.Direct3D11;
using Vortice.DXGI;
using WinRT;
using Windows.Graphics.DirectX.Direct3D11;

namespace LexVerse.Infrastructure.Capture;

internal static class Direct3DInterop
{
    public static IDirect3DDevice CreateDirect3DDevice(ID3D11Device d3dDevice)
    {
        using var dxgiDevice = d3dDevice.QueryInterface<IDXGIDevice>();
        NativeMethods.CreateDirect3D11DeviceFromDXGIDevice(
            dxgiDevice.NativePointer,
            out var inspectable).ThrowOnFailure();

        try
        {
            return MarshalInterface<IDirect3DDevice>.FromAbi(inspectable);
        }
        finally
        {
            Marshal.Release(inspectable);
        }
    }

    public static ID3D11Texture2D GetTexture2DFromSurface(IDirect3DSurface surface)
    {
        var access = surface.As<IDirect3DDxgiInterfaceAccess>();
        access.GetInterface(typeof(ID3D11Texture2D).GUID, out var nativePointer);
        return new ID3D11Texture2D(nativePointer);
    }

    [ComImport]
    [Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDirect3DDxgiInterfaceAccess
    {
        void GetInterface(in Guid iid, out IntPtr p);
    }

    private static class NativeMethods
    {
        [DllImport("d3d11.dll")]
        internal static extern int CreateDirect3D11DeviceFromDXGIDevice(
            IntPtr dxgiDevice,
            out IntPtr graphicsDevice);
    }
}

internal static class HResultExtensions
{
    public static void ThrowOnFailure(this int result)
    {
        if (result < 0)
        {
            Marshal.ThrowExceptionForHR(result);
        }
    }
}
