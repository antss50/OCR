using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using LexVerse.Overlay;
using LexVerse.Overlay.Models;

namespace LexVerse.Overlay.Sample
{
    internal class Program
    {
        [STAThread]
        private static void Main()
        {
            var demo = new OverlaySampleData();
            var app = new Application();
            var window = new MainWindow(demo.Items);

            app.Run(window);
        }
    }

    internal sealed class OverlaySampleData
    {
        private readonly Random _random = new();
        private readonly DispatcherTimer _timer;

        public ObservableCollection<TextItem> Items { get; } = new()
        {
            new TextItem { Text = "Xin chao the gioi", X = 100, Y = 100, FontSize = 28 },
            new TextItem { Text = "Day la mot quyen sach", X = 300, Y = 160, FontSize = 22 },
            new TextItem { Text = "LexVerse Overlay Sample", X = 200, Y = 240, FontSize = 26 },
            new TextItem { Text = "Dong 4 - vi du", X = 120, Y = 320, FontSize = 20 },
            new TextItem { Text = "Dong 5 - vi du", X = 420, Y = 420, FontSize = 20 }
        };

        public OverlaySampleData()
        {
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += UpdateItems;
            _timer.Start();
        }

        private void UpdateItems(object? sender, EventArgs e)
        {
            foreach (var item in Items)
            {
                item.X += _random.NextDouble() * 10 - 5;
                item.Y += _random.NextDouble() * 10 - 5;
            }

            Items[0].Text = $"{DateTime.Now:HH:mm:ss} - Xin chao the gioi";
        }
    }
}
