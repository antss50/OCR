using System;
using System.Threading;

namespace LexVerse.App
{
    internal class Program
    {
        private static void Main(string[] args)
        {
            // Start overlay on a separate STA thread
            var overlayThread = new Thread(() =>
            {
                var app = new LexVerse.Overlay.App();
                var window = new LexVerse.Overlay.MainWindow();
                app.Run(window);
            });

            overlayThread.SetApartmentState(ApartmentState.STA);
            overlayThread.IsBackground = false; // keep process alive while overlay runs
            overlayThread.Start();

            Console.WriteLine("LexVerse.App started. Overlay launched.");
            Console.WriteLine("Press Enter to exit.");
            Console.ReadLine();
        }
    }
}
