using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using LexVerse.Core.Capture;
using LexVerse.Core.Geometry;
using LexVerse.Core.Imaging;
using LexVerse.Core.Overlay;
using LexVerse.Core.Pipeline;
using LexVerse.Core.ScreenCapture;
using LexVerse.Core.Translation;
using LexVerse.Infrastructure.Capture;
using LexVerse.Infrastructure.Translation;
using LexVerse.Infrastructure.Windows;
using LexVerse.OCR;
using LexVerse.Overlay.Models;
using Windows.Graphics.Capture;
using OverlayWindow = LexVerse.Overlay.MainWindow;

namespace LexVerse.Pipeline.Sample;

public partial class MainWindow : Window
{
    private const double OverlayHorizontalPadding = 4;
    private const double OverlayVerticalPadding = 2;
    private const string LightBlockBackground = "White";
    private const string LightBlockForeground = "Black";
    private const string DarkBlockBackground = "Black";
    private const string DarkBlockForeground = "White";
    private const double MinBaseOverlayFontSize = 12;
    private const double MaxBaseOverlayFontSize = 28;
    private const double MinOverlayFontSize = 8;
    private const double MaxOverlayFontSize = 44;
    private const double MinOverlayFontScale = 0.6;
    private const double MaxOverlayFontScale = 1.8;
    private const double OverlayFontScaleStep = 0.1;
    private const int HotkeyId = 0x4C56;
    private const int WmHotkey = 0x0312;
    private const uint VkF6 = 0x75;
    private const uint ModNoRepeat = 0x4000;
    private const byte VkControl = 0x11;
    private const byte VkC = 0x43;
    private const uint KeyEventKeyUp = 0x0002;
    private const uint MonitorDefaultToNearest = 0x00000002;
    private const double WindowEdgeDragThickness = 18;
    private const int WmNcLeftButtonDown = 0x00A1;
    private static readonly IntPtr HtCaption = new(0x0002);

    private readonly ObservableCollection<TextItem> _overlayItems = [];
    private readonly Lazy<ITextTranslator> _popupTranslator = new(() => new GoogleCloudTextTranslator());
    private PopupKeywordExplainer? _popupKeywordExplainer;
    private GraphicsCaptureItem? _captureItem;
    private Func<PixelSize, FrameGeometry>? _captureGeometryProvider;
    private CaptureSourceInfo? _captureSourceInfo;
    private ScreenRect? _selectedScreenOcrRegion;
    private bool _useMatchedWindowCaptureItem;
    private readonly Win32WindowTracker _selectedWindowTracker = new();
    private WindowsGraphicsCaptureSession? _captureSession;
    private OverlayWindow? _overlayWindow;
    private CancellationTokenSource? _pipelineCancellation;
    private CancellationTokenSource? _popupTranslationCancellation;
    private HwndSource? _hwndSource;
    private bool _isSelectingRegion;
    private bool _isHotkeyRegistered;
    private OverlayRenderFrame? _lastOverlayFrame;
    private double _overlayFontScale = 1;

    public MainWindow()
    {
        InitializeComponent();
        KeyDown += MainWindow_KeyDown;
        UpdateFontSizeControls();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            ToggleWindowState();
            return;
        }

        BeginWindowDragMove();
    }

    private void WindowShell_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left ||
            WindowState == WindowState.Maximized ||
            IsInteractiveElement(e.OriginalSource as DependencyObject))
        {
            return;
        }

        var point = e.GetPosition(this);
        if (!IsNearWindowDragEdge(point))
        {
            return;
        }

        e.Handled = true;
        BeginWindowDragMove();
    }

    private void MinimizeWindow_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeWindow_Click(object sender, RoutedEventArgs e)
    {
        ToggleWindowState();
    }

    private void CloseWindow_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ToggleWindowState()
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void BeginWindowDragMove()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        ReleaseCapture();
        SendMessage(handle, WmNcLeftButtonDown, HtCaption, IntPtr.Zero);
    }

    private bool IsNearWindowDragEdge(Point point)
    {
        return point.X <= WindowEdgeDragThickness
            || point.Y <= WindowEdgeDragThickness
            || ActualWidth - point.X <= WindowEdgeDragThickness
            || ActualHeight - point.Y <= WindowEdgeDragThickness;
    }

    private static bool IsInteractiveElement(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is ButtonBase
                or ComboBox
                or TextBoxBase
                or Slider
                or ScrollBar
                or ListBox
                or ListBoxItem)
            {
                return true;
            }

            source = GetDependencyObjectParent(source);
        }

        return false;
    }

    private static DependencyObject? GetDependencyObjectParent(DependencyObject source)
    {
        return source is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
            ? System.Windows.Media.VisualTreeHelper.GetParent(source)
            : LogicalTreeHelper.GetParent(source);
    }

    private async void ActivatePopup_Click(object sender, RoutedEventArgs e)
    {
        await TranslateHighlightedTextWithPopupAsync();
    }

    private void ShowPopupDemo_Click(object sender, RoutedEventArgs e)
    {
        const string sourceText = "414 - Byzantine emperor Theodosius II proclaimed his elder sister Aelia Pulcheria as Augusta.";
        const string translatedText = "414 - Hoàng đế Byzantine Theodosius II tuyên bố chị gái của ông là Aelia Pulcheria là Augusta.";

        var popup = CreatePopupWindow(sourceText);
        popup.Show();
        popup.ShowResult(
            sourceText,
            translatedText,
            GetSelectedTargetLanguageDisplayName(),
            GetSelectedTargetLanguageCode());
        PopupRuntimeText.Text = "Preview shown";
#if false
        var popup = CreatePopupWindow("The hero gained a new ability after completing the quest.");
        popup.Show();
        popup.ShowResult(
            "The hero gained a new ability after completing the quest.",
            "Người anh hùng đã có được một khả năng mới sau khi hoàn thành nhiệm vụ.",
            GetSelectedTargetLanguageDisplayName());
        PopupRuntimeText.Text = "Preview shown";
    }
#endif
    }

    private async void StartRegionRealtime_Click(object sender, RoutedEventArgs e)
    {
        await StartRegionRealtimeAsync();
    }

    private async void StartFullScreenRealtime_Click(object sender, RoutedEventArgs e)
    {
        await StartFullScreenRealtimeAsync();
    }

    private async Task StartPipelineFromConfiguredSourceAsync(string status)
    {
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

            SetRealtimeSourceButtonsEnabled(false);
            StopButton.IsEnabled = true;
            RealtimeStopButton.IsEnabled = true;
            StatusText.Text = $"Pipeline running. {status} {sessionItem.Status}";
            RealtimeStateText.Text = "Running";
            RealtimeRuntimeText.Text = "Running";
            PopupRuntimeText.Text = "Ready";

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

    private async Task TranslateHighlightedTextWithPopupAsync()
    {
        _popupTranslationCancellation?.Cancel();
        _popupTranslationCancellation?.Dispose();
        _popupTranslationCancellation = new CancellationTokenSource();
        var cancellationToken = _popupTranslationCancellation.Token;

        PopupRuntimeText.Text = "Reading selection";
        StatusText.Text = "Status: Reading highlighted text. Keep the text selected and press F6.";

        var sourceText = await TryReadHighlightedTextAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(sourceText))
        {
            PopupRuntimeText.Text = "No selected text";
            StatusText.Text = "Status: No highlighted text was copied. Select text in another app, then press F6.";
            return;
        }

        await TranslateTextWithPopupAsync(sourceText, cancellationToken);
    }

    private async Task TranslateTextWithPopupAsync(
        string sourceText,
        CancellationToken cancellationToken)
    {
        PopupRuntimeText.Text = "Translating";
        StatusText.Text = $"Status: Translating \"{TrimForStatus(sourceText)}\".";

        var popup = CreatePopupWindow(sourceText);
        var translateTask = _popupTranslator.Value.TranslateAsync(
            sourceText,
            GetSelectedTargetLanguageCode(),
            GetSelectedSourceLanguageForTranslation(),
            CreateTranslationPromptOptions(),
            cancellationToken);
        var loadingDelay = ShowPopupBeforeTranslationFinishesBox.IsChecked == true
            ? TimeSpan.Zero
            : TimeSpan.FromMilliseconds(320);

        if (await Task.WhenAny(translateTask, Task.Delay(loadingDelay, cancellationToken)) != translateTask)
        {
            popup.Show();
        }

        try
        {
            var result = await translateTask;
            if (!popup.IsVisible)
            {
                popup.Show();
            }

            popup.ShowResult(
                sourceText,
                result.TranslatedText,
                GetSelectedTargetLanguageDisplayName(),
                GetSelectedTargetLanguageCode());
            PopupRuntimeText.Text = "Done";
            StatusText.Text = $"Status: Popup translated \"{TrimForStatus(sourceText)}\".";
        }
        catch (OperationCanceledException)
        {
            popup.Close();
        }
        catch (Exception ex)
        {
            if (!popup.IsVisible)
            {
                popup.Show();
            }

            popup.ShowError(sourceText, ex.Message);
            PopupRuntimeText.Text = "Provider error";
            StatusText.Text = "Status: Popup translation failed.";
        }
    }

    private TranslationPopupWindow CreatePopupWindow(string sourceText)
    {
        return new TranslationPopupWindow(
            sourceText,
            ClosePopupOnOutsideClickBox.IsChecked == true,
            AllowDraggingPopupBox.IsChecked == true,
            ExplainPopupKeywordAsync);
    }

    private Task<string> ExplainPopupKeywordAsync(
        string term,
        string context,
        string targetLanguageCode,
        CancellationToken cancellationToken)
    {
        _popupKeywordExplainer ??= new PopupKeywordExplainer(_popupTranslator.Value, CreateTranslationPromptOptions);
        return _popupKeywordExplainer.ExplainAsync(
            term,
            context,
            targetLanguageCode,
            cancellationToken);
    }

    private async Task<string?> TryReadHighlightedTextAsync(CancellationToken cancellationToken)
    {
        var clipboardSequenceBeforeCopy = GetClipboardSequenceNumber();
        string? previousClipboardText = null;
        var hadTextClipboard = false;

        try
        {
            hadTextClipboard = Clipboard.ContainsText();
            if (hadTextClipboard)
            {
                previousClipboardText = Clipboard.GetText();
            }
        }
        catch
        {
        }

        SendCopyShortcut();
        await Task.Delay(140, cancellationToken);
        var clipboardSequenceAfterCopy = GetClipboardSequenceNumber();

        string? selectedText = null;
        try
        {
            selectedText = Clipboard.ContainsText()
                ? Clipboard.GetText().Trim()
                : null;
        }
        catch
        {
        }

        if (hadTextClipboard &&
            previousClipboardText is not null &&
            !string.Equals(previousClipboardText, selectedText, StringComparison.Ordinal))
        {
            try
            {
                Clipboard.SetText(previousClipboardText);
            }
            catch
            {
            }
        }

        if (clipboardSequenceAfterCopy == clipboardSequenceBeforeCopy &&
            string.Equals(previousClipboardText, selectedText, StringComparison.Ordinal))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(selectedText))
        {
            return null;
        }

        return selectedText;
    }

    private static void SendCopyShortcut()
    {
        keybd_event(VkControl, 0, 0, UIntPtr.Zero);
        keybd_event(VkC, 0, 0, UIntPtr.Zero);
        keybd_event(VkC, 0, KeyEventKeyUp, UIntPtr.Zero);
        keybd_event(VkControl, 0, KeyEventKeyUp, UIntPtr.Zero);
    }

    private async Task StartRegionRealtimeAsync()
    {
        if (_isSelectingRegion)
        {
            return;
        }

        _isSelectingRegion = true;
        StatusText.Text = "Status: Drag to select the realtime translation region.";
        RegionRuntimeText.Text = "Selecting";

        try
        {
            await StopPipelineAsync();
            WindowState = WindowState.Minimized;
            await Task.Delay(160);

            var selector = new SelectionOverlayWindow();
            var completed = selector.ShowDialog();

            WindowState = WindowState.Normal;
            Activate();

            if (completed != true || selector.Selection is not { } selection)
            {
                RegionRuntimeText.Text = "Not selected";
                StatusText.Text = "Status: Region selection canceled.";
                return;
            }

            ConfigureMonitorCapture(selection, useRegionMask: true);
            RegionRuntimeText.Text = $"{selection.Width:0} x {selection.Height:0}";
            SelectedSourceText.Text = $"Realtime region: {selection.Width:0} x {selection.Height:0}";
            await StartPipelineFromConfiguredSourceAsync($"Watching selected region {selection.Width:0}x{selection.Height:0}.");
        }
        catch (Exception ex)
        {
            RegionRuntimeText.Text = "Failed";
            StatusText.Text = $"Status: Could not start region realtime: {ex.Message}";
        }
        finally
        {
            _isSelectingRegion = false;
        }
    }

    private async Task StartFullScreenRealtimeAsync()
    {
        try
        {
            await StopPipelineAsync();
            var monitor = MonitorFromWindow(new WindowInteropHelper(this).Handle, MonitorDefaultToNearest);
            var bounds = GetMonitorBounds(monitor);
            ConfigureMonitorCapture(bounds, useRegionMask: false);
            RegionRuntimeText.Text = "Full screen";
            SelectedSourceText.Text = $"Realtime full screen: {bounds.Width:0} x {bounds.Height:0}";
            await StartPipelineFromConfiguredSourceAsync($"Watching full screen {bounds.Width:0}x{bounds.Height:0}.");
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Status: Could not start full-screen realtime: {ex.Message}";
        }
    }

    private void ConfigureMonitorCapture(ScreenRect requestedRect, bool useRegionMask)
    {
        var center = new NativePoint
        {
            X = (int)Math.Round(requestedRect.X + (requestedRect.Width / 2)),
            Y = (int)Math.Round(requestedRect.Y + (requestedRect.Height / 2))
        };
        var monitor = MonitorFromPoint(center, MonitorDefaultToNearest);
        var monitorBounds = GetMonitorBounds(monitor);

        _captureItem = GraphicsCaptureItemFactory.CreateForMonitor(monitor);
        _captureGeometryProvider = frameSize => new FrameGeometry(
            frameSize,
            monitorBounds,
            CoordinateSpace.FrameLocal,
            1,
            1,
            0);
        _captureSourceInfo = new CaptureSourceInfo(
            useRegionMask ? CaptureSourceKind.Region : CaptureSourceKind.Monitor,
            useRegionMask ? "Selected region" : "Full screen",
            MonitorDeviceName: monitor.ToString());
        _selectedScreenOcrRegion = useRegionMask ? Intersect(requestedRect, monitorBounds) : null;
        _useMatchedWindowCaptureItem = false;
    }

    private static ScreenRect GetMonitorBounds(IntPtr monitor)
    {
        if (monitor == IntPtr.Zero)
        {
            throw new InvalidOperationException("Could not find a monitor for capture.");
        }

        var info = new MonitorInfo
        {
            Size = Marshal.SizeOf<MonitorInfo>()
        };

        if (!GetMonitorInfo(monitor, ref info))
        {
            throw new InvalidOperationException("Could not read monitor bounds.");
        }

        return new ScreenRect(
            info.Monitor.Left,
            info.Monitor.Top,
            info.Monitor.Right - info.Monitor.Left,
            info.Monitor.Bottom - info.Monitor.Top);
    }

    private static ScreenRect Intersect(ScreenRect a, ScreenRect b)
    {
        var x = Math.Max(a.X, b.X);
        var y = Math.Max(a.Y, b.Y);
        var right = Math.Min(a.Right, b.Right);
        var bottom = Math.Min(a.Bottom, b.Bottom);
        return new ScreenRect(x, y, Math.Max(0, right - x), Math.Max(0, bottom - y));
    }

    private void DecreaseFontSizeButton_Click(object sender, RoutedEventArgs e)
    {
        AdjustOverlayFontScale(-OverlayFontScaleStep);
    }

    private void IncreaseFontSizeButton_Click(object sender, RoutedEventArgs e)
    {
        AdjustOverlayFontScale(OverlayFontScaleStep);
    }

    private void AdjustOverlayFontScale(double delta)
    {
        var nextScale = Math.Clamp(_overlayFontScale + delta, MinOverlayFontScale, MaxOverlayFontScale);
        nextScale = Math.Round(nextScale, 1, MidpointRounding.AwayFromZero);

        if (Math.Abs(nextScale - _overlayFontScale) < 0.001)
        {
            return;
        }

        _overlayFontScale = nextScale;
        UpdateFontSizeControls();

        if (_lastOverlayFrame is not null)
        {
            UpdateOverlay(_lastOverlayFrame);
        }
    }

    private void UpdateFontSizeControls()
    {
        FontSizeScaleText.Text = $"{_overlayFontScale * 100:0}%";
        DecreaseFontSizeButton.IsEnabled = _overlayFontScale > MinOverlayFontScale + 0.001;
        IncreaseFontSizeButton.IsEnabled = _overlayFontScale < MaxOverlayFontScale - 0.001;
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
            new WindowsOcrService(GetComboBoxTagOrText(OcrLanguageBox)),
            new GoogleCloudTextTranslator(),
            new InMemoryTranslationCache(),
            options,
            CreateRegionProvider(options));

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var iterationTimer = Stopwatch.StartNew();
                pipeline.UpdateTranslationPrompt(CreateTranslationPromptOptions());
                var result = await pipeline.CaptureRecognizeAndTranslateAsync(
                    cancellationToken,
                    (partialResult, _) =>
                    {
                        RenderResult(partialResult, isPartial: true);
                        return Task.CompletedTask;
                    });
                RenderResult(result, isPartial: false);
                await DelayUntilNextCaptureAsync(options.OcrInterval, iterationTimer.Elapsed, cancellationToken);
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

    private static async Task DelayUntilNextCaptureAsync(
        TimeSpan interval,
        TimeSpan elapsed,
        CancellationToken cancellationToken)
    {
        var remaining = interval - elapsed;
        if (remaining > TimeSpan.FromMilliseconds(10))
        {
            await Task.Delay(remaining, cancellationToken);
        }
        else
        {
            await Task.Yield();
        }
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
            RealtimeRuntimeText.Text = isPartial ? "Translating" : "Running";
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
        UpdateOverlay(result.OverlayFrame);
    }

    private void UpdateOverlay(OverlayRenderFrame frame)
    {
        if (_overlayWindow is null)
        {
            return;
        }

        _lastOverlayFrame = frame;
        _overlayItems.Clear();
        if (!ShouldShowOverlayOnSelectedSource())
        {
            _overlayWindow.Hide();
            return;
        }

        if (frame.Trace is null)
        {
            _overlayWindow.Hide();
            return;
        }

        _overlayWindow.SetPhysicalBounds(frame.Trace.Geometry.SourceScreenRect);
        if (!_overlayWindow.IsVisible)
        {
            _overlayWindow.Show();
        }

        var showDebugBoxes = DebugOverlayBox.IsChecked == true;
        var useBlackBlocks = BlackOverlayBlocksBox.IsChecked == true;
        var blockBackground = useBlackBlocks ? DarkBlockBackground : LightBlockBackground;
        var blockForeground = useBlackBlocks ? DarkBlockForeground : LightBlockForeground;

        foreach (var item in frame.Items)
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
                Background = blockBackground,
                BorderBrush = showDebugBoxes ? "#FF0078D4" : "Transparent",
                Foreground = blockForeground
            });
        }
    }

    private bool ShouldShowOverlayOnSelectedSource()
    {
        if (_captureSourceInfo?.Hwnd is not { } hwnd || hwnd == IntPtr.Zero)
        {
            return true;
        }

        try
        {
            var snapshot = _selectedWindowTracker.GetSnapshot(hwnd);
            return snapshot.IsVisible
                && !snapshot.IsMinimized
                && IsForegroundWindowOrRoot(hwnd);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsForegroundWindowOrRoot(IntPtr hwnd)
    {
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero)
        {
            return false;
        }

        return foreground == hwnd || GetAncestor(foreground, GA_ROOT) == hwnd;
    }

    private double CalculateOverlayFontSize(
        OverlayTextItem item,
        OverlayWindow overlayWindow)
    {
        var fontRect = overlayWindow.ScreenPhysicalToLocalDip(new(0, 0, 1, Math.Max(1, item.FontSize)));
        var baseFontSize = Math.Clamp(fontRect.Height, MinBaseOverlayFontSize, MaxBaseOverlayFontSize);
        return Math.Clamp(baseFontSize * _overlayFontScale, MinOverlayFontSize, MaxOverlayFontSize);
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

        return baseOptions with
        {
            TargetLanguage = GetComboBoxTagOrText(TargetLanguageBox),
            TranslationPrompt = CreateTranslationPromptOptions()
        };
    }

    private TranslationPromptOptions CreateTranslationPromptOptions()
    {
        return new TranslationPromptOptions(
            TranslationPromptBox is null ? null : TranslationPromptBox.Text,
            ParseIgnoredTerms(IgnoredTermsBox is null ? null : IgnoredTermsBox.Text),
            KeepTermsCheck?.IsChecked == true);
    }

    private static IReadOnlyList<string> ParseIgnoredTerms(string? rawTerms)
    {
        if (string.IsNullOrWhiteSpace(rawTerms))
        {
            return [];
        }

        return rawTerms
            .Split([',', ';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeIgnoredTermInput)
            .Where(term => term.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string NormalizeIgnoredTermInput(string term)
    {
        var normalized = term.Trim();
        if (normalized.EndsWith("...", StringComparison.Ordinal))
        {
            normalized = normalized[..^3].TrimEnd();
        }

        return normalized;
    }

    private void TranslationSettings_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateTranslationSettingsStatus();
    }

    private void TranslationSettings_CheckedChanged(object sender, RoutedEventArgs e)
    {
        UpdateTranslationSettingsStatus();
    }

    private void UpdateTranslationSettingsStatus()
    {
        if (TranslationPromptRuntimeText is null)
        {
            return;
        }

        var promptOptions = CreateTranslationPromptOptions();
        var labels = new List<string>
        {
            promptOptions.HasInstruction ? "Custom prompt" : "Default prompt"
        };

        if (promptOptions.HasIgnoredTerms)
        {
            labels.Add($"{promptOptions.IgnoredTerms.Count} ignored terms");
        }

        if (promptOptions.PreserveAcronymsAndTechnicalTerms)
        {
            labels.Add("Acronyms kept");
        }

        TranslationPromptRuntimeText.Text = string.Join(" | ", labels);

        if (IsLoaded)
        {
            StatusText.Text = "Status: Translation settings updated.";
        }
    }

    private static string GetComboBoxTagOrText(ComboBox comboBox)
    {
        if (comboBox.SelectedItem is ComboBoxItem item && item.Tag is not null)
        {
            return item.Tag.ToString() ?? string.Empty;
        }

        return comboBox.Text.Trim();
    }

    private string GetSelectedTargetLanguageCode()
    {
        return NormalizeTranslationLanguage(GetComboBoxTagOrText(TargetLanguageBox));
    }

    private string? GetSelectedSourceLanguageForTranslation()
    {
        var language = NormalizeTranslationLanguage(GetComboBoxTagOrText(OcrLanguageBox));
        return string.IsNullOrWhiteSpace(language) || language.Equals("auto", StringComparison.OrdinalIgnoreCase)
            ? null
            : language;
    }

    private string GetSelectedTargetLanguageDisplayName()
    {
        return TargetLanguageBox.SelectedItem is ComboBoxItem { Content: { } content }
            ? content.ToString() ?? "Translation"
            : "Translation";
    }

    private static string NormalizeTranslationLanguage(string language)
    {
        var trimmed = language.Trim();
        var separator = trimmed.IndexOf('-');
        return separator <= 0 ? trimmed : trimmed[..separator];
    }

    private static string TrimForStatus(string text)
    {
        return text.Length <= 46 ? text : string.Concat(text.AsSpan(0, 43), "...");
    }

    private sealed record SessionCaptureItem(
        GraphicsCaptureItem Item,
        string Status);

    private IOcrRegionProvider? CreateRegionProvider(RealtimeTranslationOptions options)
    {
        if (_selectedScreenOcrRegion is { Width: > 0, Height: > 0 } selectedRegion)
        {
            return new FixedScreenOcrRegionProvider(selectedRegion, options.Mode);
        }

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

    private sealed class FixedScreenOcrRegionProvider(ScreenRect screenRegion, OcrProcessingMode mode) : IOcrRegionProvider
    {
        public IReadOnlyList<OcrRegion> GetRegions(CapturedFrame frame)
        {
            ArgumentNullException.ThrowIfNull(frame);

            var source = frame.Geometry.SourceScreenRect;
            var clipped = MainWindow.Intersect(screenRegion, source);
            if (clipped.Width <= 0 || clipped.Height <= 0)
            {
                return [];
            }

            var scaleX = frame.Width / source.Width;
            var scaleY = frame.Height / source.Height;
            var x = (int)Math.Round((clipped.X - source.X) * scaleX);
            var y = (int)Math.Round((clipped.Y - source.Y) * scaleY);
            var width = (int)Math.Round(clipped.Width * scaleX);
            var height = (int)Math.Round(clipped.Height * scaleY);

            return width <= 0 || height <= 0
                ? []
                : [new OcrRegion("selected-region", x, y, width, height, mode)];
        }
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
        _lastOverlayFrame = null;
        _overlayItems.Clear();

        SetRealtimeSourceButtonsEnabled(true);
        StopButton.IsEnabled = false;
        RealtimeStopButton.IsEnabled = false;
        RealtimeStateText.Text = "Idle";
        RealtimeRuntimeText.Text = "Idle";
    }

    private void SetRealtimeSourceButtonsEnabled(bool isEnabled)
    {
        RegionButton.IsEnabled = isEnabled;
        FullScreenButton.IsEnabled = isEnabled;
        RealtimeRegionButton.IsEnabled = isEnabled;
        RealtimeFullScreenButton.IsEnabled = isEnabled;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var handle = new WindowInteropHelper(this).Handle;
        _hwndSource = HwndSource.FromHwnd(handle);
        _hwndSource?.AddHook(WndProc);
        _isHotkeyRegistered = RegisterHotKey(handle, HotkeyId, ModNoRepeat, VkF6);

        HotkeyRuntimeText.Text = _isHotkeyRegistered
            ? "F6 registered globally"
            : "F6 is used by another app";
        StatusText.Text = _isHotkeyRegistered
            ? "Status: Ready. Highlight text and press F6, or choose Region / Full screen."
            : "Status: Ready, but F6 global hotkey is already used by another app.";
    }

    protected override async void OnClosed(EventArgs e)
    {
        _popupTranslationCancellation?.Cancel();
        _popupTranslationCancellation?.Dispose();
        _popupTranslationCancellation = null;

        var handle = new WindowInteropHelper(this).Handle;
        if (_isHotkeyRegistered)
        {
            UnregisterHotKey(handle, HotkeyId);
        }

        _hwndSource?.RemoveHook(WndProc);
        await StopPipelineAsync();
        base.OnClosed(e);
    }

    private IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            _ = TranslateHighlightedTextWithPopupAsync();
        }

        return IntPtr.Zero;
    }

    private async void MainWindow_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F6)
        {
            await TranslateHighlightedTextWithPopupAsync();
        }
    }

    private const uint GA_ROOT = 2;

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo monitorInfo);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;

        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;

        public int Top;

        public int Right;

        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MonitorInfo
    {
        public int Size;

        public NativeRect Monitor;

        public NativeRect WorkArea;

        public uint Flags;
    }
}
