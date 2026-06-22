using System.Text;
using System.Windows;
using System.Windows.Interop;
using LexVerse.Core.Pipeline;
using LexVerse.Core.ScreenCapture;
using LexVerse.Infrastructure.Capture;
using LexVerse.OCR;
using Windows.Graphics.Capture;
using WinRT.Interop;

namespace LexVerse.Demo.Wpf;

public partial class MainWindow : Window
{
    private GraphicsCaptureItem? _captureItem;
    private WindowsGraphicsCaptureSession? _captureSession;
    private CancellationTokenSource? _captureLoopCancellation;

    public MainWindow()
    {
        InitializeComponent();
    }

    private async void PickButton_Click(object sender, RoutedEventArgs e)
    {
        StopCaptureLoop();
        await DisposeCaptureSessionAsync();

        try
        {
            var picker = new GraphicsCapturePicker();
            var windowHandle = new WindowInteropHelper(this).Handle;
            InitializeWithWindow.Initialize(picker, windowHandle);

            _captureItem = await picker.PickSingleItemAsync();
            if (_captureItem is null)
            {
                SourceText.Text = "No capture source selected.";
                StartButton.IsEnabled = false;
                DebugCaptureButton.IsEnabled = false;
                StatusText.Text = "Selection canceled.";
                return;
            }

            SourceText.Text = $"Selected: {_captureItem.DisplayName}";
            StartButton.IsEnabled = true;
            DebugCaptureButton.IsEnabled = true;
            StatusText.Text = "Source selected. Press Start to begin OCR.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not choose capture source: {ex.Message}";
            StartButton.IsEnabled = false;
            DebugCaptureButton.IsEnabled = false;
        }
    }

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (_captureItem is null)
        {
            StatusText.Text = "Choose a capture source first.";
            return;
        }

        StopCaptureLoop();
        await DisposeCaptureSessionAsync();

        try
        {
            _captureSession = WindowsGraphicsCaptureSession.Create(_captureItem);
            _captureLoopCancellation = new CancellationTokenSource();

            PickButton.IsEnabled = false;
            StartButton.IsEnabled = false;
            StopButton.IsEnabled = true;
            DebugCaptureButton.IsEnabled = true;
            StatusText.Text = "OCR running.";

            _ = RunCaptureLoopAsync(_captureLoopCancellation.Token);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not start capture: {ex.Message}";
            PickButton.IsEnabled = true;
            StartButton.IsEnabled = true;
            StopButton.IsEnabled = false;
            DebugCaptureButton.IsEnabled = _captureItem is not null;
        }
    }

    private async void StopButton_Click(object sender, RoutedEventArgs e)
    {
        StopCaptureLoop();
        await DisposeCaptureSessionAsync();

        PickButton.IsEnabled = true;
        StartButton.IsEnabled = _captureItem is not null;
        StopButton.IsEnabled = false;
        DebugCaptureButton.IsEnabled = _captureItem is not null;
        StatusText.Text = "Stopped.";
    }

    private async void DebugCaptureButton_Click(object sender, RoutedEventArgs e)
    {
        if (_captureItem is null)
        {
            StatusText.Text = "Choose a capture source first.";
            return;
        }

        DebugCaptureButton.IsEnabled = false;
        StatusText.Text = "Capturing OCR debug snapshot.";

        try
        {
            var captureSession = _captureSession;
            var disposeAfterCapture = false;

            if (captureSession is null)
            {
                captureSession = WindowsGraphicsCaptureSession.Create(_captureItem);
                disposeAfterCapture = true;
            }

            try
            {
                var frame = await captureSession.CaptureFrameAsync();
                var debugResult = await new WindowsOcrService("en-US").RecognizeWithLayoutDebugAsync(frame);
                var directory = OcrDebugSnapshotWriter.WriteSnapshot(frame, debugResult);
                StatusText.Text = $"Debug snapshot saved: {directory}";
            }
            finally
            {
                if (disposeAfterCapture)
                {
                    await captureSession.DisposeAsync();
                }
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not save debug snapshot: {ex.Message}";
        }
        finally
        {
            DebugCaptureButton.IsEnabled = _captureItem is not null;
        }
    }

    private async Task RunCaptureLoopAsync(CancellationToken cancellationToken)
    {
        if (_captureSession is null)
        {
            return;
        }

        var pipeline = new ScreenOcrPipeline(
            _captureSession,
            new ExactFrameChangeDetector(),
            new WindowsOcrService("en-US"));

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var result = await pipeline.CaptureAndRecognizeAsync(cancellationToken);
                Dispatcher.Invoke(() => RenderPipelineResult(result));
                await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    StatusText.Text = $"Capture/OCR error: {ex.Message}";
                    StopButton_Click(this, new RoutedEventArgs());
                });
                break;
            }
        }
    }

    private void RenderPipelineResult(ScreenOcrPipelineResult result)
    {
        FrameText.Text = $"{result.Frame.Width}x{result.Frame.Height}, stride {result.Frame.Stride}, captured {result.Frame.CapturedAt:HH:mm:ss.fff}";

        if (!result.Changed)
        {
            StatusText.Text = "Frame unchanged. OCR skipped.";
            return;
        }

        var ocrResult = result.OcrResult;
        if (ocrResult is null)
        {
            StatusText.Text = "Frame changed, but OCR returned no result.";
            return;
        }

        StatusText.Text = $"Frame changed. English OCR found {ocrResult.Blocks.Count} text block(s).";
        BlocksListBox.ItemsSource = ocrResult.Blocks
            .Select(block => $"{block.Bounds.X},{block.Bounds.Y} {block.Bounds.Width}x{block.Bounds.Height}, font {block.FontSize:0}: {block.Text}")
            .ToArray();

        var builder = new StringBuilder();
        foreach (var block in ocrResult.Blocks)
        {
            builder.AppendLine(block.Text);
        }

        ResultTextBox.Text = builder.ToString();
        ResultTextBox.CaretIndex = 0;
        ResultTextBox.ScrollToHome();
    }

    private void StopCaptureLoop()
    {
        _captureLoopCancellation?.Cancel();
        _captureLoopCancellation?.Dispose();
        _captureLoopCancellation = null;
    }

    private async Task DisposeCaptureSessionAsync()
    {
        if (_captureSession is not null)
        {
            await _captureSession.DisposeAsync();
            _captureSession = null;
        }
    }

    protected override async void OnClosed(EventArgs e)
    {
        StopCaptureLoop();
        await DisposeCaptureSessionAsync();
        base.OnClosed(e);
    }
}
