using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using LexVerse.Application.Diagnostics;

namespace LexVerse.Infrastructure.Diagnostics;

public sealed class LocalOperationalTelemetry : IOperationalTelemetry, IDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly BoundedJsonlWriter _writer;
    private readonly Channel<OperationalEvent> _channel;
    private readonly Task _writerTask;
    private readonly TimeSpan _flushInterval;
    private int _disposed;

    public LocalOperationalTelemetry(
        string? logPath = null,
        long maxFileBytes = 1_048_576,
        int retainedFiles = 3,
        int queueCapacity = 256,
        TimeSpan? flushInterval = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(queueCapacity, 1);
        _flushInterval = flushInterval ?? TimeSpan.FromSeconds(5);
        if (_flushInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(flushInterval));
        }
        var path = logPath ?? System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LexVerse",
            "Logs",
            "operational-metrics.jsonl");
        _writer = new BoundedJsonlWriter(path, maxFileBytes, retainedFiles);
        _channel = Channel.CreateBounded<OperationalEvent>(new BoundedChannelOptions(queueCapacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest,
            AllowSynchronousContinuations = false
        });
        _writerTask = Task.Run(WriteLoopAsync);
    }

    public string LogPath => _writer.Path;

    public void Record(OperationalEvent operationalEvent)
    {
        ArgumentNullException.ThrowIfNull(operationalEvent);
        Validate(operationalEvent);
        if (Volatile.Read(ref _disposed) == 0)
        {
            _channel.Writer.TryWrite(operationalEvent);
        }
    }

    private async Task WriteLoopAsync()
    {
        var batch = new List<OperationalEvent>(32);
        var batchTimer = new System.Diagnostics.Stopwatch();
        try
        {
            while (true)
            {
                if (batch.Count == 0)
                {
                    if (!await _channel.Reader.WaitToReadAsync())
                    {
                        break;
                    }

                    batchTimer.Restart();
                }

                while (batch.Count < 32 && _channel.Reader.TryRead(out var operationalEvent))
                {
                    batch.Add(operationalEvent);
                }

                if (batch.Count >= 32)
                {
                    FlushBatch();
                    continue;
                }

                var remaining = _flushInterval - batchTimer.Elapsed;
                if (remaining <= TimeSpan.Zero)
                {
                    FlushBatch();
                    continue;
                }

                var dataAvailable = _channel.Reader.WaitToReadAsync().AsTask();
                var flushDeadline = Task.Delay(remaining);
                var completed = await Task.WhenAny(dataAvailable, flushDeadline);
                if (completed == flushDeadline)
                {
                    FlushBatch();
                }
                else if (!await dataAvailable)
                {
                    break;
                }
            }
        }
        catch
        {
        }
        finally
        {
            if (batch.Count > 0)
            {
                try
                {
                    WriteBatch(batch);
                }
                catch
                {
                }
            }
        }

        void FlushBatch()
        {
            WriteBatch(batch);
            batch.Clear();
            batchTimer.Reset();
        }
    }

    private void WriteBatch(IEnumerable<OperationalEvent> batch)
    {
        _writer.Append(batch.Select(item => JsonSerializer.Serialize(item, SerializerOptions)));
    }

    private static void Validate(OperationalEvent operationalEvent)
    {
        if (operationalEvent.TimestampUtc == default)
        {
            throw new ArgumentException("Telemetry timestamp is required.", nameof(operationalEvent));
        }

        foreach (var duration in new[]
                 {
                     operationalEvent.Duration,
                     operationalEvent.CaptureDuration,
                     operationalEvent.OcrDuration,
                     operationalEvent.TranslationDuration
                 })
        {
            if (duration < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(operationalEvent), "Telemetry durations cannot be negative.");
            }
        }

        if (operationalEvent.OcrBlockCount < 0
            || operationalEvent.TranslatedBlockCount < 0
            || operationalEvent.CacheHitCount < 0
            || operationalEvent.CacheMissCount < 0
            || operationalEvent.ModuleCount < 0
            || operationalEvent.FrameCount < 0
            || operationalEvent.PerformanceBudgetExceededCount < 0
            || operationalEvent.WorkingSetBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(operationalEvent), "Telemetry counters cannot be negative.");
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _channel.Writer.TryComplete();
        try
        {
            _writerTask.GetAwaiter().GetResult();
        }
        catch
        {
        }
    }
}
