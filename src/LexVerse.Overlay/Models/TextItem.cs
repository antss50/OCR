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
            set { _text = value; OnPropertyChanged(); }
        }

        public double X
        {
            get => _x;
            set { _x = value; OnPropertyChanged(); }
        }

        public double Y
        {
            get => _y;
            set { _y = value; OnPropertyChanged(); }
        }

        public double FontSize
        {
            get => _fontSize;
            set { _fontSize = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
        }
    }
}
