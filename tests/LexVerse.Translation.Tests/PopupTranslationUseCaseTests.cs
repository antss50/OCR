using LexVerse.Application.Diagnostics;
using LexVerse.Application.Popup;
using LexVerse.Application.Product;
using LexVerse.Core.Product;
using LexVerse.Core.Translation;
using LexVerse.Infrastructure.Translation;
using Xunit;

namespace LexVerse.Translation.Tests;

public sealed class PopupTranslationUseCaseTests
{
    [Fact]
    public async Task TranslateSelectedText_GatesThenReadsAndTranslatesSelection()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var reader = new StubSelectedTextReader("  Hello world  ");
        var translator = new RecordingTranslator();
        var useCase = CreateUseCase(allowed: true, reader, translator);

        var result = await useCase.TranslateSelectedTextAsync(
            new PopupTranslationRequest("vi-VN", "en-US"),
            cancellationToken);

        Assert.True(result.HasSelection);
        Assert.Equal("Hello world", result.SourceText);
        Assert.Equal("translated:Hello world", result.Translation?.TranslatedText);
        Assert.Equal("vi", translator.TargetLanguage);
        Assert.Equal("en", translator.SourceLanguage);
        Assert.Equal(1, reader.CallCount);
    }

    [Fact]
    public async Task TranslateSelectedText_ReturnsNoSelectionWithoutCallingProvider()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var reader = new StubSelectedTextReader(null);
        var translator = new RecordingTranslator();
        var useCase = CreateUseCase(allowed: true, reader, translator);

        var result = await useCase.TranslateSelectedTextAsync(
            new PopupTranslationRequest("vi"),
            cancellationToken);

        Assert.Same(PopupTranslationResult.NoSelection, result);
        Assert.Equal(0, translator.CallCount);
    }

    [Fact]
    public async Task TranslateSelectedText_DeniedAccessDoesNotReadClipboardOrCallProvider()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var reader = new StubSelectedTextReader("secret text");
        var translator = new RecordingTranslator();
        var useCase = CreateUseCase(allowed: false, reader, translator);

        await Assert.ThrowsAsync<FeatureAccessDeniedException>(() =>
            useCase.TranslateSelectedTextAsync(
                new PopupTranslationRequest("vi"),
                cancellationToken));

        Assert.Equal(0, reader.CallCount);
        Assert.Equal(0, translator.CallCount);
    }

    [Fact]
    public async Task TranslateText_SupportsInAppQuickTranslationThroughSameGate()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var reader = new StubSelectedTextReader(null);
        var translator = new RecordingTranslator();
        var useCase = CreateUseCase(allowed: true, reader, translator);

        var result = await useCase.TranslateTextAsync(
            "Quick test",
            new PopupTranslationRequest("ja"),
            cancellationToken);

        Assert.Equal("translated:Quick test", result.Translation?.TranslatedText);
        Assert.Equal(0, reader.CallCount);
    }

    [Fact]
    public async Task DeferredTranslator_InitializesProviderOnceOnFirstRequest()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var factoryCalls = 0;
        var inner = new RecordingTranslator();
        var deferred = new DeferredTextTranslator(() =>
        {
            factoryCalls++;
            return inner;
        });

        Assert.Equal(0, factoryCalls);

        _ = await deferred.TranslateAsync("one", "vi", cancellationToken: cancellationToken);
        _ = await deferred.TranslateAsync("two", "vi", cancellationToken: cancellationToken);

        Assert.Equal(1, factoryCalls);
        Assert.Equal(2, inner.CallCount);
    }

    [Fact]
    public async Task PopupTelemetryRecordsOutcomeWithoutAContentPayload()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var telemetry = new RecordingTelemetry();
        var useCase = CreateUseCase(
            allowed: true,
            new StubSelectedTextReader(null),
            new RecordingTranslator(),
            telemetry);

        _ = await useCase.TranslateSelectedTextAsync(
            new PopupTranslationRequest("vi"),
            cancellationToken);

        var operationalEvent = Assert.Single(telemetry.Events);
        Assert.Equal(OperationalEventKind.PopupTranslation, operationalEvent.Kind);
        Assert.Equal(OperationalOutcome.NoSelection, operationalEvent.Outcome);
        Assert.Equal(PopupInputKind.SelectedText, operationalEvent.PopupInputKind);
        Assert.NotNull(operationalEvent.Duration);
    }

    [Fact]
    public async Task TelemetryFailureDoesNotBreakPopupTranslation()
    {
        var useCase = CreateUseCase(
            allowed: true,
            new StubSelectedTextReader(null),
            new RecordingTranslator(),
            new ThrowingTelemetry());

        var result = await useCase.TranslateTextAsync(
            "safe operation",
            new PopupTranslationRequest("vi"),
            TestContext.Current.CancellationToken);

        Assert.True(result.HasSelection);
    }

    private static PopupTranslationUseCase CreateUseCase(
        bool allowed,
        ISelectedTextReader reader,
        ITextTranslator translator,
        IOperationalTelemetry? telemetry = null)
    {
        var decision = allowed
            ? FeatureAccessDecision.Allowed(ProductFeatures.PopupTranslation, null)
            : FeatureAccessDecision.Denied(
                ProductFeatures.PopupTranslation,
                FeatureAccessDenialReason.NotEntitled);
        var gate = new FeatureGate(new FixedAccessService(decision));
        return new PopupTranslationUseCase(gate, reader, translator, telemetry);
    }

    private sealed class StubSelectedTextReader(string? text) : ISelectedTextReader
    {
        public int CallCount { get; private set; }

        public Task<string?> ReadSelectedTextAsync(CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(text);
        }
    }

    private sealed class RecordingTranslator : ITextTranslator
    {
        public int CallCount { get; private set; }

        public string? TargetLanguage { get; private set; }

        public string? SourceLanguage { get; private set; }

        public Task<TextTranslationResult> TranslateAsync(
            string text,
            string targetLanguage,
            string? sourceLanguage = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            TargetLanguage = targetLanguage;
            SourceLanguage = sourceLanguage;
            return Task.FromResult(new TextTranslationResult(
                text,
                $"translated:{text}",
                targetLanguage,
                sourceLanguage));
        }
    }

    private sealed class FixedAccessService(FeatureAccessDecision decision) : IFeatureAccessService
    {
        public ValueTask<FeatureAccessDecision> GetAccessAsync(
            FeatureKey feature,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(decision);

        public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void Invalidate() { }
    }

    private sealed class RecordingTelemetry : IOperationalTelemetry
    {
        public List<OperationalEvent> Events { get; } = [];

        public void Record(OperationalEvent operationalEvent) => Events.Add(operationalEvent);
    }

    private sealed class ThrowingTelemetry : IOperationalTelemetry
    {
        public void Record(OperationalEvent operationalEvent) =>
            throw new IOException("telemetry unavailable");
    }
}
