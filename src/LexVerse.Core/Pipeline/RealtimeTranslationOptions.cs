using LexVerse.Core.Translation;

namespace LexVerse.Core.Pipeline;

public sealed record RealtimeTranslationOptions(
    OcrProcessingMode Mode,
    string SourceLanguage,
    string TargetLanguage,
    TimeSpan OcrInterval)
{
    public int MaxParallelTranslationRequests { get; init; } = 2;

    public int MaxTranslationBatchSize { get; init; } = 12;

    public TranslationPromptOptions TranslationPrompt { get; init; } = TranslationPromptOptions.Empty;

    public static RealtimeTranslationOptions Subtitle { get; } = new(
        OcrProcessingMode.Subtitle,
        "auto",
        "vi",
        TimeSpan.FromMilliseconds(500))
    {
        MaxParallelTranslationRequests = 2,
        MaxTranslationBatchSize = 8
    };

    public static RealtimeTranslationOptions GameDialogue { get; } = new(
        OcrProcessingMode.GameDialogue,
        "auto",
        "vi",
        TimeSpan.FromMilliseconds(350))
    {
        MaxParallelTranslationRequests = 2,
        MaxTranslationBatchSize = 6
    };

    public static RealtimeTranslationOptions Document { get; } = new(
        OcrProcessingMode.Document,
        "auto",
        "vi",
        TimeSpan.FromMilliseconds(450))
    {
        MaxParallelTranslationRequests = 3,
        MaxTranslationBatchSize = 16
    };

    public static RealtimeTranslationOptions FullScreen { get; } = new(
        OcrProcessingMode.FullScreen,
        "auto",
        "vi",
        TimeSpan.FromMilliseconds(900))
    {
        MaxParallelTranslationRequests = 3,
        MaxTranslationBatchSize = 16
    };

    public static RealtimeTranslationOptions Comic { get; } = new(
        OcrProcessingMode.Comic,
        "auto",
        "vi",
        TimeSpan.FromMilliseconds(650))
    {
        MaxParallelTranslationRequests = 3,
        MaxTranslationBatchSize = 10
    };
}
