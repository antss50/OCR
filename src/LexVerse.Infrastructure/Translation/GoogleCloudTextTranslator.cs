using Google.Cloud.Translation.V2;
using LexVerse.Core.Translation;
using LexVerse.Infrastructure.Configuration;

namespace LexVerse.Infrastructure.Translation;

public sealed class GoogleCloudTextTranslator : ITextTranslator
{
    private readonly TranslationClient _client;

    public GoogleCloudTextTranslator(TranslationClient? client = null, string? envFilePath = null)
    {
        if (client is null)
        {
            if (string.IsNullOrWhiteSpace(envFilePath))
            {
                DotEnvFile.LoadNearest();
            }
            else
            {
                DotEnvFile.Load(envFilePath);
            }
        }

        _client = client ?? TranslationClient.Create();
    }

    public async Task<TextTranslationResult> TranslateAsync(
        string text,
        string targetLanguage,
        string? sourceLanguage = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetLanguage);

        if (string.IsNullOrWhiteSpace(text))
        {
            return new TextTranslationResult(text, string.Empty, targetLanguage, sourceLanguage);
        }

        var response = await _client.TranslateTextAsync(
            text,
            targetLanguage,
            sourceLanguage,
            model: null,
            cancellationToken);

        return new TextTranslationResult(
            text,
            response.TranslatedText,
            response.TargetLanguage,
            response.DetectedSourceLanguage ?? sourceLanguage);
    }

    public async Task<IReadOnlyList<TextTranslationResult>> TranslateBatchAsync(
        IReadOnlyList<string> texts,
        string targetLanguage,
        string? sourceLanguage = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(texts);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetLanguage);

        if (texts.Count == 0)
        {
            return [];
        }

        var results = new TextTranslationResult[texts.Count];
        var requestTexts = new List<string>(texts.Count);
        var requestIndexes = new List<int>(texts.Count);

        for (var i = 0; i < texts.Count; i++)
        {
            var text = texts[i];
            if (string.IsNullOrWhiteSpace(text))
            {
                results[i] = new TextTranslationResult(text, string.Empty, targetLanguage, sourceLanguage);
                continue;
            }

            requestIndexes.Add(i);
            requestTexts.Add(text);
        }

        if (requestTexts.Count == 0)
        {
            return results;
        }

        var responses = await _client.TranslateTextAsync(
            requestTexts,
            targetLanguage,
            sourceLanguage,
            model: null,
            cancellationToken);

        if (responses.Count != requestTexts.Count)
        {
            throw new InvalidOperationException(
                $"Translator returned {responses.Count} result(s) for {requestTexts.Count} text item(s).");
        }

        for (var i = 0; i < responses.Count; i++)
        {
            var index = requestIndexes[i];
            var response = responses[i];
            results[index] = new TextTranslationResult(
                requestTexts[i],
                response.TranslatedText,
                response.TargetLanguage,
                response.DetectedSourceLanguage ?? sourceLanguage);
        }

        return results;
    }

    public async Task<IReadOnlyList<TextTranslationResult>> TranslateBatchAsync(
        IReadOnlyList<string> texts,
        string targetLanguage,
        string? sourceLanguage,
        TranslationPromptOptions? promptOptions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(texts);

        var protectedTexts = texts
            .Select(text => TranslationTermProtector.Protect(text, promptOptions))
            .ToArray();
        var translatedResults = await TranslateBatchAsync(
            protectedTexts.Select(item => item.Text).ToArray(),
            targetLanguage,
            sourceLanguage,
            cancellationToken);

        if (translatedResults.Count != protectedTexts.Length)
        {
            throw new InvalidOperationException(
                $"Translator returned {translatedResults.Count} result(s) for {protectedTexts.Length} text item(s).");
        }

        return translatedResults
            .Select((result, index) => protectedTexts[index].Restore(result))
            .ToArray();
    }
}
