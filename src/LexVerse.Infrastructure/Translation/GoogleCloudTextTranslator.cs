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
}
