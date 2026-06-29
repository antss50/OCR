using System.Runtime.InteropServices;
using LexVerse.Core.Capture;
using LexVerse.Core.Geometry;
using LexVerse.Core.Imaging;
using LexVerse.Core.ScreenCapture;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;

namespace LexVerse.Infrastructure.Capture;

public sealed class WindowsGraphicsCaptureSession : IScreenCaptureSession
{
    private readonly ID3D11Device _d3dDevice;
    private readonly ID3D11DeviceContext _d3dContext;
    private readonly IDirect3DDevice _direct3DDevice;
    private readonly Direct3D11CaptureFramePool _framePool;
    private readonly GraphicsCaptureSession _session;
    private readonly CaptureSourceInfo _source;
    private readonly Func<PixelSize, FrameGeometry>? _geometryProvider;
    private SizeInt32 _framePoolSize;
    private ScreenRect? _lastSourceScreenRect;
    private long _geometryVersion;

    private WindowsGraphicsCaptureSession(
        ID3D11Device d3dDevice,
        ID3D11DeviceContext d3dContext,
        IDirect3DDevice direct3DDevice,
        Direct3D11CaptureFramePool framePool,
        GraphicsCaptureSession session,
        SizeInt32 framePoolSize,
        CaptureSourceInfo source,
        Func<PixelSize, FrameGeometry>? geometryProvider)
    {
        _d3dDevice = d3dDevice;
        _d3dContext = d3dContext;
        _direct3DDevice = direct3DDevice;
        _framePool = framePool;
        _session = session;
        _source = source;
        _geometryProvider = geometryProvider;
        _framePoolSize = framePoolSize;
    }

    public static WindowsGraphicsCaptureSession Create(
        GraphicsCaptureItem item,
        Func<PixelSize, FrameGeometry>? geometryProvider = null,
        CaptureSourceInfo? source = null,
        bool captureCursor = false)
    {
        ArgumentNullException.ThrowIfNull(item);

        var featureLevels = new[]
        {
            FeatureLevel.Level_12_1,
            FeatureLevel.Level_12_0,
            FeatureLevel.Level_11_1,
            FeatureLevel.Level_11_0
        };

        var d3dDevice = D3D11.D3D11CreateDevice(
            DriverType.Hardware,
            DeviceCreationFlags.BgraSupport,
            featureLevels);
        var d3dContext = d3dDevice.ImmediateContext;

        var direct3DDevice = Direct3DInterop.CreateDirect3DDevice(d3dDevice);
        var framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
            direct3DDevice,
            DirectXPixelFormat.B8G8R8A8UIntNormalized,
            2,
            item.Size);

        var session = framePool.CreateCaptureSession(item);
        session.IsCursorCaptureEnabled = captureCursor;
        session.StartCapture();

        return new WindowsGraphicsCaptureSession(
            d3dDevice,
            d3dContext,
            direct3DDevice,
            framePool,
            session,
            item.Size,
            source ?? new CaptureSourceInfo(CaptureSourceKind.Picker, item.DisplayName),
            geometryProvider);
    }

    public async Task<CapturedFrame> CaptureFrameAsync(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            using var frame = await WaitForFrameAsync(cancellationToken);
            if (RecreateFramePoolIfNeeded(frame.ContentSize))
            {
                continue;
            }

            return CopyFrameToCpu(frame);
        }
    }

    public ValueTask DisposeAsync()
    {
        _session.Dispose();
        _framePool.Dispose();
        _direct3DDevice.Dispose();
        _d3dContext.Dispose();
        _d3dDevice.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task<Direct3D11CaptureFrame> WaitForFrameAsync(CancellationToken cancellationToken)
    {
        var latestFrame = TryGetLatestFrame(_framePool);
        if (latestFrame is not null)
        {
            return latestFrame;
        }

        var completion = new TaskCompletionSource<Direct3D11CaptureFrame>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
        {
            var frame = TryGetLatestFrame(sender);
            if (frame is null)
            {
                return;
            }

            sender.FrameArrived -= OnFrameArrived;
            completion.TrySetResult(frame);
        }

        _framePool.FrameArrived += OnFrameArrived;

        using var cancellationRegistration = cancellationToken.CanBeCanceled
            ? cancellationToken.Register(() =>
            {
                _framePool.FrameArrived -= OnFrameArrived;
                completion.TrySetCanceled(cancellationToken);
            })
            : default;

        try
        {
            return await completion.Task;
        }
        finally
        {
            _framePool.FrameArrived -= OnFrameArrived;
        }
    }

    private static Direct3D11CaptureFrame? TryGetLatestFrame(Direct3D11CaptureFramePool framePool)
    {
        Direct3D11CaptureFrame? latestFrame = null;

        while (true)
        {
            var nextFrame = framePool.TryGetNextFrame();
            if (nextFrame is null)
            {
                return latestFrame;
            }

            latestFrame?.Dispose();
            latestFrame = nextFrame;
        }
    }

    private CapturedFrame CopyFrameToCpu(Direct3D11CaptureFrame frame)
    {
        using var sourceTexture = Direct3DInterop.GetTexture2DFromSurface(frame.Surface);
        var sourceDescription = sourceTexture.Description;
        var width = Math.Min(frame.ContentSize.Width, (int)sourceDescription.Width);
        var height = Math.Min(frame.ContentSize.Height, (int)sourceDescription.Height);
        var stagingDescription = sourceDescription;
        stagingDescription.BindFlags = BindFlags.None;
        stagingDescription.CPUAccessFlags = CpuAccessFlags.Read;
        stagingDescription.MiscFlags = ResourceOptionFlags.None;
        stagingDescription.Usage = ResourceUsage.Staging;

        using var stagingTexture = _d3dDevice.CreateTexture2D(in stagingDescription);
        _d3dContext.CopyResource(stagingTexture, sourceTexture);

        _d3dContext.Map(stagingTexture, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None, out var mapped);
        try
        {
            var destinationStride = width * 4;
            var pixels = new byte[destinationStride * height];

            unsafe
            {
                for (var row = 0; row < height; row++)
                {
                    var source = (byte*)mapped.DataPointer + row * mapped.RowPitch;
                    var destinationOffset = row * destinationStride;
                    Marshal.Copy((IntPtr)source, pixels, destinationOffset, destinationStride);
                }
            }

            return new CapturedFrame(
                width,
                height,
                destinationStride,
                PixelFormat.Bgra8,
                pixels,
                DateTimeOffset.UtcNow,
                CreateFrameGeometry(width, height),
                _source);
        }
        finally
        {
            _d3dContext.Unmap(stagingTexture, 0);
        }
    }

    private FrameGeometry CreateFrameGeometry(int width, int height)
    {
        var frameSize = new PixelSize(width, height);
        if (_geometryProvider is null)
        {
            return FrameGeometry.FrameLocal(frameSize, _geometryVersion);
        }

        var geometry = _geometryProvider(frameSize);
        if (_lastSourceScreenRect is null || !_lastSourceScreenRect.Value.Equals(geometry.SourceScreenRect))
        {
            _geometryVersion++;
            _lastSourceScreenRect = geometry.SourceScreenRect;
        }

        geometry = geometry with
        {
            Version = _geometryVersion
        };
        geometry.Validate();
        return geometry;
    }

    private bool RecreateFramePoolIfNeeded(SizeInt32 contentSize)
    {
        if (contentSize.Width == _framePoolSize.Width &&
            contentSize.Height == _framePoolSize.Height)
        {
            return false;
        }

        _framePool.Recreate(
            _direct3DDevice,
            DirectXPixelFormat.B8G8R8A8UIntNormalized,
            2,
            contentSize);
        _framePoolSize = contentSize;
        _geometryVersion++;
        return true;
    }
}
