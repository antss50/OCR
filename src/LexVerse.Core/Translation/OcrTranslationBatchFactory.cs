using LexVerse.Core.Ocr;

namespace LexVerse.Core.Translation;

public static class OcrTranslationBatchFactory
{
    public static OcrTranslationBatch Create(
        OcrResult ocrResult,
        string targetLanguage)
    {
        ArgumentNullException.ThrowIfNull(ocrResult);

        var blocks = ocrResult.Blocks
            .Select((block, index) => new TranslationTextBlock(
                CreateStableBlockId(block, index),
                block.Bounds,
                block.Text,
                block.FontSize))
            .ToArray();

        return new OcrTranslationBatch(ocrResult.Language, targetLanguage, blocks);
    }

    private static string CreateStableBlockId(OcrTextBlock block, int index)
    {
        return $"{index}:{block.Bounds.X},{block.Bounds.Y},{block.Bounds.Width},{block.Bounds.Height}";
    }
}
