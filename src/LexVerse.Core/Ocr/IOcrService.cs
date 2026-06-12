using LexVerse.Core.Imaging;

namespace LexVerse.Core.Ocr;

public interface IOcrService
{
    Task<OcrResult> RecognizeAsync(CapturedFrame frame, CancellationToken cancellationToken = default);
}
