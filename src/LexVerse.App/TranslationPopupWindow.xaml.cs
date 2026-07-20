using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Interop;

namespace LexVerse.App;

public partial class TranslationPopupWindow : Window
{
    private const uint WdaExcludeFromCapture = 0x00000011;
    public TranslationPopupWindow()
    {
        InitializeComponent();
        PositionNearCursor();
    }

    public void ShowResult(string sourceText, string translatedText, string targetLabel)
    {
        LoadingPanel.Visibility = Visibility.Collapsed;
        ErrorPanel.Visibility = Visibility.Collapsed;
        ResultPanel.Visibility = Visibility.Visible;
        SourceTextBlock.Text = sourceText;
        TranslatedTextBlock.Text = translatedText;
        TargetLabel.Text = targetLabel;
        RepositionInsideVirtualScreen();
    }

    public void ShowError(string title, string message)
    {
        LoadingPanel.Visibility = Visibility.Collapsed;
        ResultPanel.Visibility = Visibility.Collapsed;
        ErrorPanel.Visibility = Visibility.Visible;
        ErrorTitle.Text = title;
        ErrorMessage.Text = message;
        RepositionInsideVirtualScreen();
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(TranslatedTextBlock.Text);
            CopyButton.Content = "Copied";
        }
        catch
        {
            CopyButton.Content = "Copy failed";
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        RepositionInsideVirtualScreen();
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)));
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _ = SetWindowDisplayAffinity(new WindowInteropHelper(this).Handle, WdaExcludeFromCapture);
    }

    private void PositionNearCursor()
    {
        if (GetCursorPos(out var point))
        {
            Left = point.X + 18;
            Top = point.Y + 18;
            return;
        }

        Left = SystemParameters.WorkArea.Left + 60;
        Top = SystemParameters.WorkArea.Top + 60;
    }

    private void RepositionInsideVirtualScreen()
    {
        Dispatcher.BeginInvoke(() =>
        {
            UpdateLayout();
            const double margin = 14;
            var width = Math.Max(ActualWidth, 440);
            var height = Math.Max(ActualHeight, 120);
            var minLeft = SystemParameters.VirtualScreenLeft + margin;
            var minTop = SystemParameters.VirtualScreenTop + margin;
            var maxLeft = SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - width - margin;
            var maxTop = SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - height - margin;
            Left = Math.Clamp(Left, minLeft, Math.Max(minLeft, maxLeft));
            Top = Math.Clamp(Top, minTop, Math.Max(minTop, maxTop));
        });
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowDisplayAffinity(IntPtr window, uint affinity);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }
}
