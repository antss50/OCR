using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Interop;
using System.Windows.Threading;

namespace LexVerse.Pipeline.Sample;

public partial class TranslationPopupWindow : Window
{
    private const int VkLeftButton = 0x01;
    private const int GwlStyle = -16;
    private const int GwlExStyle = -20;
    private const int WmNcLeftButtonDown = 0x00A1;
    private const int WsBorder = 0x00800000;
    private const int WsDlgFrame = 0x00400000;
    private const int WsThickFrame = 0x00040000;
    private const int WsExClientEdge = 0x00000200;
    private const int WsExStaticEdge = 0x00020000;
    private const int WsExWindowEdge = 0x00000100;
    private const double PopupEdgeDragThickness = 16;
    private static readonly IntPtr HtCaption = new(0x0002);
    private static readonly Regex ProperNounRegex = new(
        @"\b(?:[A-Z][a-z]+|[A-Z]{2,}|[IVXLCDM]+)(?:\s+(?:[A-Z][a-z]+|[A-Z]{2,}|[IVXLCDM]+))*\b",
        RegexOptions.Compiled);

    private readonly bool _closeOnOutsideClick;
    private readonly bool _allowDragging;
    private readonly Func<string, string, string, CancellationToken, Task<string>> _keywordExplainer;
    private readonly DispatcherTimer _outsideClickTimer;
    private CancellationTokenSource? _keywordLookupCancellation;
    private string _sourceText;
    private string _targetLanguageCode = "vi";
    private int _keywordLookupVersion;
    private bool _wasLeftButtonPressed;

    public TranslationPopupWindow(
        string sourceText,
        bool closeOnOutsideClick,
        bool allowDragging,
        Func<string, string, string, CancellationToken, Task<string>> keywordExplainer)
    {
        InitializeComponent();
        _sourceText = sourceText;
        SourceTextBlock.Text = sourceText;
        _closeOnOutsideClick = closeOnOutsideClick;
        _allowDragging = allowDragging;
        _keywordExplainer = keywordExplainer;
        _outsideClickTimer = CreateOutsideClickTimer();
        PositionNearCursor();
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int value);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);

    public void ShowResult(
        string sourceText,
        string translatedText,
        string targetLabel,
        string targetLanguageCode)
    {
        _sourceText = sourceText;
        _targetLanguageCode = targetLanguageCode;
        LoadingPanel.Visibility = Visibility.Collapsed;
        ResultPanel.Visibility = Visibility.Visible;
        SourceTextBlock.Text = sourceText;
        TranslatedTextBlock.Text = translatedText;
        TargetLabel.Text = targetLabel;
        KeywordDetailPanel.Visibility = Visibility.Collapsed;
        RenderKeywordItems(BuildKeywordTerms(sourceText));
        RepositionInsideScreen();
    }

    public void ShowError(string sourceText, string message)
    {
        _sourceText = sourceText;
        LoadingPanel.Visibility = Visibility.Collapsed;
        ResultPanel.Visibility = Visibility.Visible;
        SourceTextBlock.Text = sourceText;
        TargetLabel.Text = "Provider message";
        TranslatedTextBlock.Text = message;
        KeywordDetailPanel.Visibility = Visibility.Collapsed;
        RenderKeywordItems(BuildKeywordTerms(sourceText));
        RepositionInsideScreen();
    }

    private void RenderKeywordItems(IReadOnlyList<PopupKeyword> keywords)
    {
        KeywordItemsPanel.Children.Clear();
        KeywordPanel.Visibility = keywords.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

        foreach (var keyword in keywords)
        {
            var button = new Button
            {
                Style = (Style)FindResource("KeywordChipButton"),
                Content = keyword.Term,
                Tag = keyword.Term,
                ToolTip = "Tìm nhanh và tóm tắt",
                MinHeight = 30
            };
            button.PreviewMouseLeftButtonDown += KeywordButton_PreviewMouseLeftButtonDown;
            button.Click += KeywordButton_Click;
            KeywordItemsPanel.Children.Add(button);
        }
    }

    private async void KeywordButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        await ShowKeywordDetailAsync(sender);
    }

    private async void KeywordButton_Click(object sender, RoutedEventArgs e)
    {
        await ShowKeywordDetailAsync(sender);
    }

    private async Task ShowKeywordDetailAsync(object sender)
    {
        if (sender is not Button { Tag: string term })
        {
            return;
        }

        _keywordLookupCancellation?.Cancel();
        var lookupVersion = ++_keywordLookupVersion;
        var lookupCancellation = new CancellationTokenSource();
        _keywordLookupCancellation = lookupCancellation;
        var cancellationToken = lookupCancellation.Token;

        KeywordDetailPanel.Visibility = Visibility.Visible;
        KeywordDetailTitle.Text = term;
        KeywordDetailText.Foreground = new SolidColorBrush(Color.FromRgb(255, 247, 233));
        KeywordDetailText.Text = "Đang tìm thông tin và tóm tắt...";
        KeywordDetailPanel.BringIntoView();
        UpdateLayout();
        RepositionInsideScreen();

        try
        {
            var summary = await _keywordExplainer(
                term,
                _sourceText,
                _targetLanguageCode,
                cancellationToken);

            if (!IsCurrentKeywordLookup(lookupVersion, lookupCancellation))
            {
                return;
            }

            KeywordDetailText.Text = summary;
            KeywordDetailPanel.BringIntoView();
            UpdateLayout();
            RepositionInsideScreen();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (!IsCurrentKeywordLookup(lookupVersion, lookupCancellation))
            {
                return;
            }

            KeywordDetailText.Text = $"Chưa tóm tắt được \"{term}\": {ex.Message}";
            KeywordDetailPanel.BringIntoView();
            UpdateLayout();
            RepositionInsideScreen();
        }
        finally
        {
            if (ReferenceEquals(_keywordLookupCancellation, lookupCancellation))
            {
                _keywordLookupCancellation = null;
            }

            lookupCancellation.Dispose();
        }
    }

    private bool IsCurrentKeywordLookup(int lookupVersion, CancellationTokenSource lookupCancellation)
    {
        return lookupVersion == _keywordLookupVersion
            && ReferenceEquals(_keywordLookupCancellation, lookupCancellation)
            && !lookupCancellation.IsCancellationRequested;
    }

    private static IReadOnlyList<PopupKeyword> BuildKeywordTerms(string sourceText)
    {
        var keywords = new List<PopupKeyword>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string term)
        {
            term = NormalizeKeywordTerm(term);
            if (term.Length == 0 || !seen.Add(term))
            {
                return;
            }

            keywords.Add(new PopupKeyword(term));
        }

        foreach (Match match in ProperNounRegex.Matches(sourceText))
        {
            var term = NormalizeKeywordTerm(match.Value);
            if (!ShouldSkipKeyword(term))
            {
                Add(term);
            }
        }

        if (sourceText.Contains("Augusta", StringComparison.OrdinalIgnoreCase))
        {
            Add("Augusta");
        }

        return keywords
            .Take(6)
            .ToArray();
    }

    private static string NormalizeKeywordTerm(string term)
    {
        term = term.Trim();
        var leadingWords = new[]
        {
            "In ",
            "At ",
            "On ",
            "From ",
            "To ",
            "By ",
            "For ",
            "With ",
            "During "
        };

        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var leadingWord in leadingWords)
            {
                if (!term.StartsWith(leadingWord, StringComparison.Ordinal))
                {
                    continue;
                }

                term = term[leadingWord.Length..].Trim();
                changed = true;
            }
        }

        return term.Trim(' ', ',', '.', ';', ':', ')', '(', '[', ']');
    }

    private static bool ShouldSkipKeyword(string term)
    {
        if (term.Length < 3)
        {
            return true;
        }

        var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "The",
            "A",
            "An",
            "I"
        };

        var parts = term.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 1 && stopWords.Contains(term);
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void HeaderBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_allowDragging || e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        e.Handled = true;
        BeginPopupDragMove();
    }

    private void PopupShell_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_allowDragging ||
            e.ChangedButton != MouseButton.Left ||
            IsInteractiveElement(e.OriginalSource as DependencyObject))
        {
            return;
        }

        var point = e.GetPosition(this);
        if (!IsNearPopupDragEdge(point) &&
            !ReferenceEquals(e.OriginalSource, PopupShell))
        {
            return;
        }

        e.Handled = true;
        BeginPopupDragMove();
    }

    private bool IsNearPopupDragEdge(Point point)
    {
        return point.X <= PopupEdgeDragThickness
            || point.Y <= PopupEdgeDragThickness
            || ActualWidth - point.X <= PopupEdgeDragThickness
            || ActualHeight - point.Y <= PopupEdgeDragThickness;
    }

    private void BeginPopupDragMove()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        ReleaseCapture();
        SendMessage(handle, WmNcLeftButtonDown, HtCaption, IntPtr.Zero);
    }

    private static bool IsInteractiveElement(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is ButtonBase
                or TextBoxBase
                or ScrollBar)
            {
                return true;
            }

            source = GetDependencyObjectParent(source);
        }

        return false;
    }

    private static DependencyObject? GetDependencyObjectParent(DependencyObject source)
    {
        return source is Visual or System.Windows.Media.Media3D.Visual3D
            ? VisualTreeHelper.GetParent(source)
            : LogicalTreeHelper.GetParent(source);
    }

    protected override void OnClosed(EventArgs e)
    {
        _keywordLookupCancellation?.Cancel();
        _keywordLookupCancellation?.Dispose();
        _outsideClickTimer.Stop();
        base.OnClosed(e);
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        RepositionInsideScreen();
        if (_closeOnOutsideClick)
        {
            _outsideClickTimer.Start();
        }

        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150)));
        if (PopupShell.RenderTransform is TranslateTransform transform)
        {
            transform.BeginAnimation(
                TranslateTransform.YProperty,
                new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(170))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        RemoveNativeFrameEdges();
    }

    private void RemoveNativeFrameEdges()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var style = GetWindowLong(handle, GwlStyle);
        style &= ~(WsBorder | WsDlgFrame | WsThickFrame);
        SetWindowLong(handle, GwlStyle, style);

        var exStyle = GetWindowLong(handle, GwlExStyle);
        exStyle &= ~(WsExClientEdge | WsExStaticEdge | WsExWindowEdge);
        SetWindowLong(handle, GwlExStyle, exStyle);
    }

    private DispatcherTimer CreateOutsideClickTimer()
    {
        var timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(80)
        };
        timer.Tick += (_, _) => CloseIfClickedOutside();
        return timer;
    }

    private void CloseIfClickedOutside()
    {
        var isLeftButtonPressed = (GetAsyncKeyState(VkLeftButton) & 0x8000) != 0;
        if (!isLeftButtonPressed)
        {
            _wasLeftButtonPressed = false;
            return;
        }

        if (_wasLeftButtonPressed)
        {
            return;
        }

        _wasLeftButtonPressed = true;
        if (!IsMouseInsidePopup())
        {
            Close();
        }
    }

    private bool IsMouseInsidePopup()
    {
        if (!GetCursorPos(out var point))
        {
            return true;
        }

        var localPoint = PointFromScreen(new Point(point.X, point.Y));
        return localPoint.X >= 0
            && localPoint.Y >= 0
            && localPoint.X <= ActualWidth
            && localPoint.Y <= ActualHeight;
    }

    private void PositionNearCursor()
    {
        if (!GetCursorPos(out var point))
        {
            Left = SystemParameters.WorkArea.Left + 80;
            Top = SystemParameters.WorkArea.Top + 80;
            return;
        }

        Left = point.X + 18;
        Top = point.Y + 18;
    }

    private void RepositionInsideScreen()
    {
        Dispatcher.BeginInvoke(() =>
        {
            UpdateLayout();
            var margin = 14;
            var screenLeft = SystemParameters.VirtualScreenLeft + margin;
            var screenTop = SystemParameters.VirtualScreenTop + margin;
            var screenRight = SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - margin;
            var screenBottom = SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - margin;
            var width = Math.Max(ActualWidth, 420);
            var height = Math.Max(ActualHeight, 120);

            Left = Clamp(Left, screenLeft, screenRight - width);
            Top = Clamp(Top, screenTop, screenBottom - height);
        }, DispatcherPriority.Render);
    }

    private static double Clamp(double value, double min, double max)
    {
        if (max < min)
        {
            return min;
        }

        return Math.Max(min, Math.Min(max, value));
    }

    private sealed record PopupKeyword(string Term);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }
}
