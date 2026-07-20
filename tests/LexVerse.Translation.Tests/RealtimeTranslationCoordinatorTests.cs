using System.Collections.Concurrent;
using LexVerse.Application.Diagnostics;
using LexVerse.Application.Product;
using LexVerse.Application.Realtime;
using LexVerse.Core.Capture;
using LexVerse.Core.Geometry;
using LexVerse.Core.Imaging;
using LexVerse.Core.Overlay;
using LexVerse.Core.Pipeline;
using LexVerse.Core.Product;
using Xunit;

namespace LexVerse.Translation.Tests;

public sealed class RealtimeTranslationCoordinatorTests
{
    [Fact]
    public async Task Start_ProcessesFramesAndStopDisposesSessionAndClearsOutput()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var session = new FakeSession();
        var factory = new FakeSessionFactory(session);
        var output = new FakeOutput();
        await using var coordinator = CreateCoordinator(allowed: true, factory, output);

        await coordinator.StartAsync(CreateRequest(), cancellationToken);
        await output.FirstRender.Task.WaitAsync(cancellationToken);
        await coordinator.StopAsync(cancellationToken);

        Assert.Equal(RealtimeCoordinatorStatus.Stopped, coordinator.State.Status);
        Assert.True(session.Disposed);
        Assert.True(output.ClearCount >= 1);
        Assert.Equal(1, factory.CallCount);
    }

    [Fact]
    public async Task Start_DeniedFeatureDoesNotCreateCaptureSession()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var factory = new FakeSessionFactory(new FakeSession());
        var output = new FakeOutput();
        await using var coordinator = CreateCoordinator(allowed: false, factory, output);

        await Assert.ThrowsAsync<FeatureAccessDeniedException>(() =>
            coordinator.StartAsync(CreateRequest(), cancellationToken));

        Assert.Equal(0, factory.CallCount);
        Assert.Equal(RealtimeCoordinatorStatus.Faulted, coordinator.State.Status);
    }

    [Fact]
    public async Task StartingNewTargetStopsAndDisposesPreviousSession()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var first = new FakeSession();
        var second = new FakeSession();
        var factory = new FakeSessionFactory(first, second);
        var output = new FakeOutput();
        await using var coordinator = CreateCoordinator(allowed: true, factory, output);

        await coordinator.StartAsync(CreateRequest(), cancellationToken);
        await first.FirstProcess.Task.WaitAsync(cancellationToken);
        await coordinator.StartAsync(
            CreateRequest(RealtimeCaptureKind.FullScreen),
            cancellationToken);
        await second.FirstProcess.Task.WaitAsync(cancellationToken);

        Assert.True(first.Disposed);
        Assert.False(second.Disposed);
        Assert.Equal(2, factory.CallCount);
        Assert.Equal(RealtimeCaptureKind.FullScreen, coordinator.State.CaptureKind);
    }

    [Fact]
    public async Task PipelineFailureTransitionsToFaultedAndReleasesResources()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var session = new FakeSession(throwOnProcess: true);
        var factory = new FakeSessionFactory(session);
        var output = new FakeOutput();
        var faulted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var coordinator = CreateCoordinator(allowed: true, factory, output);
        coordinator.StateChanged += (_, state) =>
        {
            if (state.Status == RealtimeCoordinatorStatus.Faulted)
            {
                faulted.TrySetResult();
            }
        };

        await coordinator.StartAsync(CreateRequest(), cancellationToken);
        await faulted.Task.WaitAsync(cancellationToken);
        await session.DisposedSignal.Task.WaitAsync(cancellationToken);

        Assert.Equal("pipeline-failed", coordinator.State.ErrorCode);
        Assert.True(session.Disposed);
        Assert.True(output.ClearCount >= 1);
    }

    [Fact]
    public void ScreenRectRegionProviderMapsPhysicalSelectionToFramePixels()
    {
        var provider = new ScreenRectOcrRegionProvider(
            new ScreenRect(300, 400, 200, 160),
            OcrProcessingMode.Document);
        var frame = CreateFrame(
            frameSize: new PixelSize(500, 400),
            sourceRect: new ScreenRect(100, 200, 1000, 800));

        var region = Assert.Single(provider.GetRegions(frame));

        Assert.Equal(100, region.X);
        Assert.Equal(100, region.Y);
        Assert.Equal(100, region.Width);
        Assert.Equal(80, region.Height);
    }

    [Fact]
    public async Task RealtimeTelemetryCorrelatesSessionAndFrameWithoutContent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var session = new FakeSession();
        var output = new FakeOutput();
        var telemetry = new RecordingTelemetry();
        await using var coordinator = CreateCoordinator(
            allowed: true,
            new FakeSessionFactory(session),
            output,
            telemetry);

        await coordinator.StartAsync(CreateRequest(), cancellationToken);
        await output.FirstRender.Task.WaitAsync(cancellationToken);
        await coordinator.StopAsync(cancellationToken);

        var events = telemetry.Events.ToArray();
        var started = Assert.Single(events, item =>
            item.Kind == OperationalEventKind.RealtimeSession
            && item.Outcome == OperationalOutcome.Started);
        var frame = Assert.Single(events, item =>
            item.Kind == OperationalEventKind.RealtimeFrame);
        var stopped = Assert.Single(events, item =>
            item.Kind == OperationalEventKind.RealtimeSession
            && item.Outcome == OperationalOutcome.Stopped);
        Assert.Equal(started.CorrelationId, frame.CorrelationId);
        Assert.Equal(started.CorrelationId, stopped.CorrelationId);
        Assert.Equal(OcrProcessingMode.Document, frame.ProcessingMode);
        Assert.False(frame.PerformanceBudgetExceeded);
    }

    [Fact]
    public async Task CleanupFailureIsReportedWithoutLeakingTheBackgroundException()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var session = new FakeSession(throwOnDispose: true);
        var output = new FakeOutput();
        await using var coordinator = CreateCoordinator(
            allowed: true,
            new FakeSessionFactory(session),
            output);

        await coordinator.StartAsync(CreateRequest(), cancellationToken);
        await output.FirstRender.Task.WaitAsync(cancellationToken);
        await coordinator.StopAsync(cancellationToken);

        Assert.Equal(RealtimeCoordinatorStatus.Faulted, coordinator.State.Status);
        Assert.Equal("cleanup-failed", coordinator.State.ErrorCode);
        Assert.True(output.ClearCount >= 1);
    }

    [Fact]
    public async Task RealtimeTelemetrySamplesFramesAndKeepsSessionTotals()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var output = new FakeOutput();
        var telemetry = new RecordingTelemetry();
        await using var coordinator = CreateCoordinator(
            allowed: true,
            new FakeSessionFactory(new FakeSession()),
            output,
            telemetry);

        await coordinator.StartAsync(
            CreateRequest(ocrInterval: TimeSpan.FromMilliseconds(1)),
            cancellationToken);
        await output.TwentyFirstRender.Task.WaitAsync(cancellationToken);
        await coordinator.StopAsync(cancellationToken);

        var events = telemetry.Events.ToArray();
        var stopped = Assert.Single(events, item =>
            item.Kind == OperationalEventKind.RealtimeSession
            && item.Outcome == OperationalOutcome.Stopped);
        var sampledFrames = events
            .Where(item => item.Kind == OperationalEventKind.RealtimeFrame)
            .ToArray();
        Assert.True(stopped.FrameCount >= 21);
        Assert.Contains(sampledFrames, item => item.FrameCount == 1);
        Assert.Contains(sampledFrames, item => item.FrameCount == 20);
        Assert.True(sampledFrames.Length < stopped.FrameCount);
    }

    private static RealtimeTranslationCoordinator CreateCoordinator(
        bool allowed,
        IRealtimePipelineSessionFactory factory,
        IRealtimeOutput output,
        IOperationalTelemetry? telemetry = null)
    {
        var access = new FixedAccessService(allowed);
        return new RealtimeTranslationCoordinator(new FeatureGate(access), factory, output, telemetry);
    }

    private static RealtimeTranslationRequest CreateRequest(
        RealtimeCaptureKind kind = RealtimeCaptureKind.Region,
        TimeSpan? ocrInterval = null) =>
        new(
            kind,
            new ScreenRect(0, 0, 800, 600),
            "en-US",
            RealtimeTranslationOptions.Document with
            {
                OcrInterval = ocrInterval ?? TimeSpan.FromHours(1)
            });

    private static RealtimeFrameUpdate CreateUpdate()
    {
        var zero = TimeSpan.Zero;
        return new RealtimeFrameUpdate(
            new OverlayRenderFrame([], null, 0, DateTimeOffset.UtcNow),
            new RealtimeTranslationPipelineTiming(zero, zero, zero, zero, zero, zero, 0, 0),
            0,
            0,
            true,
            false);
    }

    private static CapturedFrame CreateFrame(PixelSize frameSize, ScreenRect sourceRect)
    {
        var stride = frameSize.Width * 4;
        return new CapturedFrame(
            frameSize.Width,
            frameSize.Height,
            stride,
            PixelFormat.Bgra8,
            new byte[stride * frameSize.Height],
            DateTimeOffset.UtcNow,
            new FrameGeometry(frameSize, sourceRect, CoordinateSpace.FrameLocal, 1, 1, 0),
            new CaptureSourceInfo(CaptureSourceKind.Region, "test"));
    }

    private sealed class FixedAccessService(bool allowed) : IFeatureAccessService
    {
        public ValueTask<FeatureAccessDecision> GetAccessAsync(
            FeatureKey feature,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(
                allowed
                    ? FeatureAccessDecision.Allowed(feature, null)
                    : FeatureAccessDecision.Denied(feature, FeatureAccessDenialReason.NotEntitled));

        public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void Invalidate() { }
    }

    private sealed class FakeSessionFactory(params FakeSession[] sessions) : IRealtimePipelineSessionFactory
    {
        private int _index;

        public int CallCount { get; private set; }

        public Task<IRealtimePipelineSession> CreateAsync(
            RealtimeTranslationRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult<IRealtimePipelineSession>(sessions[_index++]);
        }
    }

    private sealed class FakeSession(
        bool throwOnProcess = false,
        bool throwOnDispose = false) : IRealtimePipelineSession
    {
        public TaskCompletionSource FirstProcess { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource DisposedSignal { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool Disposed { get; private set; }

        public Task<RealtimeFrameUpdate> ProcessNextAsync(
            Func<RealtimeFrameUpdate, CancellationToken, Task>? partialResultHandler = null,
            CancellationToken cancellationToken = default)
        {
            FirstProcess.TrySetResult();
            return throwOnProcess
                ? Task.FromException<RealtimeFrameUpdate>(new InvalidOperationException("pipeline failed"))
                : Task.FromResult(CreateUpdate());
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            DisposedSignal.TrySetResult();
            return throwOnDispose
                ? ValueTask.FromException(new IOException("cleanup failed"))
                : ValueTask.CompletedTask;
        }
    }

    private sealed class FakeOutput : IRealtimeOutput
    {
        public TaskCompletionSource FirstRender { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource TwentyFirstRender { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private int _renderCount;

        public int ClearCount { get; private set; }

        public Task RenderAsync(
            RealtimeFrameUpdate update,
            bool isPartial,
            CancellationToken cancellationToken = default)
        {
            FirstRender.TrySetResult();
            if (Interlocked.Increment(ref _renderCount) >= 21)
            {
                TwentyFirstRender.TrySetResult();
            }
            return Task.CompletedTask;
        }

        public Task ClearAsync(CancellationToken cancellationToken = default)
        {
            ClearCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingTelemetry : IOperationalTelemetry
    {
        public ConcurrentQueue<OperationalEvent> Events { get; } = new();

        public void Record(OperationalEvent operationalEvent) => Events.Enqueue(operationalEvent);
    }
}
