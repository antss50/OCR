using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LexVerse.Core.Imaging;
using LexVerse.Core.Ocr;
using LexVerse.OCR;
using CorePixelFormat = LexVerse.Core.Imaging.PixelFormat;
using WpfPixelFormats = System.Windows.Media.PixelFormats;

namespace LexVerse.Demo.Wpf;

internal static class OcrDebugSnapshotWriter
{
    private const double StrokeThickness = 2;

    public static string WriteSnapshot(CapturedFrame frame, OcrLayoutDebugResult debugResult)
    {
        if (frame.PixelFormat != CorePixelFormat.Bgra8)
        {
            throw new NotSupportedException($"Debug snapshots only support BGRA frames, got {frame.PixelFormat}.");
        }

        var timestamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
        var directory = Path.Combine(FindRepositoryRoot(), "artifacts", "ocr-debug", timestamp);
        Directory.CreateDirectory(directory);

        SaveImage(CreateBitmap(frame), Path.Combine(directory, "source.png"));
        SaveImage(DrawBoxes(frame, debugResult.LineBlocksBeforeGrouping, Colors.DeepSkyBlue), Path.Combine(directory, "before-grouping.png"));
        SaveImage(DrawBoxes(frame, debugResult.Result.Blocks, Colors.LimeGreen), Path.Combine(directory, "after-grouping.png"));
        File.WriteAllText(Path.Combine(directory, "blocks.txt"), CreateMetadata(frame, debugResult), Encoding.UTF8);

        return directory;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "LexVerse.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    }

    private static BitmapSource DrawBoxes(
        CapturedFrame frame,
        IReadOnlyList<OcrTextBlock> blocks,
        Color strokeColor)
    {
        var source = CreateBitmap(frame);
        var visual = new DrawingVisual();

        using (var context = visual.RenderOpen())
        {
            context.DrawImage(source, new Rect(0, 0, frame.Width, frame.Height));

            var pen = new Pen(new SolidColorBrush(strokeColor), StrokeThickness);
            pen.Freeze();

            foreach (var block in blocks)
            {
                var rect = new Rect(block.Bounds.X, block.Bounds.Y, block.Bounds.Width, block.Bounds.Height);
                context.DrawRectangle(null, pen, rect);
            }
        }

        var rendered = new RenderTargetBitmap(frame.Width, frame.Height, 96, 96, WpfPixelFormats.Pbgra32);
        rendered.Render(visual);
        rendered.Freeze();
        return rendered;
    }

    private static BitmapSource CreateBitmap(CapturedFrame frame)
    {
        var bitmap = BitmapSource.Create(
            frame.Width,
            frame.Height,
            96,
            96,
            WpfPixelFormats.Bgra32,
            null,
            frame.Pixels,
            frame.Stride);

        bitmap.Freeze();
        return bitmap;
    }

    private static void SaveImage(BitmapSource image, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));

        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static string CreateMetadata(CapturedFrame frame, OcrLayoutDebugResult debugResult)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"CapturedAt: {frame.CapturedAt:O}");
        builder.AppendLine($"Frame: {frame.Width}x{frame.Height}, stride {frame.Stride}");
        builder.AppendLine($"Language: {debugResult.Result.Language}");
        builder.AppendLine($"TextAngle: {debugResult.Result.TextAngle}");
        builder.AppendLine();

        AppendBlocks(builder, "Before grouping", debugResult.LineBlocksBeforeGrouping);
        builder.AppendLine();
        AppendBlocks(builder, "After grouping", debugResult.Result.Blocks);

        return builder.ToString();
    }

    private static void AppendBlocks(StringBuilder builder, string title, IReadOnlyList<OcrTextBlock> blocks)
    {
        builder.AppendLine($"{title}: {blocks.Count}");

        for (var index = 0; index < blocks.Count; index++)
        {
            var block = blocks[index];
            builder.AppendLine(
                $"{index + 1}. {block.Bounds.X},{block.Bounds.Y} {block.Bounds.Width}x{block.Bounds.Height}: {block.Text}");
        }
    }
}
