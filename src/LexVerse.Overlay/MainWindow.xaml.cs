using System;
using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using LexVerse.Core.Geometry;
using LexVerse.Overlay.Models;

namespace LexVerse.Overlay
{
    public partial class MainWindow : Window
    {
        public ObservableCollection<TextItem> Items { get; }

        public ScreenPoint VirtualScreenOrigin { get; private set; }

        public MainWindow()
            : this(new ObservableCollection<TextItem>())
        {
        }

        public MainWindow(ObservableCollection<TextItem> items)
        {
            Items = items ?? throw new ArgumentNullException(nameof(items));
            InitializeComponent();
            DataContext = this;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            var virtualLeft = GetSystemMetrics(SM_XVIRTUALSCREEN);
            var virtualTop = GetSystemMetrics(SM_YVIRTUALSCREEN);
            var virtualWidth = GetSystemMetrics(SM_CXVIRTUALSCREEN);
            var virtualHeight = GetSystemMetrics(SM_CYVIRTUALSCREEN);
            var dpi = VisualTreeHelper.GetDpi(this);

            VirtualScreenOrigin = new ScreenPoint(virtualLeft, virtualTop);
            Left = virtualLeft / dpi.DpiScaleX;
            Top = virtualTop / dpi.DpiScaleY;
            Width = virtualWidth / dpi.DpiScaleX;
            Height = virtualHeight / dpi.DpiScaleY;

            var hwnd = new WindowInteropHelper(this).Handle;
            int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            exStyle |= WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            SetWindowLong(hwnd, GWL_EXSTYLE, exStyle);
        }

        public Rect ScreenPhysicalToLocalDip(ScreenRect physicalRect)
        {
            return new PhysicalPixelToDipConverter(this, VirtualScreenOrigin).ToLocalDip(physicalRect);
        }

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x20;
        private const int WS_EX_LAYERED = 0x80000;
        private const int WS_EX_TOOLWINDOW = 0x80;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int SM_XVIRTUALSCREEN = 76;
        private const int SM_YVIRTUALSCREEN = 77;
        private const int SM_CXVIRTUALSCREEN = 78;
        private const int SM_CYVIRTUALSCREEN = 79;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);
    }
}
