using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using LexVerse.Core.Capture;
using LexVerse.Core.Geometry;
using LexVerse.Core.Pipeline;
using LexVerse.Core.ScreenCapture;
using LexVerse.Core.Translation;
using LexVerse.Infrastructure.Capture;
using LexVerse.Infrastructure.Translation;
using LexVerse.Infrastructure.Windows;
using LexVerse.OCR;
using LexVerse.Overlay.Models;
using Windows.Graphics.Capture;
using WinRT.Interop;
using OverlayWindow = LexVerse.Overlay.MainWindow;

namespace LexVerse.Pipeline.Sample;

public partial class MainWindow : Window
{
    private const double OverlayHorizontalPadding = 4;
    private const double OverlayVerticalPadding = 2;

    private readonly ObservableCollection<TextItem> _overlayItems = [];
    private GraphicsCaptureItem? _captureItem;
    private Func<PixelSize, FrameGeometry>? _captureGeometryProvider;
    private CaptureSourceInfo? _captureSourceInfo;
    private bool _useMatchedWindowCaptureItem;
    private WindowsGraphicsCaptureSession? _captureSession;
    private OverlayWindow? _overlayWindow;
    private CancellationTokenSource? _pipelineCancellation;

    public MainWindow()
    {
        InitializeComponent();
    }

    private async void PickButton_Click(object sender, RoutedEventArgs e)
    {
        await StopPipelineAsync();

        try
        {
            var picker = new GraphicsCapturePicker();
            InitializeWithWindow.Initialize(picker, new WindowInteropHelper(this).Handle);

            _captureItem = await picker.PickSingleItemAsync();
            if (_captureItem is null)
            {
                _captureGeometryProvider = null;
                _captureSourceInfo = null;
                _useMatchedWindowCaptureItem = false;
                StatusText.Text = "Selection canceled.";
                StartButton.IsEnabled = false;
                return;
            }

            var binding = CreateCaptureBinding(_captureItem);
            _captureGeometryProvider = binding.GeometryProvider;
            _captureSourceInfo = binding.SourceInfo;
            _useMatchedWindowCaptureItem = binding.UseMatchedWindowCaptureItem;

            StatusText.Text = $"Selected: {_captureItem.DisplayName}. {binding.Status}";
            StartButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            _captureGeometryProvider = null;
            _captureSourceInfo = null;
            _useMatchedWindowCaptureItem = false;
            StatusText.Text = $"Could not choose source: {ex.Message}";
            StartButton.IsEnabled = false;
        }
    }

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (_captureItem is null)
        {
            StatusText.Text = "Pick a source first.";
            return;
        }

        await StopPipelineAsync();

        try
        {
            var sessionItem = CreateSessionCaptureItem();
            _captureSession = WindowsGraphicsCaptureSession.Create(
                sessionItem.Item,
                _captureGeometryProvider,
                _captureSourceInfo);
            _overlayWindow = new OverlayWindow(_overlayItems);
            _overlayWindow.Show();
            _pipelineCancellation = new CancellationTokenSource();

            PickButton.IsEnabled = false;
            StartButton.IsEnabled = false;
            StopButton.IsEnabled = true;
            StatusText.Text = $"Pipeline running. {sessionItem.Status}";

            _ = RunPipelineLoopAsync(_pipelineCancellation.Token);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not start pipeline: {ex.Message}";
            await StopPipelineAsync();
        }
    }

    private async void StopButton_Click(object sender, RoutedEventArgs e)
    {
        await StopPipelineAsync();
        StatusText.Text = "Stopped.";
    }

    private async Task RunPipelineLoopAsync(CancellationToken cancellationToken)
    {
        if (_captureSession is null)
        {
            return;
        }

        var options = CreateOptions();
        var pipeline = new RealtimeTranslationPipeline(
            _captureSession,
            new ExactFrameChangeDetector(),
            new WindowsOcrService(OcrLanguageBox.Text),
            new GoogleCloudTextTranslator(),
            new InMemoryTranslationCache(),
            options,
            CreateRegionProvider(options));

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var result = await pipeline.CaptureRecognizeAndTranslateAsync(
                    cancellationToken,
                    (partialResult, _) =>
                    {
                        RenderResult(partialResult, isPartial: true);
                        return Task.CompletedTask;
                    });
                RenderResult(result, isPartial: false);
                await Task.Delay(options.OcrInterval, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => StatusText.Text = $"Pipeline error: {ex.Message}");
                break;
            }
        }

        await Dispatcher.InvokeAsync(async () => await StopPipelineAsync());
    }

    private SessionCaptureItem CreateSessionCaptureItem()
    {
        if (_useMatchedWindowCaptureItem &&
            _captureSourceInfo?.Hwnd is { } hwnd &&
            hwnd != IntPtr.Zero)
        {
            try
            {
                return new SessionCaptureItem(
                    GraphicsCaptureItemFactory.CreateForWindow(hwnd),
                    "Using matched HWND capture item.");
            }
            catch (Exception ex)
            {
                return new SessionCaptureItem(
                    _captureItem ?? throw new InvalidOperationException("Pick a source first."),
                    $"Using picker item; HWND capture unavailable: {ex.Message}");
            }
        }

        return new SessionCaptureItem(
            _captureItem ?? throw new InvalidOperationException("Pick a source first."),
            "Using picker capture item.");
    }

    private void RenderResult(RealtimeTranslationPipelineResult result, bool isPartial)
    {
        Dispatcher.Invoke(() =>
        {
            UpdateOverlay(result);

            if (result.UsedCachedTranslation)
            {
                StatusText.Text = $"Frame unchanged. Cached OCR/translation reused. {FormatTiming(result.Timing)}";
                return;
            }

            if (!result.Ocr.Changed)
            {
                StatusText.Text = $"Frame unchanged. OCR skipped. {FormatTiming(result.Timing)}";
                return;
            }

            var prefix = isPartial ? "Partial" : "Done";
            StatusText.Text = $"{prefix}: OCR {result.Ocr.OcrResult?.Blocks.Count ?? 0} block(s), translated {result.TranslatedBlocks.Count}. {FormatTiming(result.Timing)}";
            BlocksListBox.ItemsSource = result.OverlayFrame.Items
                .Select(item => $"{item.TargetRect.X:0},{item.TargetRect.Y:0} {item.TargetRect.Width:0}x{item.TargetRect.Height:0}: {item.TranslatedText}")
                .Prepend(FormatTimingDetails(result.Timing))
                .ToArray();
        });
    }

    private static string FormatTiming(RealtimeTranslationPipelineTiming timing)
    {
        return $"total {Ms(timing.Total)}ms | capture {Ms(timing.Capture)} | detect {Ms(timing.ChangeDetection)} | mask {Ms(timing.RegionMask)} | ocr {Ms(timing.Ocr)} | translate {Ms(timing.Translation)} | cache {timing.CacheHits}/{timing.CacheMisses}";
    }

    private static string FormatTimingDetails(RealtimeTranslationPipelineTiming timing)
    {
        return string.Join(
            "  ",
            $"total={Ms(timing.Total)}ms",
            $"capture={Ms(timing.Capture)}ms",
            $"change={Ms(timing.ChangeDetection)}ms",
            $"mask={Ms(timing.RegionMask)}ms",
            $"ocr={Ms(timing.Ocr)}ms",
            $"translate={Ms(timing.Translation)}ms",
            $"cacheHit={timing.CacheHits}",
            $"cacheMiss={timing.CacheMisses}");
    }

    private static long Ms(TimeSpan value)
    {
        return (long)Math.Round(value.TotalMilliseconds);
    }

    private void UpdateOverlay(RealtimeTranslationPipelineResult result)
    {
        _overlayItems.Clear();
        var showDebugBoxes = DebugOverlayBox.IsChecked == true;

        if (_overlayWindow is null)
        {
            return;
        }

        foreach (var item in result.OverlayFrame.Items)
        {
            var targetRect = _overlayWindow.ScreenPhysicalToLocalDip(item.TargetRect);
            var x = targetRect.X;
            var y = targetRect.Y;
            var paddedX = Math.Max(0, x - OverlayHorizontalPadding);
            var paddedY = Math.Max(0, y - OverlayVerticalPadding);
            var width = Math.Max(24, targetRect.Width);
            var minHeight = Math.Max(18, targetRect.Height);
            var maxWidth = Math.Max(24, _overlayWindow.ActualWidth - paddedX);

            if (showDebugBoxes)
            {
                var sourceRect = _overlayWindow.ScreenPhysicalToLocalDip(item.SourceRect ?? item.TargetRect);
                _overlayItems.Add(new TextItem
                {
                    Text = item.DebugText ?? string.Empty,
                    X = sourceRect.X,
                    Y = sourceRect.Y,
                    Width = Math.Max(24, sourceRect.Width),
                    MinHeight = Math.Max(12, sourceRect.Height),
                    FontSize = 1,
                    Background = "#00FFFFFF",
                    BorderBrush = "#FFFF2D2D",
                    Foreground = "#FFFF2D2D"
                });
            }

            _overlayItems.Add(new TextItem
            {
                Text = item.TranslatedText,
                X = paddedX,
                Y = paddedY,
                Width = Math.Min(width + (OverlayHorizontalPadding * 2), maxWidth),
                MinHeight = minHeight + (OverlayVerticalPadding * 2),
                FontSize = CalculateOverlayFontSize(item, _overlayWindow),
                Background = "White",
                BorderBrush = showDebugBoxes ? "#FF0078D4" : "Transparent",
                Foreground = "Black"
            });
        }
    }

    private static double CalculateOverlayFontSize(
        LexVerse.Core.Overlay.OverlayTextItem item,
        OverlayWindow overlayWindow)
    {
        var fontRect = overlayWindow.ScreenPhysicalToLocalDip(new(0, 0, 1, Math.Max(1, item.FontSize)));
        return Math.Clamp(fontRect.Height, 12, 28);
    }

    private RealtimeTranslationOptions CreateOptions()
    {
        var mode = ((ComboBoxItem)ModeBox.SelectedItem).Tag?.ToString();
        var baseOptions = mode switch
        {
            "subtitle" => RealtimeTranslationOptions.Subtitle,
            "game-dialogue" => RealtimeTranslationOptions.GameDialogue,
            "document" => RealtimeTranslationOptions.Document,
            _ => RealtimeTranslationOptions.FullScreen
        };

        return baseOptions with { TargetLanguage = TargetLanguageBox.Text.Trim() };
    }

    private static CaptureBinding CreateCaptureBinding(GraphicsCaptureItem item)
    {
        var candidates = new Win32WindowEnumerator().EnumerateWindows();
        var best = candidates
            .Select(candidate => new
            {
                Candidate = candidate,
                Score = ScoreWindowCandidate(candidate, item),
                HasTitleMatch = HasTitleMatch(candidate.Title, item.DisplayName)
            })
            .Where(match => match.Score >= 0)
            .OrderByDescending(match => match.Score)
            .FirstOrDefault();

        if (best is null)
        {
            return new CaptureBinding(
                null,
                new CaptureSourceInfo(CaptureSourceKind.Picker, item.DisplayName),
                "Could not infer screen bounds; overlay may be offset.",
                false);
        }

        var hwnd = best.Candidate.Hwnd;
        var tracker = new Win32WindowTracker();
        Func<PixelSize, FrameGeometry> geometryProvider = frameSize =>
        {
            var snapshot = tracker.GetSnapshot(hwnd);
            var sourceRect = ChooseSourceRect(frameSize, snapshot.WindowRect, snapshot.ClientRect);
            return new FrameGeometry(
                frameSize,
                sourceRect,
                CoordinateSpace.FrameLocal,
                snapshot.Dpi.ScaleX,
                snapshot.Dpi.ScaleY,
                snapshot.Version);
        };

        return new CaptureBinding(
            geometryProvider,
            new CaptureSourceInfo(CaptureSourceKind.Picker, item.DisplayName, hwnd),
            $"Using window bounds from {best.Candidate.ProcessName ?? "unknown"}.",
            best.HasTitleMatch);
    }

    private static double ScoreWindowCandidate(
        LexVerse.Core.Windows.WindowCandidate candidate,
        GraphicsCaptureItem item)
    {
        var titleScore = 0;
        if (candidate.Title.Equals(item.DisplayName, StringComparison.OrdinalIgnoreCase))
        {
            titleScore = 1000;
        }
        else if (candidate.Title.Contains(item.DisplayName, StringComparison.OrdinalIgnoreCase)
                 || item.DisplayName.Contains(candidate.Title, StringComparison.OrdinalIgnoreCase))
        {
            titleScore = 500;
        }

        var sizeDelta = ClosestSizeDelta(
            new PixelSize(item.Size.Width, item.Size.Height),
            candidate.WindowRect,
            candidate.ClientRect);

        var score = titleScore - (sizeDelta / 10.0);
        return score > -80 ? score : -1;
    }

    private static bool HasTitleMatch(string candidateTitle, string displayName)
    {
        return candidateTitle.Equals(displayName, StringComparison.OrdinalIgnoreCase)
            || candidateTitle.Contains(displayName, StringComparison.OrdinalIgnoreCase)
            || displayName.Contains(candidateTitle, StringComparison.OrdinalIgnoreCase);
    }

    private static ScreenRect ChooseSourceRect(
        PixelSize frameSize,
        ScreenRect windowRect,
        ScreenRect clientRect)
    {
        return SizeDelta(frameSize, clientRect) <= SizeDelta(frameSize, windowRect)
            ? clientRect
            : windowRect;
    }

    private static double ClosestSizeDelta(
        PixelSize frameSize,
        ScreenRect windowRect,
        ScreenRect? clientRect)
    {
        var windowDelta = SizeDelta(frameSize, windowRect);
        return clientRect is null
            ? windowDelta
            : Math.Min(windowDelta, SizeDelta(frameSize, clientRect.Value));
    }

    private static double SizeDelta(PixelSize frameSize, ScreenRect rect)
    {
        return Math.Abs(rect.Width - frameSize.Width) + Math.Abs(rect.Height - frameSize.Height);
    }

    private sealed record CaptureBinding(
        Func<PixelSize, FrameGeometry>? GeometryProvider,
        CaptureSourceInfo SourceInfo,
        string Status,
        bool UseMatchedWindowCaptureItem);

    private sealed record SessionCaptureItem(
        GraphicsCaptureItem Item,
        string Status);

    private static IOcrRegionProvider? CreateRegionProvider(RealtimeTranslationOptions options)
    {
        return options.Mode switch
        {
            OcrProcessingMode.Subtitle => new RelativeOcrRegionProvider([
                new RelativeOcrRegion("subtitle-bottom", 0.05, 0.58, 0.90, 0.35, OcrProcessingMode.Subtitle)
            ]),
            OcrProcessingMode.GameDialogue => new RelativeOcrRegionProvider([
                new RelativeOcrRegion("game-dialogue", 0.05, 0.55, 0.90, 0.40, OcrProcessingMode.GameDialogue)
            ]),
            _ => null
        };
    }

    private async Task StopPipelineAsync()
    {
        _pipelineCancellation?.Cancel();
        _pipelineCancellation?.Dispose();
        _pipelineCancellation = null;

        if (_captureSession is not null)
        {
            await _captureSession.DisposeAsync();
            _captureSession = null;
        }

        _overlayWindow?.Close();
        _overlayWindow = null;
        _overlayItems.Clear();

        PickButton.IsEnabled = true;
        StartButton.IsEnabled = _captureItem is not null;
        StopButton.IsEnabled = false;
    }

    protected override async void OnClosed(EventArgs e)
    {
        await StopPipelineAsync();
        base.OnClosed(e);
    }
}
