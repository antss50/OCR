using System.Text;

namespace LexVerse.Infrastructure.Diagnostics;

internal sealed class BoundedJsonlWriter
{
    private readonly object _gate = new();
    private readonly string _path;
    private readonly long _maxFileBytes;
    private readonly int _retainedFiles;

    public BoundedJsonlWriter(string path, long maxFileBytes, int retainedFiles)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxFileBytes, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(retainedFiles, 1);
        _path = path;
        _maxFileBytes = maxFileBytes;
        _retainedFiles = retainedFiles;
    }

    public string Path => _path;

    public void Append(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var payload = string.Join(Environment.NewLine, lines) + Environment.NewLine;
        var payloadBytes = Encoding.UTF8.GetByteCount(payload);

        lock (_gate)
        {
            var directory = System.IO.Path.GetDirectoryName(_path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var currentBytes = File.Exists(_path) ? new FileInfo(_path).Length : 0;
            if (currentBytes > 0 && currentBytes + payloadBytes > _maxFileBytes)
            {
                Rotate();
            }

            File.AppendAllText(_path, payload, new UTF8Encoding(false));
        }
    }

    private void Rotate()
    {
        var oldest = $"{_path}.{_retainedFiles}";
        if (File.Exists(oldest))
        {
            File.Delete(oldest);
        }

        for (var index = _retainedFiles - 1; index >= 1; index--)
        {
            var source = $"{_path}.{index}";
            var destination = $"{_path}.{index + 1}";
            if (File.Exists(source))
            {
                File.Move(source, destination, overwrite: true);
            }
        }

        if (File.Exists(_path))
        {
            File.Move(_path, $"{_path}.1", overwrite: true);
        }
    }
}
