using System;
using System.Collections.ObjectModel;
using System.Windows.Threading;
using LexVerse.Overlay.Models;

namespace LexVerse.Overlay
{
    public class OverlayDemo
    {
        public ObservableCollection<TextItem> Items { get; } = new ObservableCollection<TextItem>();

        private readonly DispatcherTimer _timer;
        private readonly Random _rnd = new Random();

        public OverlayDemo()
        {
            // Create 5 sample text blocks
            Items.Add(new TextItem { Text = "Xin chào thế giới", X = 100, Y = 100, FontSize = 28 });
            Items.Add(new TextItem { Text = "Đây là một quyển sách", X = 300, Y = 160, FontSize = 22 });
            Items.Add(new TextItem { Text = "LexVerse Overlay Demo", X = 200, Y = 240, FontSize = 26 });
            Items.Add(new TextItem { Text = "Dòng 4 - ví dụ", X = 120, Y = 320, FontSize = 20 });
            Items.Add(new TextItem { Text = "Dòng 5 - ví dụ", X = 420, Y = 420, FontSize = 20 });

            // Timer to demonstrate realtime updates
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += Timer_Tick;
            _timer.Start();
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            // Randomly nudge positions and change one text to show updates
            for (int i = 0; i < Items.Count; i++)
            {
                var it = Items[i];
                it.X += _rnd.NextDouble() * 10 - 5;
                it.Y += _rnd.NextDouble() * 10 - 5;
            }

            // update content of first item periodically
            var first = Items[0];
            first.Text = DateTime.Now.ToString("HH:mm:ss") + " - Xin chào thế giới";
        }
    }
}
