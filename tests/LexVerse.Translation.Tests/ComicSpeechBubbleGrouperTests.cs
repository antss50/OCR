using LexVerse.Core.Ocr;
using Xunit;

namespace LexVerse.Translation.Tests;

public sealed class ComicSpeechBubbleGrouperTests
{
    private readonly ComicSpeechBubbleGrouper _grouper = new();

    [Fact]
    public void GroupLines_WhenLinesAreStackedAndCentered_MergesSpeechBubbleIntoOneBlock()
    {
        var blocks = _grouper.GroupLines([
            Line("WHAT DO YOU", 106, 50, 180, 24),
            Line("THINK YOU'RE", 92, 78, 208, 24),
            Line("DOING?!", 130, 106, 132, 24)
        ]);

        var block = Assert.Single(blocks);
        Assert.Equal("WHAT DO YOU THINK YOU'RE DOING?!", block.Text);
        Assert.Equal(new BoundingBox(92, 50, 208, 80), block.Bounds);
    }

    [Fact]
    public void GroupLines_WhenSpeechBubblesAreSideBySide_KeepsBubblesSeparate()
    {
        var blocks = _grouper.GroupLines([
            Line("ARE YOU", 40, 50, 120, 24),
            Line("LISTENING?", 48, 78, 104, 24),
            Line("I AM", 300, 52, 90, 24),
            Line("RIGHT HERE.", 286, 80, 128, 24)
        ]);

        Assert.Collection(
            blocks,
            block => Assert.Equal("ARE YOU LISTENING?", block.Text),
            block => Assert.Equal("I AM RIGHT HERE.", block.Text));
    }

    [Fact]
    public void GroupLines_WhenBubblesAreVerticallyDistant_KeepsBubblesSeparate()
    {
        var blocks = _grouper.GroupLines([
            Line("SAMIN, PLEASE...", 90, 40, 220, 24),
            Line("I'M NOT HERE", 112, 68, 176, 24),
            Line("WHAT ARE YOU", 108, 170, 184, 24),
            Line("DOING?!", 140, 198, 120, 24)
        ]);

        Assert.Collection(
            blocks,
            block => Assert.Equal("SAMIN, PLEASE... I'M NOT HERE", block.Text),
            block => Assert.Equal("WHAT ARE YOU DOING?!", block.Text));
    }

    private static OcrTextBlock Line(string text, int x, int y, int width, int height)
    {
        var bounds = new BoundingBox(x, y, width, height);
        var word = new OcrWord(text, bounds, height);

        return new OcrTextBlock(text, bounds, [word], height);
    }
}
