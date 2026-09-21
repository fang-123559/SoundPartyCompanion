using SoundPartyCompanion.Core.Settings;
using Xunit;

namespace SoundPartyCompanion.Tests;

public sealed class WindowPlacementRulesTests
{
    private static readonly WindowWorkArea WorkArea = new(100, 50, 1200, 800);

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
