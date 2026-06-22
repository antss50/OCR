using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using LexVerse.Core.Pipeline;
using LexVerse.Core.ScreenCapture;
using LexVerse.Core.Translation;
using LexVerse.Infrastructure.Capture;
using LexVerse.Infrastructure.Translation;
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
                StatusText.Text = "Selection canceled.";
                StartButton.IsEnabled = false;
                return;
            }

            StatusText.Text = $"Selected: {_captureItem.DisplayName}";
            StartButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
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
            _captureSession = WindowsGraphicsCaptureSession.Create(_captureItem);
            _overlayWindow = new OverlayWindow(_overlayItems);
            _overlayWindow.Show();
            _pipelineCancellation = new CancellationTokenSource();

            PickButton.IsEnabled = false;
            StartButton.IsEnabled = false;
            StopButton.IsEnabled = true;
            StatusText.Text = "Pipeline running.";

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
                var result = await pipeline.CaptureRecognizeAndTranslateAsync(cancellationToken);
                RenderResult(result);
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

    private void RenderResult(RealtimeTranslationPipelineResult result)
    {
        Dispatcher.Invoke(() =>
        {
            UpdateOverlay(result);

            if (!result.Ocr.Changed)
            {
                StatusText.Text = "Frame unchanged. OCR skipped.";
                return;
            }

            StatusText.Text = $"OCR {result.Ocr.OcrResult?.Blocks.Count ?? 0} block(s), translated {result.TranslatedBlocks.Count}.";
            BlocksListBox.ItemsSource = result.TranslatedBlocks
                .Select(block => $"{block.Bounds.X},{block.Bounds.Y} {block.Bounds.Width}x{block.Bounds.Height}: {block.TranslatedText}")
                .ToArray();
        });
    }

    private void UpdateOverlay(RealtimeTranslationPipelineResult result)
    {
        var translatedBlocks = result.TranslatedBlocks;
        var frame = result.Ocr.Frame;
        var overlayWidth = _overlayWindow?.ActualWidth > 0
            ? _overlayWindow.ActualWidth
            : SystemParameters.PrimaryScreenWidth;
        var overlayHeight = _overlayWindow?.ActualHeight > 0
            ? _overlayWindow.ActualHeight
            : SystemParameters.PrimaryScreenHeight;
        var scaleX = overlayWidth / frame.Width;
        var scaleY = overlayHeight / frame.Height;

        _overlayItems.Clear();
        var showDebugBoxes = DebugOverlayBox.IsChecked == true;

        for (var index = 0; index < translatedBlocks.Count; index++)
        {
            var block = translatedBlocks[index];
            var x = block.Bounds.X * scaleX;
            var y = block.Bounds.Y * scaleY;
            var width = CalculateOverlayTextWidth(block, scaleX);
            var minHeight = Math.Max(18, block.Bounds.Height * scaleY);

            if (showDebugBoxes)
            {
                _overlayItems.Add(new TextItem
                {
                    Text = string.Empty,
                    X = x,
                    Y = y,
                    Width = block.Bounds.Width * scaleX,
                    MinHeight = minHeight,
                    FontSize = 1,
                    Background = "#00FFFFFF",
                    BorderBrush = "#FFFF2D2D",
                    Foreground = "#FFFF2D2D"
                });
            }

            _overlayItems.Add(new TextItem
            {
                Text = block.TranslatedText,
                X = Math.Max(0, x - OverlayHorizontalPadding),
                Y = Math.Max(0, y - OverlayVerticalPadding),
                Width = width + (OverlayHorizontalPadding * 2),
                MinHeight = minHeight + (OverlayVerticalPadding * 2),
                FontSize = Math.Max(16, block.FontSize * scaleY),
                Background = "White",
                BorderBrush = showDebugBoxes ? "#FF0078D4" : "Transparent",
                Foreground = "Black"
            });
        }
    }

    private static double CalculateOverlayTextWidth(TranslatedTextBlock block, double scaleX)
    {
        return Math.Clamp(block.Bounds.Width * scaleX, 80, 720);
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
