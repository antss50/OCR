namespace LexVerse.Core.Pipeline;

public sealed record RealtimeTranslationOptions(
    OcrProcessingMode Mode,
    string SourceLanguage,
    string TargetLanguage,
    TimeSpan OcrInterval)
{
    public static RealtimeTranslationOptions Subtitle { get; } = new(
        OcrProcessingMode.Subtitle,
        "auto",
        "vi",
        TimeSpan.FromMilliseconds(600));

    public static RealtimeTranslationOptions GameDialogue { get; } = new(
        OcrProcessingMode.GameDialogue,
        "auto",
        "vi",
        TimeSpan.FromMilliseconds(400));

    public static RealtimeTranslationOptions Document { get; } = new(
        OcrProcessingMode.Document,
        "auto",
        "vi",
        TimeSpan.FromMilliseconds(1000));

    public static RealtimeTranslationOptions FullScreen { get; } = new(
        OcrProcessingMode.FullScreen,
        "auto",
        "vi",
        TimeSpan.FromMilliseconds(1000));
}
