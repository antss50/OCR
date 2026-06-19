using Google.Cloud.Translation.V2;
using LexVerse.Infrastructure.Translation;
using Xunit;

namespace LexVerse.Translation.Tests;

public sealed class GoogleCloudTextTranslatorTests
{
    [Fact]
    public async Task TranslateAsync_WithGoogleCloudCredentials_TranslatesUsingRealApi()
    {
        var envFilePath = FindRepositoryFile(".env");
        Assert.SkipWhen(envFilePath is null, "Missing .env file for Google Cloud integration test.");

        var credentialsPath = FindGoogleCredentialsPath(envFilePath);
        Assert.SkipWhen(
            string.IsNullOrWhiteSpace(credentialsPath) || !File.Exists(credentialsPath),
            "Missing GOOGLE_APPLICATION_CREDENTIALS file for Google Cloud integration test.");

        var translator = new GoogleCloudTextTranslator(envFilePath: envFilePath);

        var result = await translator.TranslateAsync(
            "Good morning",
            "vi",
            "en",
            TestContext.Current.CancellationToken);

        Assert.Equal("Good morning", result.SourceText);
        Assert.Equal("vi", result.TargetLanguage);
        Assert.Equal("en", result.SourceLanguage);
        Assert.False(string.IsNullOrWhiteSpace(result.TranslatedText));
        Assert.NotEqual(result.SourceText, result.TranslatedText);
    }

    [Fact]
    public async Task TranslateAsync_WhenTextIsEmpty_ReturnsEmptyResultWithoutCallingClient()
    {
        var client = new FakeTranslationClient();
        var translator = new GoogleCloudTextTranslator(client);

        var result = await translator.TranslateAsync(
            string.Empty,
            "vi",
            "en",
            TestContext.Current.CancellationToken);

        Assert.Equal(string.Empty, result.SourceText);
        Assert.Equal(string.Empty, result.TranslatedText);
        Assert.Equal("vi", result.TargetLanguage);
        Assert.Equal("en", result.SourceLanguage);
        Assert.Equal(0, client.CallCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task TranslateAsync_WhenTargetLanguageIsMissing_Throws(string? targetLanguage)
    {
        var translator = new GoogleCloudTextTranslator(new FakeTranslationClient());

        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => translator.TranslateAsync(
                "hello",
                targetLanguage!,
                cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TranslateAsync_WhenTextIsProvided_DelegatesToClientAndMapsResponse()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = new FakeTranslationClient
        {
            Result = new TranslationResult(
                "hello",
                "xin chao",
                detectedSourceLanguage: "en",
                specifiedSourceLanguage: null,
                targetLanguage: "vi",
                model: null)
        };
        var translator = new GoogleCloudTextTranslator(client);

        var result = await translator.TranslateAsync(
            "hello",
            "vi",
            cancellationToken: cancellationToken);

        Assert.Equal(1, client.CallCount);
        Assert.Equal(["hello"], client.TextItems);
        Assert.Equal("vi", client.TargetLanguage);
        Assert.Null(client.SourceLanguage);
        Assert.Null(client.Model);
        Assert.Equal(cancellationToken, client.CancellationToken);
        Assert.Equal("hello", result.SourceText);
        Assert.Equal("xin chao", result.TranslatedText);
        Assert.Equal("vi", result.TargetLanguage);
        Assert.Equal("en", result.SourceLanguage);
    }

    [Fact]
    public async Task TranslateAsync_WhenGoogleDoesNotDetectSourceLanguage_UsesSpecifiedSourceLanguage()
    {
        var client = new FakeTranslationClient
        {
            Result = new TranslationResult(
                "bonjour",
                "hello",
                detectedSourceLanguage: null,
                specifiedSourceLanguage: "fr",
                targetLanguage: "en",
                model: null)
        };
        var translator = new GoogleCloudTextTranslator(client);

        var result = await translator.TranslateAsync(
            "bonjour",
            "en",
            "fr",
            TestContext.Current.CancellationToken);

        Assert.Equal("fr", result.SourceLanguage);
    }

    private sealed class FakeTranslationClient : TranslationClient
    {
        public int CallCount { get; private set; }
        public IReadOnlyList<string> TextItems { get; private set; } = [];
        public string? TargetLanguage { get; private set; }
        public string? SourceLanguage { get; private set; }
        public TranslationModel? Model { get; private set; }
        public CancellationToken CancellationToken { get; private set; }

        public TranslationResult Result { get; init; } = new(
            "hello",
            "xin chao",
            detectedSourceLanguage: "en",
            specifiedSourceLanguage: null,
            targetLanguage: "vi",
            model: null);

        public override Task<IList<TranslationResult>> TranslateTextAsync(
            IEnumerable<string> textItems,
            string targetLanguage,
            string? sourceLanguage = null,
            TranslationModel? model = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            TextItems = textItems.ToArray();
            TargetLanguage = targetLanguage;
            SourceLanguage = sourceLanguage;
            Model = model;
            CancellationToken = cancellationToken;

            return Task.FromResult<IList<TranslationResult>>([Result]);
        }
    }

    private static string? FindRepositoryFile(string fileName)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        return null;
    }

    private static string? FindGoogleCredentialsPath(string envFilePath)
    {
        var envDirectory = Path.GetDirectoryName(envFilePath) ?? Directory.GetCurrentDirectory();

        foreach (var rawLine in File.ReadLines(envFilePath))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            if (line.StartsWith("export ", StringComparison.Ordinal))
            {
                line = line["export ".Length..].TrimStart();
            }

            var separatorIndex = line.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = line[..separatorIndex].Trim();
            if (!key.Equals("GOOGLE_APPLICATION_CREDENTIALS", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = Unquote(line[(separatorIndex + 1)..].Trim());
            return Path.IsPathRooted(value) ? value : Path.GetFullPath(Path.Combine(envDirectory, value));
        }

        return null;
    }

    private static string Unquote(string value)
    {
        var commentIndex = value.IndexOf(" #", StringComparison.Ordinal);
        if (commentIndex >= 0)
        {
            value = value[..commentIndex].TrimEnd();
        }

        if (value.Length >= 2
            && ((value[0] == '"' && value[^1] == '"')
                || (value[0] == '\'' && value[^1] == '\'')))
        {
            return value[1..^1];
        }

        return value;
    }
}
