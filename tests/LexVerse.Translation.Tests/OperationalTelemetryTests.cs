using System.Reflection;
using System.Text.Json;
using LexVerse.Application.Diagnostics;
using LexVerse.Application.Realtime;
using LexVerse.Core.Pipeline;
using LexVerse.Infrastructure.Diagnostics;
using Xunit;

namespace LexVerse.Translation.Tests;

public sealed class OperationalTelemetryTests
{
    [Fact]
    public void OperationalEventContractCannotCarryArbitraryTextOrObjectPayloads()
    {
        var payloadProperties = typeof(OperationalEvent)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.PropertyType == typeof(string)
                || property.PropertyType == typeof(object)
                || typeof(System.Collections.IDictionary).IsAssignableFrom(property.PropertyType))
            .Select(property => property.Name)
            .ToArray();

        Assert.Empty(payloadProperties);
    }

    [Fact]
    public void LocalTelemetryFlushesPrivacySafeEventsAndRotatesBoundedFiles()
    {
        var directory = CreateTemporaryDirectory();
        var path = Path.Combine(directory, "operations.jsonl");
        try
        {
            using (var telemetry = new LocalOperationalTelemetry(
                       path,
                       maxFileBytes: 512,
                       retainedFiles: 2,
                       queueCapacity: 128))
            {
                for (var index = 0; index < 100; index++)
                {
                    telemetry.Record(new OperationalEvent
                    {
                        Kind = OperationalEventKind.RealtimeFrame,
                        Outcome = OperationalOutcome.Succeeded,
                        CorrelationId = Guid.NewGuid(),
                        Duration = TimeSpan.FromMilliseconds(250 + index),
                        OcrBlockCount = index,
                        TranslatedBlockCount = index,
                        FrameChanged = true
                    });
                }
            }

            Assert.True(File.Exists(path));
            Assert.True(File.Exists($"{path}.1"));
            Assert.False(File.Exists($"{path}.3"));

            var combined = string.Join(
                Environment.NewLine,
                Directory.GetFiles(directory).Select(File.ReadAllText));
            Assert.Contains("RealtimeFrame", combined, StringComparison.Ordinal);
            Assert.DoesNotContain("sourceText", combined, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("translatedText", combined, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("windowTitle", combined, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task LocalTelemetryFlushesOnDeadlineWithoutWaitingForShutdown()
    {
        var directory = CreateTemporaryDirectory();
        var path = Path.Combine(directory, "deadline.jsonl");
        try
        {
            using var telemetry = new LocalOperationalTelemetry(
                path,
                flushInterval: TimeSpan.FromMilliseconds(50));
            telemetry.Record(new OperationalEvent
            {
                Kind = OperationalEventKind.ApplicationStartup,
                Outcome = OperationalOutcome.Succeeded,
                Duration = TimeSpan.FromMilliseconds(100)
            });

            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
            while (!File.Exists(path) && DateTime.UtcNow < deadline)
            {
                await Task.Delay(20, TestContext.Current.CancellationToken);
            }

            Assert.True(File.Exists(path));
            Assert.Contains(
                "ApplicationStartup",
                await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken),
                StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void LocalExceptionLogOmitsRawMessageAndStackTrace()
    {
        var directory = CreateTemporaryDirectory();
        var path = Path.Combine(directory, "errors.jsonl");
        try
        {
            var log = new LocalExceptionLog(path);
            log.Report("test", new InvalidOperationException("sensitive-selected-text"));

            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            Assert.Equal("test", root.GetProperty("Source").GetString());
            Assert.Equal(64, root.GetProperty("Fingerprint").GetString()?.Length);
            Assert.False(root.TryGetProperty("Message", out _));
            Assert.False(root.TryGetProperty("StackTrace", out _));
            Assert.DoesNotContain(
                "sensitive-selected-text",
                root.GetRawText(),
                StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void RealtimeBudgetFlagsOnlyMeasurementsAboveConfiguredLimits()
    {
        var budget = RealtimePerformanceBudget.For(OcrProcessingMode.Document);
        var withinBudget = new RealtimeTranslationPipelineTiming(
            budget.Capture,
            TimeSpan.Zero,
            TimeSpan.Zero,
            budget.Ocr,
            budget.Translation,
            budget.Total,
            0,
            0);
        var overBudget = withinBudget with
        {
            Total = budget.Total + TimeSpan.FromMilliseconds(1)
        };

        Assert.False(budget.IsExceeded(withinBudget));
        Assert.True(budget.IsExceeded(overBudget));
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lexverse-telemetry-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
