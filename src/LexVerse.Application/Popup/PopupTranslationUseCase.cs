using System.Diagnostics;
using LexVerse.Application.Diagnostics;
using LexVerse.Application.Product;
using LexVerse.Core.Product;
using LexVerse.Core.Translation;

namespace LexVerse.Application.Popup;

public sealed class PopupTranslationUseCase(
    FeatureGate featureGate,
    ISelectedTextReader selectedTextReader,
    ITextTranslator translator,
    IOperationalTelemetry? telemetry = null)
{
    private readonly FeatureGate _featureGate =
        featureGate ?? throw new ArgumentNullException(nameof(featureGate));
    private readonly ISelectedTextReader _selectedTextReader =
        selectedTextReader ?? throw new ArgumentNullException(nameof(selectedTextReader));
    private readonly ITextTranslator _translator =
        translator ?? throw new ArgumentNullException(nameof(translator));
    private readonly IOperationalTelemetry _telemetry = telemetry ?? NullOperationalTelemetry.Instance;

    public async Task<PopupTranslationResult> TranslateSelectedTextAsync(
        PopupTranslationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return await ExecuteMeasuredAsync(
            PopupInputKind.SelectedText,
            token => ReadAndTranslateSelectedTextAsync(request, token),
            cancellationToken);
    }

    public async Task<PopupTranslationResult> TranslateTextAsync(
        string sourceText,
        PopupTranslationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceText);
        ArgumentNullException.ThrowIfNull(request);

        return await ExecuteMeasuredAsync(
            PopupInputKind.QuickText,
            token => TranslateCoreAsync(sourceText, request, token),
            cancellationToken);
    }

    private async Task<PopupTranslationResult> ReadAndTranslateSelectedTextAsync(
        PopupTranslationRequest request,
        CancellationToken cancellationToken)
    {
        var sourceText = await _selectedTextReader.ReadSelectedTextAsync(cancellationToken);
        return string.IsNullOrWhiteSpace(sourceText)
            ? PopupTranslationResult.NoSelection
            : await TranslateCoreAsync(sourceText, request, cancellationToken);
    }

    private async Task<PopupTranslationResult> ExecuteMeasuredAsync(
        PopupInputKind inputKind,
        Func<CancellationToken, Task<PopupTranslationResult>> action,
        CancellationToken cancellationToken)
    {
        var timer = Stopwatch.StartNew();
        try
        {
            var result = await _featureGate.ExecuteAsync(
                ProductFeatures.PopupTranslation,
                action,
                cancellationToken);
            RecordTelemetry(
                inputKind,
                result.HasSelection ? OperationalOutcome.Succeeded : OperationalOutcome.NoSelection,
                timer.Elapsed);
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            RecordTelemetry(inputKind, OperationalOutcome.Cancelled, timer.Elapsed);
            throw;
        }
        catch (FeatureAccessDeniedException)
        {
            RecordTelemetry(
                inputKind,
                OperationalOutcome.Denied,
                timer.Elapsed,
                OperationalFailureCode.Entitlement);
            throw;
        }
        catch
        {
            RecordTelemetry(
                inputKind,
                OperationalOutcome.Failed,
                timer.Elapsed,
                OperationalFailureCode.Provider);
            throw;
        }
    }

    private void RecordTelemetry(
        PopupInputKind inputKind,
        OperationalOutcome outcome,
        TimeSpan duration,
        OperationalFailureCode? failureCode = null) =>
        _telemetry.TryRecord(new OperationalEvent
        {
            Kind = OperationalEventKind.PopupTranslation,
            Outcome = outcome,
            PopupInputKind = inputKind,
            Duration = duration,
            FailureCode = failureCode
        });

    private async Task<PopupTranslationResult> TranslateCoreAsync(
        string sourceText,
        PopupTranslationRequest request,
        CancellationToken cancellationToken)
    {
        sourceText = sourceText.Trim();
        var translation = await _translator.TranslateAsync(
            sourceText,
            request.TargetLanguage,
            request.SourceLanguage,
            request.PromptOptions,
            cancellationToken);

        return PopupTranslationResult.Success(sourceText, translation);
    }
}
