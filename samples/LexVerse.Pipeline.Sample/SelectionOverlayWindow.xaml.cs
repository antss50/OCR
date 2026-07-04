using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using LexVerse.Core.Geometry;

namespace LexVerse.Pipeline.Sample;

public partial class SelectionOverlayWindow : Window
{
    private const int VkEscape = 0x1B;
    private Point? _startPoint;
    private readonly DispatcherTimer _escapeTimer;

    public ScreenRect? Selection { get; private set; }

    public SelectionOverlayWindow()
    {
        InitializeComponent();

        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;

        _escapeTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(50)
        };
        _escapeTimer.Tick += (_, _) =>
        {
            if ((GetAsyncKeyState(VkEscape) & 0x8000) != 0)
            {
                CancelSelection();
            }
        };

        Loaded += (_, _) =>
        {
            Activate();
            Focus();
            Keyboard.Focus(this);
            _escapeTimer.Start();
        };
        Closed += (_, _) => _escapeTimer.Stop();
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _startPoint = e.GetPosition(SelectionCanvas);
        SelectionBorder.Visibility = Visibility.Visible;
        SelectionSizeText.Visibility = Visibility.Visible;
        Hint.Visibility = Visibility.Collapsed;
        CaptureMouse();
    }

    private void Window_MouseMove(object sender, MouseEventArgs e)
    {
        if (_startPoint is not { } start || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var current = e.GetPosition(SelectionCanvas);
        UpdateSelectionVisual(start, current);
    }

    private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        ReleaseMouseCapture();

        if (_startPoint is not { } start)
        {
            DialogResult = false;
            return;
        }

        var end = e.GetPosition(SelectionCanvas);
        var startScreen = PointToScreen(start);
        var endScreen = PointToScreen(end);
        var x = Math.Min(startScreen.X, endScreen.X);
        var y = Math.Min(startScreen.Y, endScreen.Y);
        var width = Math.Abs(endScreen.X - startScreen.X);
        var height = Math.Abs(endScreen.Y - startScreen.Y);

        Selection = new ScreenRect(x, y, width, height);
        DialogResult = IsMeaningful(Selection.Value);
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            CancelSelection();
        }
    }

    private void UpdateSelectionVisual(Point start, Point current)
    {
        var x = Math.Min(start.X, current.X);
        var y = Math.Min(start.Y, current.Y);
        var width = Math.Abs(current.X - start.X);
        var height = Math.Abs(current.Y - start.Y);

        Canvas.SetLeft(SelectionBorder, x);
        Canvas.SetTop(SelectionBorder, y);
        SelectionBorder.Width = width;
        SelectionBorder.Height = height;

        Canvas.SetLeft(SelectionSizeText, x + Math.Max(0, width - 48));
        Canvas.SetTop(SelectionSizeText, y + height + 8);
        var startScreen = PointToScreen(start);
        var currentScreen = PointToScreen(current);
        SelectionSizeText.Text = $"{Math.Abs(currentScreen.X - startScreen.X):0} x {Math.Abs(currentScreen.Y - startScreen.Y):0}";
    }

    private void CancelSelection()
    {
        ReleaseMouseCapture();
        Selection = null;
        DialogResult = false;
    }

    private static bool IsMeaningful(ScreenRect rect)
    {
        return rect.Width >= 12 && rect.Height >= 12;
    }
}
