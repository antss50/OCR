using System.Collections.ObjectModel;
using System.Windows.Threading;
using LexVerse.Application.Realtime;
using LexVerse.Core.Overlay;
using LexVerse.Overlay.Models;
using OverlayWindow = LexVerse.Overlay.MainWindow;

namespace LexVerse.App.Features.Realtime;

public sealed class WpfRealtimeOverlayOutput : IRealtimeOutput
{
    private const double HorizontalPadding = 4;
    private const double VerticalPadding = 2;
    private const double MinFontSize = 8;
    private const double MaxFontSize = 44;
    private readonly ObservableCollection<TextItem> _items = [];
    private readonly Dispatcher _dispatcher;
    private OverlayWindow? _window;

    public WpfRealtimeOverlayOutput(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    public Task RenderAsync(
        RealtimeFrameUpdate update,
        bool isPartial,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        return RunOnUiAsync(() => RenderOnUi(update.OverlayFrame), cancellationToken);
    }

    public Task ClearAsync(CancellationToken cancellationToken = default) =>
        RunOnUiAsync(ClearOnUi, cancellationToken);

    private void RenderOnUi(OverlayRenderFrame frame)
    {
        if (frame.Trace is null)
        {
            _window?.Hide();
            return;
        }

        _window ??= new OverlayWindow(_items);
        _window.SetPhysicalBounds(frame.Trace.Geometry.SourceScreenRect);
        if (!_window.IsVisible)
        {
            _window.Show();
        }

        var nextItems = frame.Items.Select(CreateTextItem).ToArray();
        ReconcileItems(nextItems);
    }

    private TextItem CreateTextItem(OverlayTextItem item)
    {
        var window = _window ?? throw new InvalidOperationException("Overlay window is not initialized.");
        var targetRect = window.ScreenPhysicalToLocalDip(item.TargetRect);
        var x = Math.Max(0, targetRect.X - HorizontalPadding);
        var y = Math.Max(0, targetRect.Y - VerticalPadding);
        var maxWidth = Math.Max(24, window.ActualWidth - x);
        var fontRect = window.ScreenPhysicalToLocalDip(
            new(0, 0, 1, Math.Max(1, item.FontSize)));

        return new TextItem
        {
            Text = item.TranslatedText,
            X = x,
            Y = y,
            Width = Math.Min(Math.Max(24, targetRect.Width) + HorizontalPadding * 2, maxWidth),
            MinHeight = Math.Max(18, targetRect.Height) + VerticalPadding * 2,
            FontSize = Math.Clamp(fontRect.Height, MinFontSize, MaxFontSize),
            Background = "Black",
            BorderBrush = "Transparent",
            Foreground = "White"
        };
    }

    private void ReconcileItems(IReadOnlyList<TextItem> nextItems)
    {
        for (var index = 0; index < nextItems.Count; index++)
        {
            if (index >= _items.Count)
            {
                _items.Add(nextItems[index]);
                continue;
            }

            Copy(nextItems[index], _items[index]);
        }

        while (_items.Count > nextItems.Count)
        {
            _items.RemoveAt(_items.Count - 1);
        }
    }

    private static void Copy(TextItem source, TextItem target)
    {
        target.Text = source.Text;
        target.X = source.X;
        target.Y = source.Y;
        target.Width = source.Width;
        target.MinHeight = source.MinHeight;
        target.FontSize = source.FontSize;
        target.Background = source.Background;
        target.BorderBrush = source.BorderBrush;
        target.Foreground = source.Foreground;
    }

    private void ClearOnUi()
    {
        _window?.Close();
        _window = null;
        _items.Clear();
    }

    private Task RunOnUiAsync(Action action, CancellationToken cancellationToken)
    {
        if (_dispatcher.CheckAccess())
        {
            cancellationToken.ThrowIfCancellationRequested();
            action();
            return Task.CompletedTask;
        }

        return _dispatcher.InvokeAsync(
            action,
            DispatcherPriority.Render,
            cancellationToken).Task;
    }
}
