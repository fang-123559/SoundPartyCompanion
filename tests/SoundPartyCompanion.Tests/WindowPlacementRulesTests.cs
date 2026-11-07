using SoundPartyCompanion.Core.Settings;
using Xunit;

namespace SoundPartyCompanion.Tests;

public sealed class WindowPlacementRulesTests
{
    private static readonly WindowWorkArea WorkArea = new(100, 50, 1200, 800);

    [Theory]
    [InlineData(0, 0, 1280, 800, 900, 100, 350, 125, 416, 476, 14, 848, 86)]
    [InlineData(0, 0, 1280, 800, 24, 100, 350, 125, 416, 476, 14, 0, 86)]
    [InlineData(0, 0, 800, 1000, 206, 20, 388, 250, 416, 476, 14, 192, 6)]
    [InlineData(0, 0, 800, 1000, 206, 720, 388, 250, 416, 476, 14, 192, 524)]
    [InlineData(0, 0, 800, 600, 206, 100, 388, 250, 416, 476, 14, 192, 86)]
    [InlineData(-1920, 0, 1920, 1040, -400, 50, 350, 125, 416, 476, 14, -452, 36)]
    [InlineData(0, 0, 2560, 1440, 1950, 100, 500, 180, 624, 714, 21, 1847, 79)]
    [InlineData(0, 0, 1280, 800, 900, 700, 350, 66, 416, 476, 14, 848, 324)]
    public void InPlaceSettingsAlignWithWidgetAndStayInsideWorkArea(
        double workLeft, double workTop, double workWidth, double workHeight,
        double ownerLeft, double ownerTop, double ownerWidth, double ownerHeight,
        double width, double height, double inset, double expectedLeft, double expectedTop)
    {
        var result = WindowPlacementRules.ReplaceAtAnchor(
            new WindowWorkArea(workLeft, workTop, workWidth, workHeight), width, height,
            new WindowWorkArea(ownerLeft, ownerTop, ownerWidth, ownerHeight), inset);

        Assert.Equal(expectedLeft, result.Left);
        Assert.Equal(expectedTop, result.Top);
        Assert.InRange(result.Left, workLeft, workLeft + workWidth - width);
        Assert.InRange(result.Top, workTop, workTop + workHeight - height);
    }

    [Fact]
    public void RestoreDefaultsToTopRightWithMargin()
    {
        var result = WindowPlacementRules.Restore(WorkArea, 350, 125, placement: null);

        Assert.Equal(926, result.Left);
        Assert.Equal(74, result.Top);
    }

    [Fact]
    public void NormalizeAndRestoreRoundTripPosition()
    {
        var original = new WindowPosition(525, 360);
        var normalized = WindowPlacementRules.Normalize(
            "DISPLAY2",
            WorkArea,
            350,
            125,
            original);
        var restored = WindowPlacementRules.Restore(
            WorkArea,
            350,
            125,
            normalized);

        Assert.Equal(original.Left, restored.Left, precision: 6);
        Assert.Equal(original.Top, restored.Top, precision: 6);
    }

    [Fact]
    public void ClampAndSnapKeepsWindowVisibleAndSnapsNearEdges()
    {
        var result = WindowPlacementRules.ClampAndSnap(
            WorkArea,
            350,
            125,
            new WindowPosition(940, 42));

        Assert.Equal(950, result.Left);
        Assert.Equal(50, result.Top);
    }
}
