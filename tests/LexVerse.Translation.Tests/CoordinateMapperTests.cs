using LexVerse.Core.Geometry;
using Xunit;

namespace LexVerse.Translation.Tests;

public sealed class CoordinateMapperTests
{
    [Fact]
    public void FrameToScreen_WhenFullscreenWithoutOffset_ReturnsSameRect()
    {
        var mapper = new CoordinateMapper(new FrameGeometry(
            new PixelSize(1920, 1080),
            new ScreenRect(0, 0, 1920, 1080),
            CoordinateSpace.ScreenPhysical,
            1,
            1,
            0));

        var screen = mapper.FrameToScreen(new FrameRect(100, 200, 300, 40));

        Assert.Equal(new ScreenRect(100, 200, 300, 40), screen);
    }

    [Fact]
    public void FrameToScreen_WhenRegionHasOffset_AddsRegionOrigin()
    {
        var mapper = new CoordinateMapper(new FrameGeometry(
            new PixelSize(800, 300),
            new ScreenRect(500, 700, 800, 300),
            CoordinateSpace.FrameLocal,
            1,
            1,
            0));

        var screen = mapper.FrameToScreen(new FrameRect(20, 10, 100, 30));

        Assert.Equal(new ScreenRect(520, 710, 100, 30), screen);
    }

    [Fact]
    public void FrameToScreen_WhenWindowHasOffset_AddsWindowOrigin()
    {
        var mapper = new CoordinateMapper(new FrameGeometry(
            new PixelSize(1024, 768),
            new ScreenRect(120, 80, 1024, 768),
            CoordinateSpace.FrameLocal,
            1,
            1,
            3));

        var screen = mapper.FrameToScreen(new FrameRect(200, 100, 320, 60));

        Assert.Equal(new ScreenRect(320, 180, 320, 60), screen);
    }

    [Fact]
    public void FrameToScreen_WhenFrameIsScaled_UsesSourceToFrameRatio()
    {
        var mapper = new CoordinateMapper(new FrameGeometry(
            new PixelSize(960, 540),
            new ScreenRect(100, 50, 1920, 1080),
            CoordinateSpace.FrameLocal,
            1,
            1,
            1));

        var screen = mapper.FrameToScreen(new FrameRect(10, 20, 30, 40));

        Assert.Equal(new ScreenRect(120, 90, 60, 80), screen);
    }

    [Fact]
    public void ScreenToFrame_WhenFrameIsScaled_UsesInverseRatio()
    {
        var mapper = new CoordinateMapper(new FrameGeometry(
            new PixelSize(960, 540),
            new ScreenRect(100, 50, 1920, 1080),
            CoordinateSpace.FrameLocal,
            1,
            1,
            1));

        var frame = mapper.ScreenToFrame(new ScreenRect(120, 90, 60, 80));

        Assert.Equal(new FrameRect(10, 20, 30, 40), frame);
    }

    [Fact]
    public void Constructor_WhenFrameSizeIsInvalid_Throws()
    {
        var geometry = new FrameGeometry(
            new PixelSize(0, 540),
            new ScreenRect(0, 0, 1920, 1080),
            CoordinateSpace.FrameLocal,
            1,
            1,
            0);

        Assert.Throws<ArgumentOutOfRangeException>(() => new CoordinateMapper(geometry));
    }
}
