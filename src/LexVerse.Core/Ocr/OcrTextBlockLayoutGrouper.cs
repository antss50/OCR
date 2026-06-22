using System.Text;
using System.Text.RegularExpressions;

namespace LexVerse.Core.Ocr;

public sealed class OcrTextBlockLayoutGrouper
{
    private const double IndentFactor = 1.6;
    private const double LargeVerticalGapFactor = 1.55;
    private const double MergeScoreThreshold = 0.74;
    private const double MinimumLargeVerticalGap = 26;
    private const double MinimumHorizontalSplitGap = 22;
    private const double DocumentParagraphMergeScore = 0.62;
    private static readonly Regex BulletOrNumberedItemRegex = new("^\\s*(?:[\\u2022\\-*]|[0-9]+[.)])\\s+", RegexOptions.Compiled);
    private static readonly Regex DashSeparatedListItemRegex = new("^\\s*\\S+(?:\\s+\\S+){0,4}\\s+(?:[-\\u2013\\u2014])\\s+\\S+", RegexOptions.Compiled);
    private static readonly Regex EllipsisListItemRegex = new("^\\s*(?:\\.\\.\\.|\\u2026)\\s+\\S+", RegexOptions.Compiled);
    private static readonly Regex SectionHeadingRegex = new("^\\s*(?:[0-9]+\\.)+[0-9]+\\s+\\S+", RegexOptions.Compiled);

    public IReadOnlyList<OcrTextBlock> GroupLines(IEnumerable<OcrTextBlock> lineBlocks)
    {
        ArgumentNullException.ThrowIfNull(lineBlocks);

        var lines = lineBlocks
            .Where(line => !string.IsNullOrWhiteSpace(line.Text) && line.Bounds.Width > 0 && line.Bounds.Height > 0)
            .ToArray();
        var gutters = DetectRepeatedVerticalGutters(lines);
        if (gutters.Length > 0)
        {
            lines = SplitLinesByGutters(lines, gutters).ToArray();
        }

        if (lines.Length <= 1)
        {
            return lines;
        }

        var medianLineHeight = Median(lines.Select(line => (double)line.Bounds.Height));
        var columns = BuildColumns(lines, gutters);
        var grouped = new List<OcrTextBlock>();

        foreach (var column in columns.OrderBy(column => column.Left))
        {
            grouped.AddRange(GroupColumnLines(column.Lines, medianLineHeight));
        }

        return grouped;
    }

    private static IEnumerable<OcrTextBlock> SplitLinesByGutters(
        IReadOnlyList<OcrTextBlock> lines,
        IReadOnlyList<DetectedGutter> gutters)
    {
        if (gutters.Count == 0)
        {
            return lines;
        }

        var bands = CreateColumnBands(lines, gutters);
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

    private static DetectedGutter[] DetectRepeatedVerticalGutters(IReadOnlyList<OcrTextBlock> lines)
    {
        if (lines.Count < 4)
        {
            return [];
        }

        var medianLineHeight = Median(lines.Select(line => (double)line.Bounds.Height));
        var rowTolerance = Math.Max(5, medianLineHeight * 0.65);
        var rows = BuildRows(lines, rowTolerance);
        var candidates = new List<DetectedGutter>();
        var minimumGap = Math.Max(MinimumHorizontalSplitGap, medianLineHeight * 1.35);
        var rowsWithHorizontalSplits = 0;

        foreach (var row in rows)
        {
            var rowLines = row.Lines
                .OrderBy(line => line.Bounds.X)
                .ToArray();

            var rowHasHorizontalSplit = false;
            for (var index = 1; index < rowLines.Length; index++)
            {
                var left = rowLines[index - 1];
                var right = rowLines[index];
                var gap = right.Bounds.X - left.Bounds.Right;

                if (gap < minimumGap)
                {
                    continue;
                }

                rowHasHorizontalSplit = true;
                candidates.Add(new DetectedGutter(left.Bounds.Right, right.Bounds.X, 1));
            }

            if (rowHasHorizontalSplit)
            {
                rowsWithHorizontalSplits++;
            }
        }

        if (candidates.Count < 2)
        {
            return [];
        }

        var minimumVotes = Math.Max(2, (int)Math.Ceiling(rowsWithHorizontalSplits * 0.45));
        var clusterTolerance = Math.Max(28, medianLineHeight * 1.8);
        var clusters = new List<DetectedGutterCluster>();
        foreach (var candidate in candidates.OrderBy(gutter => gutter.Center))
        {
            var cluster = clusters
                .Where(item => Math.Abs(item.Center - candidate.Center) <= clusterTolerance)
                .OrderBy(item => Math.Abs(item.Center - candidate.Center))
                .FirstOrDefault();

            if (cluster is null)
            {
                clusters.Add(new DetectedGutterCluster(candidate));
                continue;
            }

            cluster.Add(candidate);
        }

        return clusters
            .Where(cluster => cluster.Votes >= minimumVotes)
            .Select(cluster => cluster.ToGutter())
            .Where(gutter => HasTextOnBothSides(lines, gutter))
            .OrderBy(gutter => gutter.Center)
            .ToArray();
    }

    private static bool HasTextOnBothSides(IReadOnlyList<OcrTextBlock> lines, DetectedGutter gutter)
    {
        var leftCount = 0;
        var rightCount = 0;

        foreach (var line in lines)
        {
            if (line.Bounds.Right <= gutter.Left)
            {
                leftCount++;
            }
            else if (line.Bounds.X >= gutter.Right)
            {
                rightCount++;
            }
        }

        return leftCount >= 2 && rightCount >= 2;
    }

    private static IReadOnlyList<LayoutRow> BuildRows(IReadOnlyList<OcrTextBlock> lines, double rowTolerance)
    {
        var rows = new List<LayoutRow>();
        foreach (var line in lines.OrderBy(line => CenterY(line.Bounds)).ThenBy(line => line.Bounds.X))
        {
            var matchingRow = rows
                .Where(row => Math.Abs(row.CenterY - CenterY(line.Bounds)) <= rowTolerance ||
                    VerticalOverlapRatio(row.Top, row.Bottom, line.Bounds.Y, line.Bounds.Bottom) >= 0.45)
                .OrderBy(row => Math.Abs(row.CenterY - CenterY(line.Bounds)))
                .FirstOrDefault();

            if (matchingRow is null)
            {
                rows.Add(new LayoutRow(line));
                continue;
            }

            matchingRow.Add(line);
        }

        return rows;
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
        var medianFontSize = Median(orderedWords.Select(word => word.FontSize));
        var splitGap = CalculateHorizontalSplitGap(gaps, medianFontSize);
        var splitLines = new List<OcrTextBlock>();
        var currentWords = new List<OcrWord> { orderedWords[0] };

        for (var index = 1; index < orderedWords.Length; index++)
        {
            var previousWord = orderedWords[index - 1];
            var currentWord = orderedWords[index];
            var gap = currentWord.Bounds.X - previousWord.Bounds.Right;

            if (gap > splitGap && IsMeaningfulHorizontalSplit(currentWords, orderedWords, index, gap, medianFontSize))
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

    private static ColumnBand[] CreateColumnBands(
        IReadOnlyList<OcrTextBlock> lines,
        IReadOnlyList<DetectedGutter> gutters)
    {
        var words = lines
            .SelectMany(line => line.Words)
            .Where(word => word.Bounds.Width > 0 && word.Bounds.Height > 0)
            .ToArray();
        var minX = words.Length == 0
            ? lines.Min(line => (double)line.Bounds.X)
            : words.Min(word => (double)word.Bounds.X);
        var maxX = words.Length == 0
            ? lines.Max(line => (double)line.Bounds.Right)
            : words.Max(word => (double)word.Bounds.Right);
        var boundaries = new[] { minX - 1 }
            .Concat(gutters.Select(gutter => gutter.Center))
            .Concat([maxX + 1])
            .ToArray();
        var bands = new List<ColumnBand>();

        for (var index = 0; index < boundaries.Length - 1; index++)
        {
            bands.Add(new ColumnBand(boundaries[index], boundaries[index + 1]));
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

    private static IReadOnlyList<LayoutColumn> BuildColumns(
        IReadOnlyList<OcrTextBlock> lines,
        IReadOnlyList<DetectedGutter> gutters)
    {
        if (gutters.Count > 0)
        {
            return BuildColumnsFromGutters(lines, gutters);
        }

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

    private static IReadOnlyList<LayoutColumn> BuildColumnsFromGutters(
        IReadOnlyList<OcrTextBlock> lines,
        IReadOnlyList<DetectedGutter> gutters)
    {
        var bands = CreateColumnBands(lines, gutters);
        var columnsByBand = new Dictionary<int, LayoutColumn>();

        foreach (var line in lines.OrderBy(line => line.Bounds.Y).ThenBy(line => line.Bounds.X))
        {
            var bandIndex = FindBandIndex(bands, CenterX(line.Bounds));
            if (bandIndex < 0)
            {
                bandIndex = 0;
            }

            if (!columnsByBand.TryGetValue(bandIndex, out var column))
            {
                columnsByBand[bandIndex] = new LayoutColumn(line);
                continue;
            }

            column.Add(line);
        }

        return columnsByBand
            .OrderBy(item => bands[item.Key].Left)
            .Select(item => item.Value)
            .ToArray();
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
        if (IsListItemStart(currentLine.Text))
        {
            return true;
        }

        var verticalGap = currentLine.Bounds.Y - previousLine.Bounds.Bottom;
        if (verticalGap < 0)
        {
            return true;
        }

        if (LooksLikeFormulaOrEquationLine(previousLine) || LooksLikeFormulaOrEquationLine(currentLine))
        {
            return true;
        }

        if (LooksLikeDashSeparatedListItem(currentLine.Text))
        {
            return true;
        }

        if (IsLargeVerticalGap(verticalGap, medianLineHeight))
        {
            return true;
        }

        if (AreHorizontallyDetached(previousLine, currentLine, currentBlockFirstLine, medianLineHeight))
        {
            return true;
        }

        if (LooksLikeTextWrappingAroundMedia(previousLine, currentLine, currentBlockFirstLine, medianLineHeight))
        {
            return true;
        }

        if (LooksLikeSectionHeading(previousLine) && !LooksLikeSectionHeading(currentLine))
        {
            return true;
        }

        if (LooksLikeDocumentParagraphContinuation(previousLine, currentLine, currentBlockFirstLine, verticalGap, medianLineHeight))
        {
            return false;
        }

        var indentDelta = currentLine.Bounds.X - currentBlockFirstLine.Bounds.X;
        if (!IsListItemStart(currentBlockFirstLine.Text) &&
            Math.Abs(indentDelta) > Math.Max(14, medianLineHeight * IndentFactor) &&
            EndsLikeParagraph(previousLine.Text))
        {
            return true;
        }

        var mergeScore = CalculateMergeScore(previousLine, currentLine, currentBlockFirstLine, verticalGap, medianLineHeight);
        return mergeScore < MergeScoreThreshold;
    }

    private static bool IsLargeVerticalGap(int verticalGap, double medianLineHeight)
    {
        return verticalGap > Math.Max(MinimumLargeVerticalGap, medianLineHeight * LargeVerticalGapFactor);
    }

    private static bool LooksLikeDocumentParagraphContinuation(
        OcrTextBlock previousLine,
        OcrTextBlock currentLine,
        OcrTextBlock currentBlockFirstLine,
        int verticalGap,
        double medianLineHeight)
    {
        if (verticalGap < 0 || verticalGap > Math.Max(18, medianLineHeight * 1.15))
        {
            return false;
        }

        if (LooksLikeStandaloneHeading(previousLine, verticalGap, medianLineHeight) ||
            LooksLikeStandaloneHeading(currentLine, verticalGap, medianLineHeight))
        {
            return false;
        }

        if (!AreFontSizesSimilar(previousLine.FontSize, currentLine.FontSize))
        {
            return false;
        }

        var hasColumnOverlap = HorizontalOverlapRatio(previousLine.Bounds, currentLine.Bounds) >= 0.28 ||
            HorizontalOverlapRatio(currentBlockFirstLine.Bounds, currentLine.Bounds) >= 0.28;
        var leftAligned = Math.Abs(currentLine.Bounds.X - previousLine.Bounds.X) <= Math.Max(20, medianLineHeight * 1.25) ||
            Math.Abs(currentLine.Bounds.X - currentBlockFirstLine.Bounds.X) <= Math.Max(20, medianLineHeight * 1.25);
        if (!hasColumnOverlap && !leftAligned)
        {
            return false;
        }

        var previousLooksLikeProse = LooksLikeProseLine(previousLine, medianLineHeight);
        var currentLooksLikeProse = LooksLikeProseLine(currentLine, medianLineHeight);
        if (!previousLooksLikeProse || !currentLooksLikeProse)
        {
            return false;
        }

        var mergeScore = CalculateDocumentParagraphScore(previousLine, currentLine, currentBlockFirstLine, verticalGap, medianLineHeight);
        return mergeScore >= DocumentParagraphMergeScore;
    }

    private static double CalculateDocumentParagraphScore(
        OcrTextBlock previousLine,
        OcrTextBlock currentLine,
        OcrTextBlock currentBlockFirstLine,
        int verticalGap,
        double medianLineHeight)
    {
        var score = CalculateVerticalProximityScore(verticalGap, medianLineHeight);

        if (AreFontSizesSimilar(previousLine.FontSize, currentLine.FontSize))
        {
            score += 0.18;
        }

        if (HorizontalOverlapRatio(previousLine.Bounds, currentLine.Bounds) >= 0.55)
        {
            score += 0.18;
        }

        if (Math.Abs(currentLine.Bounds.X - currentBlockFirstLine.Bounds.X) <= Math.Max(22, medianLineHeight * 1.35) ||
            Math.Abs(currentLine.Bounds.X - previousLine.Bounds.X) <= Math.Max(22, medianLineHeight * 1.35))
        {
            score += 0.18;
        }

        if (!EndsLikeHardSentenceBoundary(previousLine.Text) ||
            EndsWithSoftWrapCue(previousLine.Text) ||
            StartsLikeParagraphContinuation(currentLine.Text))
        {
            score += 0.20;
        }

        if (BothLookLikeParagraphLines(previousLine, currentLine, medianLineHeight))
        {
            score += 0.10;
        }

        if (EndsLikeHardSentenceBoundary(previousLine.Text) && StartsLikeNewSentence(currentLine.Text))
        {
            score -= 0.12;
        }

        return Math.Clamp(score, 0, 1);
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

    private static bool IsListItemStart(string text)
    {
        return IsBulletOrNumberedItem(text) || EllipsisListItemRegex.IsMatch(text);
    }

    private static bool LooksLikeDashSeparatedListItem(string text)
    {
        return DashSeparatedListItemRegex.IsMatch(text);
    }

    private static bool LooksLikeSectionHeading(OcrTextBlock line)
    {
        return SectionHeadingRegex.IsMatch(line.Text) ||
            (CountWords(line.Text) <= 7 && !EndsLikeParagraph(line.Text) && HasTitleCaseSignal(line.Text));
    }

    private static bool LooksLikeFormulaOrEquationLine(OcrTextBlock line)
    {
        var text = line.Text.Trim();
        if (text.Length == 0)
        {
            return true;
        }

        var letters = text.Count(char.IsLetter);
        var digits = text.Count(char.IsDigit);
        var mathSymbols = text.Count(character => character is '=' or '+' or '-' or '/' or '\\' or '^' or '_' or '∑' or 'Σ' or '∆' or 'Δ' or 'δ' or '≈' or '≤' or '≥' or '∥' or '‖');
        var bracketSymbols = text.Count(character => character is '(' or ')' or '[' or ']' or '{' or '}');
        var nonSpace = text.Count(character => !char.IsWhiteSpace(character));
        if (nonSpace == 0)
        {
            return true;
        }

        var wordCount = CountWords(text);
        var symbolRatio = (mathSymbols + bracketSymbols) / (double)nonSpace;
        var hasEquationCue = text.Contains('=') || text.Contains('≈') || text.Contains('∑') || text.Contains('Σ') || text.Contains("||", StringComparison.Ordinal);
        var looksLikeEquationNumber = wordCount <= 2 && digits > 0 && bracketSymbols >= 2;

        return looksLikeEquationNumber ||
            (hasEquationCue && symbolRatio >= 0.14 && letters <= Math.Max(10, digits + mathSymbols + bracketSymbols)) ||
            (wordCount <= 4 && symbolRatio >= 0.32 && mathSymbols > 0);
    }

    private static bool LooksLikeProseLine(OcrTextBlock line, double medianLineHeight)
    {
        var text = line.Text.Trim();
        if (text.Length == 0 || LooksLikeFormulaOrEquationLine(line))
        {
            return false;
        }

        var wordCount = CountWords(text);
        if (wordCount < 4)
        {
            return false;
        }

        var letters = text.Count(char.IsLetter);
        var nonSpace = text.Count(character => !char.IsWhiteSpace(character));
        if (nonSpace == 0 || letters / (double)nonSpace < 0.48)
        {
            return false;
        }

        return line.Bounds.Width >= Math.Max(150, medianLineHeight * 7);
    }

    private static bool StartsLikeNewSentence(string text)
    {
        var trimmed = text.TrimStart();
        return trimmed.Length > 0 && char.IsUpper(trimmed[0]);
    }

    private static bool HasTitleCaseSignal(string text)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0)
        {
            return false;
        }

        var titleWords = words.Count(word => word.Length > 0 && char.IsUpper(word[0]));
        return titleWords >= Math.Max(1, words.Length - 1);
    }

    private static bool LooksLikeTitleDescriptionPair(
        OcrTextBlock previousLine,
        OcrTextBlock currentLine,
        int verticalGap,
        double medianLineHeight)
    {
        if (verticalGap < 0 || verticalGap > Math.Max(10, medianLineHeight * 0.65))
        {
            return false;
        }

        if (LooksLikeDashSeparatedListItem(previousLine.Text) || LooksLikeDashSeparatedListItem(currentLine.Text))
        {
            return false;
        }

        var previousWordCount = CountWords(previousLine.Text);
        var currentWordCount = CountWords(currentLine.Text);
        var aligned = Math.Abs(previousLine.Bounds.X - currentLine.Bounds.X) <= Math.Max(12, medianLineHeight * 0.8);
        var previousLooksLikeTitle = previousWordCount <= 3 &&
            previousLine.Bounds.Width <= Math.Max(180, medianLineHeight * 12) &&
            !EndsLikeParagraph(previousLine.Text);

        return aligned && previousLooksLikeTitle && currentWordCount <= 8;
    }

    private static double CalculateMergeScore(
        OcrTextBlock previousLine,
        OcrTextBlock currentLine,
        OcrTextBlock currentBlockFirstLine,
        int verticalGap,
        double medianLineHeight)
    {
        if (verticalGap < 0)
        {
            return 0;
        }

        var score = 0.0;
        score += CalculateVerticalProximityScore(verticalGap, medianLineHeight);

        if (AreFontSizesSimilar(previousLine.FontSize, currentLine.FontSize))
        {
            score += 0.25;
        }

        var horizontalOverlap = HorizontalOverlapRatio(previousLine.Bounds, currentLine.Bounds);
        if (horizontalOverlap >= 0.55)
        {
            score += 0.18;
        }

        var alignedToBlock = Math.Abs(currentLine.Bounds.X - currentBlockFirstLine.Bounds.X) <= Math.Max(14, medianLineHeight * 0.9);
        var alignedToPrevious = Math.Abs(currentLine.Bounds.X - previousLine.Bounds.X) <= Math.Max(14, medianLineHeight * 0.9);
        if (alignedToBlock || alignedToPrevious)
        {
            score += 0.20;
        }

        if (AreWidthsSimilar(previousLine.Bounds.Width, currentLine.Bounds.Width) ||
            BothLookLikeParagraphLines(previousLine, currentLine, medianLineHeight))
        {
            score += 0.06;
        }

        if (!EndsLikeHardSentenceBoundary(previousLine.Text))
        {
            score += 0.18;
        }

        if (EndsWithSoftWrapCue(previousLine.Text))
        {
            score += 0.04;
        }

        if (StartsLikeParagraphContinuation(currentLine.Text))
        {
            score += 0.08;
        }

        if (LooksLikeTitleDescriptionPair(previousLine, currentLine, verticalGap, medianLineHeight))
        {
            score = Math.Max(score, 0.94);
        }

        if (LooksLikeStandaloneHeading(currentLine, verticalGap, medianLineHeight))
        {
            score -= 0.30;
        }

        if (LooksLikeDashSeparatedListItem(previousLine.Text) || LooksLikeDashSeparatedListItem(currentLine.Text))
        {
            score -= 0.35;
        }

        return Math.Clamp(score, 0, 1);
    }

    private static double CalculateVerticalProximityScore(int verticalGap, double medianLineHeight)
    {
        if (verticalGap <= Math.Max(4, medianLineHeight * 0.25))
        {
            return 0.34;
        }

        if (verticalGap <= Math.Max(8, medianLineHeight * 0.55))
        {
            return 0.28;
        }

        if (verticalGap <= Math.Max(14, medianLineHeight * 0.95))
        {
            return 0.18;
        }

        if (verticalGap <= Math.Max(18, medianLineHeight * 1.25))
        {
            return 0.08;
        }

        return 0;
    }

    private static double CalculateHorizontalSplitGap(IReadOnlyList<double> gaps, double medianFontSize)
    {
        var normalWordGaps = gaps
            .Where(gap => gap <= Math.Max(24, medianFontSize * 2.4))
            .ToArray();
        var typicalWordGap = Median(normalWordGaps);

        return typicalWordGap > 0
            ? Math.Max(MinimumHorizontalSplitGap, Math.Max(medianFontSize * 1.55, typicalWordGap * 2.8))
            : Math.Max(MinimumHorizontalSplitGap, medianFontSize * 1.55);
    }

    private static bool IsMeaningfulHorizontalSplit(
        IReadOnlyList<OcrWord> currentWords,
        IReadOnlyList<OcrWord> orderedWords,
        int splitIndex,
        int gap,
        double medianFontSize)
    {
        var leftWordCount = currentWords.Count;
        var rightWordCount = orderedWords.Count - splitIndex;
        if (leftWordCount == 0 || rightWordCount == 0)
        {
            return false;
        }

        if (Math.Min(leftWordCount, rightWordCount) >= 3)
        {
            return true;
        }

        if (Math.Max(leftWordCount, rightWordCount) >= 4)
        {
            return gap >= Math.Max(42, medianFontSize * 2.6);
        }

        return gap >= Math.Max(72, medianFontSize * 4.5);
    }

    private static bool AreHorizontallyDetached(
        OcrTextBlock previousLine,
        OcrTextBlock currentLine,
        OcrTextBlock currentBlockFirstLine,
        double medianLineHeight)
    {
        if (HorizontalOverlapRatio(previousLine.Bounds, currentLine.Bounds) >= 0.12 ||
            HorizontalOverlapRatio(currentBlockFirstLine.Bounds, currentLine.Bounds) >= 0.12)
        {
            return false;
        }

        var alignedToPrevious = Math.Abs(currentLine.Bounds.X - previousLine.Bounds.X) <= Math.Max(14, medianLineHeight * 0.9);
        var alignedToBlock = Math.Abs(currentLine.Bounds.X - currentBlockFirstLine.Bounds.X) <= Math.Max(14, medianLineHeight * 0.9);
        if (alignedToPrevious || alignedToBlock)
        {
            return false;
        }

        var gapFromPrevious = DistanceBetween(previousLine.Bounds.X, previousLine.Bounds.Right, currentLine.Bounds.X, currentLine.Bounds.Right);
        var gapFromBlock = DistanceBetween(currentBlockFirstLine.Bounds.X, currentBlockFirstLine.Bounds.Right, currentLine.Bounds.X, currentLine.Bounds.Right);
        var minimumDetachedGap = Math.Max(MinimumHorizontalSplitGap, medianLineHeight * 1.4);

        return Math.Min(gapFromPrevious, gapFromBlock) >= minimumDetachedGap;
    }

    private static bool LooksLikeTextWrappingAroundMedia(
        OcrTextBlock previousLine,
        OcrTextBlock currentLine,
        OcrTextBlock currentBlockFirstLine,
        double medianLineHeight)
    {
        var outdent = currentBlockFirstLine.Bounds.X - currentLine.Bounds.X;
        if (outdent <= Math.Max(28, medianLineHeight * 2.0))
        {
            return false;
        }

        var currentReachesFurtherLeft = currentLine.Bounds.X < previousLine.Bounds.X - Math.Max(18, medianLineHeight * 1.2);
        var currentIsMuchWider = currentLine.Bounds.Width >= previousLine.Bounds.Width * 1.18;

        return currentReachesFurtherLeft && currentIsMuchWider;
    }

    private static bool AreFontSizesSimilar(double previousFontSize, double currentFontSize)
    {
        var larger = Math.Max(previousFontSize, currentFontSize);
        if (larger <= 0)
        {
            return false;
        }

        var smaller = Math.Min(previousFontSize, currentFontSize);
        return smaller / larger >= 0.86;
    }

    private static bool AreWidthsSimilar(int previousWidth, int currentWidth)
    {
        var larger = Math.Max(previousWidth, currentWidth);
        if (larger <= 0)
        {
            return false;
        }

        var smaller = Math.Min(previousWidth, currentWidth);
        return smaller / (double)larger >= 0.45;
    }

    private static bool BothLookLikeParagraphLines(
        OcrTextBlock previousLine,
        OcrTextBlock currentLine,
        double medianLineHeight)
    {
        return previousLine.Bounds.Width >= Math.Max(220, medianLineHeight * 14) &&
            currentLine.Bounds.Width >= Math.Max(180, medianLineHeight * 11);
    }

    private static bool EndsWithSoftWrapCue(string text)
    {
        var trimmed = text.TrimEnd();
        return trimmed.EndsWith(',') || trimmed.EndsWith(';') || trimmed.EndsWith('-') ||
            trimmed.EndsWith('(') || trimmed.EndsWith('/');
    }

    private static bool EndsLikeHardSentenceBoundary(string text)
    {
        var trimmed = text.TrimEnd();
        return trimmed.EndsWith('.') || trimmed.EndsWith('?') || trimmed.EndsWith('!') ||
            trimmed.EndsWith(':');
    }

    private static bool StartsLikeParagraphContinuation(string text)
    {
        var trimmed = text.TrimStart();
        if (trimmed.Length == 0)
        {
            return false;
        }

        return char.IsLower(trimmed[0]) ||
            trimmed.StartsWith("and ", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("or ", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("but ", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("with ", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("where ", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("which ", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("that ", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("other ", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeStandaloneHeading(
        OcrTextBlock line,
        int verticalGap,
        double medianLineHeight)
    {
        if (verticalGap <= Math.Max(6, medianLineHeight * 0.35))
        {
            return false;
        }

        return CountWords(line.Text) <= 5 &&
            line.Bounds.Width <= Math.Max(260, medianLineHeight * 14) &&
            !EndsLikeParagraph(line.Text);
    }

    private static int CountWords(string text)
    {
        return text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;
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

    private static double CenterY(BoundingBox bounds)
    {
        return bounds.Y + bounds.Height / 2.0;
    }

    private static double VerticalOverlapRatio(int topA, int bottomA, int topB, int bottomB)
    {
        var overlap = Math.Min(bottomA, bottomB) - Math.Max(topA, topB);
        if (overlap <= 0)
        {
            return 0;
        }

        var smallerHeight = Math.Min(bottomA - topA, bottomB - topB);
        return smallerHeight <= 0
            ? 0
            : overlap / (double)smallerHeight;
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

    private sealed class LayoutRow
    {
        private readonly List<OcrTextBlock> _lines = [];

        public LayoutRow(OcrTextBlock firstLine)
        {
            Add(firstLine);
        }

        public int Top { get; private set; }

        public int Bottom { get; private set; }

        public double CenterY => Top + (Bottom - Top) / 2.0;

        public IReadOnlyList<OcrTextBlock> Lines => _lines;

        public void Add(OcrTextBlock line)
        {
            if (_lines.Count == 0)
            {
                Top = line.Bounds.Y;
                Bottom = line.Bounds.Bottom;
            }
            else
            {
                Top = Math.Min(Top, line.Bounds.Y);
                Bottom = Math.Max(Bottom, line.Bounds.Bottom);
            }

            _lines.Add(line);
        }
    }

    private readonly record struct DetectedGutter(double Left, double Right, int Votes)
    {
        public double Center => (Left + Right) / 2.0;
    }

    private sealed class DetectedGutterCluster
    {
        private readonly List<DetectedGutter> _gutters = [];

        public DetectedGutterCluster(DetectedGutter first)
        {
            Add(first);
        }

        public double Center => _gutters.Average(gutter => gutter.Center);

        public int Votes => _gutters.Sum(gutter => gutter.Votes);

        public void Add(DetectedGutter gutter)
        {
            _gutters.Add(gutter);
        }

        public DetectedGutter ToGutter()
        {
            return new DetectedGutter(
                _gutters.Average(gutter => gutter.Left),
                _gutters.Average(gutter => gutter.Right),
                Votes);
        }
    }
}
