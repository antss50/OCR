namespace LexVerse.Application.Popup;

public interface ISelectedTextReader
{
    Task<string?> ReadSelectedTextAsync(CancellationToken cancellationToken = default);
}
