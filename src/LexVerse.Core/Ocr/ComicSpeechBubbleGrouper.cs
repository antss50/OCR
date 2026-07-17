using System.Text;

namespace LexVerse.Core.Ocr;

public sealed class ComicSpeechBubbleGrouper
{
    private const double MaximumVerticalGapFactor = 1.9;
    private const double MinimumMaximumVerticalGap = 42;
    private const double HorizontalOverlapThreshold = 0.14;
    private const double CenterAlignmentFactor = 0.55;
    private const double MinimumCenterAlignment = 86;
    private const double FontSimilarityThreshold = 0.72;

    public IReadOnlyList<OcrTextBlock> GroupLines(IEnumerable<OcrTextBlock> lineBlocks)
    {
        ArgumentNullException.ThrowIfNull(lineBlocks);

        var lines = lineBlocks
            .Where(line => !string.IsNullOrWhiteSpace(line.Text) && line.Bounds.Width > 0 && line.Bounds.Height > 0)
            .OrderBy(line => line.Bounds.Y)
            .ThenBy(line => CenterX(line.Bounds))
            .ThenBy(line => line.Bounds.X)
            .ToArray();
        if (lines.Length <= 1)
        {
            return lines;
        }

        var medianLineHeight = Median(lines.Select(line => (double)line.Bounds.Height));
        var clusters = new List<SpeechBubbleCluster>();
        foreach (var line in lines)
        {
            var match = clusters
                .Select(cluster => new
                {
                    Cluster = cluster,
                    Score = CalculateAppendScore(cluster, line, medianLineHeight)
                })
                .Where(item => item.Score > 0)
                .OrderByDescending(item => item.Score)
                .ThenBy(item => DistanceBetweenCenters(item.Cluster.Bounds, line.Bounds))
                .FirstOrDefault();

            if (match is null)
            {
                clusters.Add(new SpeechBubbleCluster(line));
                continue;
            }

            match.Cluster.Add(line);
        }

        return clusters
            .OrderBy(cluster => cluster.Bounds.Y)
            .ThenBy(cluster => cluster.Bounds.X)
            .Select(cluster => MergeLines(cluster.Lines))
            .ToArray();
    }

    private static double CalculateAppendScore(
        SpeechBubbleCluster cluster,
        OcrTextBlock line,
        double medianLineHeight)
    {
        var previousLine = cluster.LastLine;
        var verticalGap = line.Bounds.Y - previousLine.Bounds.Bottom;
        if (verticalGap < -Math.Max(4, medianLineHeight * 0.25))
        {
            return 0;
        }

        var maximumVerticalGap = Math.Max(MinimumMaximumVerticalGap, medianLineHeight * MaximumVerticalGapFactor);
        if (verticalGap > maximumVerticalGap)
        {
            return 0;
        }

        if (!AreFontSizesSimilar(previousLine.FontSize, line.FontSize))
        {
            return 0;
        }

        var centerDistance = Math.Abs(CenterX(cluster.Bounds) - CenterX(line.Bounds));
        var centerLimit = Math.Max(
            MinimumCenterAlignment,
            Math.Max(cluster.Bounds.Width, line.Bounds.Width) * CenterAlignmentFactor);
        var centerAligned = centerDistance <= centerLimit;
        var overlapsCluster = HorizontalOverlapRatio(cluster.Bounds, line.Bounds) >= HorizontalOverlapThreshold;
        var overlapsPrevious = HorizontalOverlapRatio(previousLine.Bounds, line.Bounds) >= HorizontalOverlapThreshold;
        if (!centerAligned && !overlapsCluster && !overlapsPrevious)
        {
            return 0;
        }

        var score = 0.4;
        score += Math.Max(0, 1 - (Math.Max(0, verticalGap) / maximumVerticalGap)) * 0.25;
        if (centerAligned)
        {
            score += 0.2;
        }

        if (overlapsCluster || overlapsPrevious)
        {
            score += 0.15;
        }

        return score;
    }

    private static OcrTextBlock MergeLines(IReadOnlyList<OcrTextBlock> lines)
    {
        var orderedLines = lines
            .OrderBy(line => line.Bounds.Y)
            .ThenBy(line => line.Bounds.X)
            .ToArray();
        var text = ReflowLines(orderedLines.Select(line => line.Text));
        var words = orderedLines.SelectMany(line => line.Words).ToArray();
        var bounds = orderedLines.Select(line => line.Bounds).Aggregate(BoundingBox.Union);
        var fontSize = words.Length == 0
            ? Median(orderedLines.Select(line => line.FontSize))
            : Median(words.Select(word => word.FontSize));

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

            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            builder.Append(line);
        }

        return builder.ToString();
    }

    private static bool AreFontSizesSimilar(double left, double right)
    {
        var larger = Math.Max(left, right);
        if (larger <= 0)
        {
            return true;
        }

        var smaller = Math.Min(left, right);
        return smaller / larger >= FontSimilarityThreshold;
    }

    private static double HorizontalOverlapRatio(BoundingBox left, BoundingBox right)
    {
        var overlap = Math.Min(left.Right, right.Right) - Math.Max(left.X, right.X);
        if (overlap <= 0)
        {
            return 0;
        }

        var smallerWidth = Math.Min(left.Width, right.Width);
        return smallerWidth <= 0
            ? 0
            : overlap / (double)smallerWidth;
    }

    private static double DistanceBetweenCenters(BoundingBox left, BoundingBox right)
    {
        return Math.Abs(CenterX(left) - CenterX(right)) + Math.Abs(CenterY(left) - CenterY(right));
    }

    private static double CenterX(BoundingBox bounds)
    {
        return bounds.X + bounds.Width / 2.0;
    }

    private static double CenterY(BoundingBox bounds)
    {
        return bounds.Y + bounds.Height / 2.0;
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

    private sealed class SpeechBubbleCluster
    {
        private readonly List<OcrTextBlock> _lines = [];

        public SpeechBubbleCluster(OcrTextBlock firstLine)
        {
            Bounds = firstLine.Bounds;
            _lines.Add(firstLine);
        }

        public BoundingBox Bounds { get; private set; }

        public OcrTextBlock LastLine => _lines
            .OrderByDescending(line => line.Bounds.Y)
            .ThenByDescending(line => line.Bounds.X)
            .First();

        public IReadOnlyList<OcrTextBlock> Lines => _lines;

        public void Add(OcrTextBlock line)
        {
            Bounds = BoundingBox.Union(Bounds, line.Bounds);
            _lines.Add(line);
        }
    }
}
