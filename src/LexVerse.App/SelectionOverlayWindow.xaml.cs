using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using LexVerse.Core.Geometry;

namespace LexVerse.App;

public partial class SelectionOverlayWindow : Window
{
    private const int VkEscape = 0x1B;
    private readonly DispatcherTimer _escapeTimer;
    private Point? _startPoint;

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

    public ScreenRect? Selection { get; private set; }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _startPoint = e.GetPosition(SelectionCanvas);
        SelectionBorder.Visibility = Visibility.Visible;
        SelectionSizeBadge.Visibility = Visibility.Visible;
        Hint.Visibility = Visibility.Collapsed;
        CaptureMouse();
    }

    private void Window_MouseMove(object sender, MouseEventArgs e)
    {
        if (_startPoint is not { } start || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        UpdateSelectionVisual(start, e.GetPosition(SelectionCanvas));
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
        Selection = new ScreenRect(
            Math.Min(startScreen.X, endScreen.X),
            Math.Min(startScreen.Y, endScreen.Y),
            Math.Abs(endScreen.X - startScreen.X),
            Math.Abs(endScreen.Y - startScreen.Y));
        DialogResult = Selection.Value.Width >= 12 && Selection.Value.Height >= 12;
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
        Canvas.SetLeft(SelectionSizeBadge, x + Math.Max(0, width - 76));
        Canvas.SetTop(SelectionSizeBadge, y + height + 8);

        var startScreen = PointToScreen(start);
        var currentScreen = PointToScreen(current);
        SelectionSizeText.Text =
            $"{Math.Abs(currentScreen.X - startScreen.X):0} × {Math.Abs(currentScreen.Y - startScreen.Y):0}";
    }

    private void CancelSelection()
    {
        ReleaseMouseCapture();
        Selection = null;
        DialogResult = false;
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);
}
