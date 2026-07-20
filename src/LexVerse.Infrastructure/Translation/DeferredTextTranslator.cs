using LexVerse.Core.Translation;

namespace LexVerse.Infrastructure.Translation;

/// <summary>
/// Defers provider initialization until the first translation request so missing credentials
/// do not prevent the desktop shell from starting and showing recovery guidance.
/// </summary>
public sealed class DeferredTextTranslator(Func<ITextTranslator> factory) : ITextTranslator
{
    private readonly Lazy<ITextTranslator> _inner = new(
        factory ?? throw new ArgumentNullException(nameof(factory)),
        LazyThreadSafetyMode.ExecutionAndPublication);

    public Task<TextTranslationResult> TranslateAsync(
        string text,
        string targetLanguage,
        string? sourceLanguage = null,
        CancellationToken cancellationToken = default) =>
        _inner.Value.TranslateAsync(text, targetLanguage, sourceLanguage, cancellationToken);

    public Task<TextTranslationResult> TranslateAsync(
        string text,
        string targetLanguage,
        string? sourceLanguage,
        TranslationPromptOptions? promptOptions,
        CancellationToken cancellationToken = default) =>
        _inner.Value.TranslateAsync(text, targetLanguage, sourceLanguage, promptOptions, cancellationToken);

    public Task<IReadOnlyList<TextTranslationResult>> TranslateBatchAsync(
        IReadOnlyList<string> texts,
        string targetLanguage,
        string? sourceLanguage = null,
        CancellationToken cancellationToken = default) =>
        _inner.Value.TranslateBatchAsync(texts, targetLanguage, sourceLanguage, cancellationToken);

    public Task<IReadOnlyList<TextTranslationResult>> TranslateBatchAsync(
        IReadOnlyList<string> texts,
        string targetLanguage,
        string? sourceLanguage,
        TranslationPromptOptions? promptOptions,
        CancellationToken cancellationToken = default) =>
        _inner.Value.TranslateBatchAsync(texts, targetLanguage, sourceLanguage, promptOptions, cancellationToken);
}
