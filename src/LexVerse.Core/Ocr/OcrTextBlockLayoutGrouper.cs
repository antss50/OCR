using System.Text;
using System.Text.RegularExpressions;

namespace LexVerse.Core.Ocr;

public sealed class OcrTextBlockLayoutGrouper
{
    private const double ParagraphGapFactor = 0.95;
    private const double IndentFactor = 1.6;
    private static readonly Regex BulletOrNumberedItemRegex = new("^\\s*(?:[\\u2022\\-*]|[0-9]+[.)])\\s+", RegexOptions.Compiled);

    public IReadOnlyList<OcrTextBlock> GroupLines(IEnumerable<OcrTextBlock> lineBlocks)
    {
        ArgumentNullException.ThrowIfNull(lineBlocks);

        var rawLines = lineBlocks
            .Where(line => !string.IsNullOrWhiteSpace(line.Text) && line.Bounds.Width > 0 && line.Bounds.Height > 0)
            .ToArray();
        var lines = SplitLinesByDetectedColumnBands(rawLines)
            .SelectMany(SplitLineByHorizontalProximity)
            .ToArray();

        if (lines.Length <= 1)
        {
            return lines;
        }

        var medianLineHeight = Median(lines.Select(line => (double)line.Bounds.Height));
        var columns = BuildColumns(lines);
        var grouped = new List<OcrTextBlock>();

        foreach (var column in columns.OrderBy(column => column.Left))
        {
            grouped.AddRange(GroupColumnLines(column.Lines, medianLineHeight));
        }

        return grouped;
    }

    private static IEnumerable<OcrTextBlock> SplitLinesByDetectedColumnBands(IReadOnlyList<OcrTextBlock> lines)
    {
        var words = lines
            .SelectMany(line => line.Words)
            .Where(word => word.Bounds.Width > 0 && word.Bounds.Height > 0)
            .ToArray();

        if (words.Length < 12)
        {
            return lines;
        }

        var bands = DetectColumnBands(words);
        if (bands.Length <= 1)
        {
            return lines;
        }

        var splitLines = new List<OcrTextBlock>();
        foreach (var line in lines)
        {
            var wordsByBand = line.Words
                .GroupBy(word => FindBandIndex(bands, CenterX(word.Bounds)))
                .Where(group => group.Key >= 0)
                .OrderBy(group => bands[group.Key].Left)
                .Select(group => group.OrderBy(word => word.Bounds.X).ThenBy(word => word.Bounds.Y).ToArray())
                .Where(groupWords => groupWords.Length > 0)
                .ToArray();

            if (wordsByBand.Length <= 1)
            {
                splitLines.Add(line);
                continue;
            }

            splitLines.AddRange(wordsByBand.Select(ToTextBlock));
        }

        return splitLines;
    }

    private static IEnumerable<OcrTextBlock> SplitLineByHorizontalProximity(OcrTextBlock line)
    {
        if (line.Words.Count <= 1)
        {
            return [line];
        }

        var orderedWords = line.Words
            .OrderBy(word => word.Bounds.X)
            .ThenBy(word => word.Bounds.Y)
            .ToArray();
        var gaps = orderedWords
            .Zip(orderedWords.Skip(1), (left, right) => right.Bounds.X - left.Bounds.Right)
            .Where(gap => gap > 0)
            .Select(gap => (double)gap)
            .ToArray();
        var medianGap = Median(gaps);
        var medianFontSize = Median(orderedWords.Select(word => word.FontSize));
        var splitGap = Math.Max(30, Math.Max(medianFontSize * 1.35, medianGap * 2.0));
        var splitLines = new List<OcrTextBlock>();
        var currentWords = new List<OcrWord> { orderedWords[0] };

        for (var index = 1; index < orderedWords.Length; index++)
        {
            var previousWord = orderedWords[index - 1];
            var currentWord = orderedWords[index];
            var gap = currentWord.Bounds.X - previousWord.Bounds.Right;

            if (gap > splitGap)
            {
                splitLines.Add(ToTextBlock(currentWords));
                currentWords.Clear();
            }

            currentWords.Add(currentWord);
        }

        if (currentWords.Count > 0)
        {
            splitLines.Add(ToTextBlock(currentWords));
        }

        return splitLines.Count <= 1
            ? [line]
            : splitLines;
    }

    private static ColumnBand[] DetectColumnBands(IReadOnlyList<OcrWord> words)
    {
        var centers = words
            .Select(word => CenterX(word.Bounds))
            .Order()
            .ToArray();
        var gaps = centers
            .Zip(centers.Skip(1), (left, right) => right - left)
            .Where(gap => gap > 0)
            .ToArray();

        if (gaps.Length == 0)
        {
            return [];
        }

        var medianWordHeight = Median(words.Select(word => word.FontSize));
        var medianGap = Median(gaps);
        var columnGapThreshold = Math.Max(42, Math.Max(medianWordHeight * 2.1, medianGap * 8));
        var splitPositions = centers
            .Zip(centers.Skip(1), (left, right) => new { Left = left, Right = right, Gap = right - left })
            .Where(item => item.Gap > columnGapThreshold)
            .Select(item => (item.Left + item.Right) / 2.0)
            .ToArray();

        if (splitPositions.Length == 0)
        {
            return [];
        }

        var minX = words.Min(word => (double)word.Bounds.X);
        var maxX = words.Max(word => (double)word.Bounds.Right);
        var boundaries = new[] { minX - 1 }
            .Concat(splitPositions)
            .Concat([maxX + 1])
            .ToArray();
        var bands = new List<ColumnBand>();
        var minimumWordCount = Math.Max(5, words.Count / 100);

        for (var index = 0; index < boundaries.Length - 1; index++)
        {
            var left = boundaries[index];
            var right = boundaries[index + 1];
            var bandWords = words
                .Where(word =>
                {
                    var centerX = CenterX(word.Bounds);
                    return centerX >= left && centerX < right;
                })
                .ToArray();

            if (bandWords.Length < minimumWordCount)
            {
                continue;
            }

            bands.Add(new ColumnBand(
                bandWords.Min(word => (double)word.Bounds.X),
                bandWords.Max(word => (double)word.Bounds.Right)));
        }

        return bands.ToArray();
    }

    private static int FindBandIndex(IReadOnlyList<ColumnBand> bands, double centerX)
    {
        for (var index = 0; index < bands.Count; index++)
        {
            var band = bands[index];
            if (centerX >= band.Left && centerX <= band.Right)
            {
                return index;
            }
        }

        return -1;
    }

    private static IReadOnlyList<LayoutColumn> BuildColumns(IReadOnlyList<OcrTextBlock> lines)
    {
        var columns = new List<LayoutColumn>();
        foreach (var line in lines.OrderBy(line => line.Bounds.X).ThenBy(line => line.Bounds.Y))
        {
            var matchingColumn = columns
                .Where(column => OverlapsHorizontally(column.Left, column.Right, line.Bounds.X, line.Bounds.Right))
                .OrderBy(column => DistanceBetween(column.Left, column.Right, line.Bounds.X, line.Bounds.Right))
                .FirstOrDefault();

            if (matchingColumn is null)
            {
                columns.Add(new LayoutColumn(line));
                continue;
            }

            matchingColumn.Add(line);
        }

        return columns;
    }

    private static IReadOnlyList<OcrTextBlock> GroupColumnLines(IReadOnlyList<OcrTextBlock> columnLines, double medianLineHeight)
    {
        var orderedLines = columnLines
            .OrderBy(line => line.Bounds.Y)
            .ThenBy(line => line.Bounds.X)
            .ToArray();

        if (orderedLines.Length <= 1)
        {
            return orderedLines;
        }

        var blocks = new List<OcrTextBlock>();
        var currentLines = new List<OcrTextBlock>();

        foreach (var line in orderedLines)
        {
            if (currentLines.Count == 0)
            {
                currentLines.Add(line);
                continue;
            }

            var previousLine = currentLines[^1];
            if (ShouldStartNewBlock(previousLine, line, currentLines[0], medianLineHeight))
            {
                blocks.Add(MergeLines(currentLines));
                currentLines.Clear();
            }

            currentLines.Add(line);
        }

        if (currentLines.Count > 0)
        {
            blocks.Add(MergeLines(currentLines));
        }

        return blocks;
    }

    private static bool ShouldStartNewBlock(
        OcrTextBlock previousLine,
        OcrTextBlock currentLine,
        OcrTextBlock currentBlockFirstLine,
        double medianLineHeight)
    {
        if (IsBulletOrNumberedItem(currentLine.Text))
        {
            return true;
        }

        var verticalGap = currentLine.Bounds.Y - previousLine.Bounds.Bottom;
        if (verticalGap > Math.Max(8, medianLineHeight * ParagraphGapFactor))
        {
            return true;
        }

        var indentDelta = currentLine.Bounds.X - currentBlockFirstLine.Bounds.X;
        if (!IsBulletOrNumberedItem(currentBlockFirstLine.Text) &&
            Math.Abs(indentDelta) > Math.Max(14, medianLineHeight * IndentFactor) &&
            EndsLikeParagraph(previousLine.Text))
        {
            return true;
        }

        return false;
    }

    private static OcrTextBlock MergeLines(IReadOnlyList<OcrTextBlock> lines)
    {
        var text = ReflowLines(lines.Select(line => line.Text));
        var words = lines.SelectMany(line => line.Words).ToArray();
        var bounds = lines.Select(line => line.Bounds).Aggregate(BoundingBox.Union);
        var fontSize = words.Length == 0
            ? Median(lines.Select(line => line.FontSize))
            : Median(words.Select(word => word.FontSize));

        return new OcrTextBlock(text, bounds, words, fontSize);
    }

    private static OcrTextBlock ToTextBlock(IReadOnlyList<OcrWord> words)
    {
        var text = string.Join(' ', words.Select(word => word.Text));
        var bounds = words.Select(word => word.Bounds).Aggregate(BoundingBox.Union);
        var fontSize = Median(words.Select(word => word.FontSize));

        return new OcrTextBlock(text, bounds, words, fontSize);
    }

    private static string ReflowLines(IEnumerable<string> lines)
    {
        var builder = new StringBuilder();

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (builder.Length == 0)
            {
                builder.Append(line);
                continue;
            }

            if (builder[^1] == '-' && line.Length > 0 && char.IsLower(line[0]))
            {
                builder.Length--;
                builder.Append(line);
                continue;
            }

            builder.Append(' ');
            builder.Append(line);
        }

        return builder.ToString();
    }

    private static bool IsBulletOrNumberedItem(string text)
    {
        return BulletOrNumberedItemRegex.IsMatch(text);
    }

    private static bool EndsLikeParagraph(string text)
    {
        var trimmed = text.TrimEnd();
        return trimmed.EndsWith('.') || trimmed.EndsWith(':') || trimmed.EndsWith(';') ||
            trimmed.EndsWith('?') || trimmed.EndsWith('!');
    }

    private static bool OverlapsHorizontally(int leftA, int rightA, int leftB, int rightB)
    {
        return Math.Min(rightA, rightB) > Math.Max(leftA, leftB);
    }

    private static int DistanceBetween(int leftA, int rightA, int leftB, int rightB)
    {
        if (OverlapsHorizontally(leftA, rightA, leftB, rightB))
        {
            return 0;
        }

        return leftA > rightB
            ? leftA - rightB
            : leftB - rightA;
    }

    private static double CenterX(BoundingBox bounds)
    {
        return bounds.X + bounds.Width / 2.0;
    }

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.Where(value => value > 0).Order().ToArray();
        if (sorted.Length == 0)
        {
            return 0;
        }

        var middle = sorted.Length / 2;
        return sorted.Length % 2 == 0
            ? (sorted[middle - 1] + sorted[middle]) / 2
            : sorted[middle];
    }

    private sealed class LayoutColumn
    {
        private readonly List<OcrTextBlock> _lines = [];

        public LayoutColumn(OcrTextBlock firstLine)
        {
            Add(firstLine);
        }

        public int Left { get; private set; }

        public int Right { get; private set; }

        public IReadOnlyList<OcrTextBlock> Lines => _lines;

        public void Add(OcrTextBlock line)
        {
            if (_lines.Count == 0)
            {
                Left = line.Bounds.X;
                Right = line.Bounds.Right;
            }
            else
            {
                Left = Math.Min(Left, line.Bounds.X);
                Right = Math.Max(Right, line.Bounds.Right);
            }

            _lines.Add(line);
        }
    }

    private readonly record struct ColumnBand(double Left, double Right);
}
