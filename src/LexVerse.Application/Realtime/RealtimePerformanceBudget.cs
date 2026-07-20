using LexVerse.Core.Pipeline;

namespace LexVerse.Application.Realtime;

public sealed record RealtimePerformanceBudget(
    TimeSpan Capture,
    TimeSpan Ocr,
    TimeSpan Translation,
    TimeSpan Total)
{
    public static RealtimePerformanceBudget For(OcrProcessingMode mode) => mode switch
    {
        OcrProcessingMode.Subtitle => new(
            TimeSpan.FromMilliseconds(100),
            TimeSpan.FromMilliseconds(650),
            TimeSpan.FromMilliseconds(1_200),
            TimeSpan.FromMilliseconds(1_600)),
        OcrProcessingMode.GameDialogue => new(
            TimeSpan.FromMilliseconds(100),
            TimeSpan.FromMilliseconds(650),
            TimeSpan.FromMilliseconds(1_200),
            TimeSpan.FromMilliseconds(1_600)),
        OcrProcessingMode.Comic => new(
            TimeSpan.FromMilliseconds(150),
            TimeSpan.FromMilliseconds(1_200),
            TimeSpan.FromMilliseconds(1_800),
            TimeSpan.FromMilliseconds(2_500)),
        _ => new(
            TimeSpan.FromMilliseconds(120),
            TimeSpan.FromMilliseconds(900),
            TimeSpan.FromMilliseconds(1_500),
            TimeSpan.FromMilliseconds(2_000))
    };

    public bool IsExceeded(RealtimeTranslationPipelineTiming timing)
    {
        ArgumentNullException.ThrowIfNull(timing);
        return timing.Capture > Capture
            || timing.Ocr > Ocr
            || timing.Translation > Translation
            || timing.Total > Total;
    }
}
