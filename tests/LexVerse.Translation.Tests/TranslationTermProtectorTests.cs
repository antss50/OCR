using LexVerse.Core.Translation;
using Xunit;

namespace LexVerse.Translation.Tests;

public sealed class TranslationTermProtectorTests
{
    [Fact]
    public async Task TranslateAsync_WithIgnoredTerms_RestoresTermsAfterTranslation()
    {
        ITextTranslator translator = new TokenEchoTranslator();
        var options = new TranslationPromptOptions(
            ignoredTerms: ["HP", "Byzantine emperor"]);

        var result = await translator.TranslateAsync(
            "HP increased for the Byzantine emperor.",
            "vi",
            "en",
            options,
            TestContext.Current.CancellationToken);

        Assert.DoesNotContain("LXVERSEKEEP", result.TranslatedText);
        Assert.Contains("HP", result.TranslatedText);
        Assert.Contains("Byzantine emperor", result.TranslatedText);
        Assert.Equal("HP increased for the Byzantine emperor.", result.SourceText);
    }

    [Fact]
    public async Task TranslateBatchAsync_WithAcronymPreservation_RestoresAcronyms()
    {
        ITextTranslator translator = new TokenEchoTranslator();
        var options = new TranslationPromptOptions(
            preserveAcronymsAndTechnicalTerms: true);

        var results = await translator.TranslateBatchAsync(
            ["OCR detected HP and EXP."],
            "vi",
            "en",
            options,
            TestContext.Current.CancellationToken);

        Assert.Single(results);
        Assert.Contains("OCR", results[0].TranslatedText);
        Assert.Contains("HP", results[0].TranslatedText);
        Assert.Contains("EXP", results[0].TranslatedText);
        Assert.DoesNotContain("LXVERSEKEEP", results[0].TranslatedText);
    }

    [Theory]
    [InlineData("HP")]
    [InlineData("MP 10/20")]
    [InlineData("EXP: 45")]
    [InlineData("FPS 60")]
    [InlineData("HP, MP, EXP, FPS")]
    public void ShouldIgnoreDetectedText_WhenTextContainsOnlyIgnoredStats_ReturnsTrue(string text)
    {
        var options = new TranslationPromptOptions(
            ignoredTerms: ["HP", "MP", "EXP", "FPS"],
            preserveAcronymsAndTechnicalTerms: true);

        Assert.True(TranslationTermProtector.ShouldIgnoreDetectedText(text, options));
    }

    [Fact]
    public void ShouldIgnoreDetectedText_WhenIgnoredTermAppearsInSentence_ReturnsFalse()
    {
        var options = new TranslationPromptOptions(
            ignoredTerms: ["HP"],
            preserveAcronymsAndTechnicalTerms: true);

        Assert.False(TranslationTermProtector.ShouldIgnoreDetectedText("HP increased after battle.", options));
    }

    private sealed class TokenEchoTranslator : ITextTranslator
    {
        public Task<TextTranslationResult> TranslateAsync(
            string text,
            string targetLanguage,
            string? sourceLanguage = null,
            CancellationToken cancellationToken = default)
        {
            Assert.DoesNotContain("HP", text);
            Assert.DoesNotContain("Byzantine emperor", text);

            return Task.FromResult(new TextTranslationResult(
                text,
                $"translated {text}",
                targetLanguage,
                sourceLanguage));
        }
    }
}
