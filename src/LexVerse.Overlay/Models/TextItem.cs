using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LexVerse.Overlay.Models
{
    public class TextItem : INotifyPropertyChanged
    {
        private string _text = string.Empty;
        private double _x;
        private double _y;
        private double _width = double.NaN;
        private double _minHeight;
        private double _fontSize = 20;
        private string _background = "White";
        private string _borderBrush = "Transparent";
        private string _foreground = "Black";

        public string Text
        {
            get => _text;
            set => SetField(ref _text, value);
        }

        public double X
        {
            get => _x;
            set => SetField(ref _x, value);
        }

        public double Y
        {
            get => _y;
            set => SetField(ref _y, value);
        }

        public double Width
        {
            get => _width;
            set => SetField(ref _width, value);
        }

        public double MinHeight
        {
            get => _minHeight;
            set => SetField(ref _minHeight, value);
        }

        public double FontSize
        {
            get => _fontSize;
            set => SetField(ref _fontSize, value);
        }

        public string Background
        {
            get => _background;
            set => SetField(ref _background, value);
        }

        public string BorderBrush
        {
            get => _borderBrush;
            set => SetField(ref _borderBrush, value);
        }

        public string Foreground
        {
            get => _foreground;
            set => SetField(ref _foreground, value);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void SetField<T>(ref T field, T value, [CallerMemberName] string? propName = null)
        {
            if (Equals(field, value))
            {
                return;
            }

            field = value;
            OnPropertyChanged(propName);
        }

        private void OnPropertyChanged([CallerMemberName] string? propName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
        }
    }
}
