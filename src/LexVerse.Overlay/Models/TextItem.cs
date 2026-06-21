using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LexVerse.Overlay.Models
{
    public class TextItem : INotifyPropertyChanged
    {
        private string _text = string.Empty;
        private double _x;
        private double _y;
        private double _fontSize = 20;

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

        public double FontSize
        {
            get => _fontSize;
            set => SetField(ref _fontSize, value);
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
