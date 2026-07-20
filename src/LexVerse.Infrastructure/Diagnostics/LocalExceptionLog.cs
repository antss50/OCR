using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LexVerse.Infrastructure.Diagnostics;

public sealed class LocalExceptionLog
{
    private readonly BoundedJsonlWriter _writer;

    public LocalExceptionLog(
        string? logPath = null,
        long maxFileBytes = 1_048_576,
        int retainedFiles = 3)
    {
        var path = logPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LexVerse",
            "Logs",
            "app-errors.jsonl");
        _writer = new BoundedJsonlWriter(path, maxFileBytes, retainedFiles);
    }

    public string LogPath => _writer.Path;

    public void Report(string source, Exception exception)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentNullException.ThrowIfNull(exception);

        try
        {
            var record = JsonSerializer.Serialize(new
            {
                TimestampUtc = DateTimeOffset.UtcNow,
                Source = source,
                ExceptionType = exception.GetType().FullName,
                exception.HResult,
                InnerExceptionType = exception.InnerException?.GetType().FullName,
                Fingerprint = CreateFingerprint(exception)
            });

            _writer.Append([record]);
        }
        catch
        {
        }
    }

    private static string CreateFingerprint(Exception exception)
    {
        var material = string.Join('|',
            exception.GetType().FullName,
            exception.HResult,
            exception.StackTrace,
            exception.InnerException?.GetType().FullName);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)))
            .ToLowerInvariant();
    }
}
