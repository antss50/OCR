namespace LexVerse.Core.Windows;

public interface IWindowTracker
{
    TrackedWindowSnapshot GetSnapshot(IntPtr hwnd);
}
