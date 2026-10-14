using SoundPartyCompanion.Core.Settings;
using Xunit;

namespace SoundPartyCompanion.Tests;

public sealed class AppSettingsRulesTests
{
    [Fact]
    public void NormalizeUsesDefaultsForMissingSettings()
    {
        Assert.Equal(AppSettings.Default, AppSettingsRules.Normalize(null));
    }

    [Theory]
    [InlineData(0.2, 0.65)]
    [InlineData(0.65, 0.65)]
    [InlineData(0.82, 0.82)]
    [InlineData(1.4, 1)]
    public void NormalizeOpacityClampsToSupportedRange(double input, double expected)
    {
        Assert.Equal(expected, AppSettingsRules.NormalizeOpacity(input));
    }

    [Fact]
    public void NormalizePlacementTrimsMonitorAndClampsCoordinates()
    {
        var result = AppSettingsRules.NormalizePlacement(
            new NormalizedWindowPlacement("  DISPLAY2  ", -0.4, 1.6));

        Assert.NotNull(result);
        Assert.Equal("DISPLAY2", result.MonitorId);
        Assert.Equal(0, result.X);
        Assert.Equal(1, result.Y);
    }

    [Fact]
    public void NormalizeRepairsLowBatteryReminderState()
    {
        var result = AppSettingsRules.Normalize(AppSettings.Default with
        {
            LowBatteryLastObservedPercent = 101,
            LowBatteryNotifiedThresholds = 15
        });

        Assert.Null(result.LowBatteryLastObservedPercent);
        Assert.Equal(3, result.LowBatteryNotifiedThresholds);
    }
}
