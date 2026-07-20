using System.Text.Json;
using System.Text.Json.Serialization;
using LexVerse.Application.Privacy;

namespace LexVerse.Infrastructure.Privacy;

public sealed class PersistentPrivacyPreferencesService : IPrivacyPreferencesService, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private PrivacyPreferences _current;
    private bool _disposed;

    public PersistentPrivacyPreferencesService(string? path = null)
    {
        _path = Path.GetFullPath(path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LexVerse",
            "Settings",
            "privacy.json"));
        _current = Load() ?? new PrivacyPreferences();
    }

    public PrivacyPreferences Current => Volatile.Read(ref _current);

    public event EventHandler<PrivacyPreferences>? Changed;

    public async Task UpdateAsync(
        PrivacyPreferences preferences,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(preferences);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var directory = Path.GetDirectoryName(_path)
                ?? throw new InvalidOperationException("The privacy settings path has no directory.");
            Directory.CreateDirectory(directory);
            var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(_path)}.{Guid.NewGuid():N}.tmp");
            try
            {
                var wire = new PrivacyWire
                {
                    SchemaVersion = PrivacyPreferences.CurrentSchemaVersion,
                    AllowRemoteTextProcessing = preferences.AllowRemoteTextProcessing,
                    AllowLocalDiagnostics = preferences.AllowLocalDiagnostics
                };
                await using (var stream = new FileStream(
                    temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
                {
                    await JsonSerializer.SerializeAsync(stream, wire, JsonOptions, cancellationToken);
                    await stream.FlushAsync(cancellationToken);
                    stream.Flush(flushToDisk: true);
                }

                File.Move(temporaryPath, _path, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }

            Volatile.Write(ref _current, preferences);
        }
        finally
        {
            _gate.Release();
        }

        Changed?.Invoke(this, preferences);
    }

    private PrivacyPreferences? Load()
    {
        try
        {
            if (!File.Exists(_path) || new FileInfo(_path).Length is <= 0 or > 16 * 1024)
            {
                return null;
            }

            var wire = JsonSerializer.Deserialize<PrivacyWire>(File.ReadAllBytes(_path), JsonOptions);
            return wire is { SchemaVersion: PrivacyPreferences.CurrentSchemaVersion }
                ? new PrivacyPreferences(wire.AllowRemoteTextProcessing, wire.AllowLocalDiagnostics)
                : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _gate.Dispose();
    }

    private sealed class PrivacyWire
    {
        public required int SchemaVersion { get; init; }
        public required bool AllowRemoteTextProcessing { get; init; }
        public required bool AllowLocalDiagnostics { get; init; }
    }
}
