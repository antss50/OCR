namespace LexVerse.Core.Windows;

public interface IWindowEnumerator
{
    IReadOnlyList<WindowCandidate> EnumerateWindows();
}
