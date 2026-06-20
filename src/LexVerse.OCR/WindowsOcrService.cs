using LexVerse.Core.Imaging;
using LexVerse.Core.Ocr;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Security.Cryptography;

namespace LexVerse.OCR;

public sealed class WindowsOcrService : IOcrService
{
    private const string DefaultLanguageTag = "en-US";

    private readonly OcrEngine _engine;

    public WindowsOcrService(string? languageTag = null)
    {
        _engine = CreateEngine(languageTag ?? DefaultLanguageTag);
    }

    public async Task<LexVerse.Core.Ocr.OcrResult> RecognizeAsync(CapturedFrame frame, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);

        if (frame.PixelFormat != PixelFormat.Bgra8)
        {
            throw new NotSupportedException($"Unsupported OCR pixel format: {frame.PixelFormat}.");
        }

        if (frame.Pixels.Length < frame.ExpectedByteCount)
        {
            throw new ArgumentException("Frame pixel buffer is smaller than width/height/stride metadata.", nameof(frame));
        }

        cancellationToken.ThrowIfCancellationRequested();

        using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(
            CryptographicBuffer.CreateFromByteArray(frame.Pixels),
            BitmapPixelFormat.Bgra8,
            frame.Width,
            frame.Height,
            BitmapAlphaMode.Premultiplied);

        var result = await _engine.RecognizeAsync(bitmap);
        cancellationToken.ThrowIfCancellationRequested();

        var blocks = result.Lines
            .Select(ToTextBlock)
            .Where(block => EnglishOcrTextFilter.IsLikelyEnglish(block.Text))
            .ToArray();

        return new LexVerse.Core.Ocr.OcrResult(
            blocks,
            _engine.RecognizerLanguage.LanguageTag,
            result.TextAngle ?? 0,
            DateTimeOffset.UtcNow);
    }

    private static OcrEngine CreateEngine(string? languageTag)
    {
        if (!string.IsNullOrWhiteSpace(languageTag))
        {
            var language = new Language(languageTag);
            if (OcrEngine.IsLanguageSupported(language))
            {
                return OcrEngine.TryCreateFromLanguage(language)
                    ?? throw new InvalidOperationException($"Could not create OCR engine for {languageTag}.");
            }
        }

        var defaultLanguage = new Language(DefaultLanguageTag);
        if (OcrEngine.IsLanguageSupported(defaultLanguage))
        {
            return OcrEngine.TryCreateFromLanguage(defaultLanguage)
                ?? throw new InvalidOperationException($"Could not create OCR engine for {DefaultLanguageTag}.");
        }

        throw new InvalidOperationException($"{DefaultLanguageTag} OCR is not installed or supported on this Windows profile.");
    }

    private static OcrTextBlock ToTextBlock(OcrLine line)
    {
        var words = line.Words
            .Select(word => new LexVerse.Core.Ocr.OcrWord(word.Text, ToBoundingBox(word.BoundingRect)))
            .ToArray();

        var bounds = words.Length == 0
            ? new BoundingBox(0, 0, 0, 0)
            : words.Select(word => word.Bounds).Aggregate(BoundingBox.Union);

        return new OcrTextBlock(line.Text, bounds, words);
    }

    private static BoundingBox ToBoundingBox(Windows.Foundation.Rect rect)
    {
        return new BoundingBox(
            (int)Math.Round(rect.X),
            (int)Math.Round(rect.Y),
            (int)Math.Round(rect.Width),
            (int)Math.Round(rect.Height));
    }
}

internal static class EnglishOcrTextFilter
{
    private const string VietnameseDiacritics =
        "àáảãạăằắẳẵặâầấẩẫậđèéẻẽẹêềếểễệìíỉĩịòóỏõọôồốổỗộơờớởỡợùúủũụưừứửữựỳýỷỹỵ" +
        "ÀÁẢÃẠĂẰẮẲẴẶÂẦẤẨẪẬĐÈÉẺẼẸÊỀẾỂỄỆÌÍỈĨỊÒÓỎÕỌÔỒỐỔỖỘƠỜỚỞỠỢÙÚỦŨỤƯỪỨỬỮỰỲÝỶỸỴ";

    public static bool IsLikelyEnglish(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var letters = 0;
        var asciiLetters = 0;

        foreach (var character in text)
        {
            if (VietnameseDiacritics.Contains(character))
            {
                return false;
            }

            if (!char.IsLetter(character))
            {
                continue;
            }

            letters++;
            if (character is >= 'A' and <= 'Z' or >= 'a' and <= 'z')
            {
                asciiLetters++;
            }
        }

        if (letters == 0)
        {
            return false;
        }

        return asciiLetters / (double)letters >= 0.9;
    }
}
