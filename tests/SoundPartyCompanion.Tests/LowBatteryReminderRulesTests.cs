using SoundPartyCompanion.Core.Devices;
using Xunit;

namespace SoundPartyCompanion.Tests;

public sealed class LowBatteryReminderRulesTests
{
    [Fact]
    public void FirstLowObservationNotifiesOnce()
    {
        var result = LowBatteryReminderRules.Evaluate(
            LowBatteryReminderState.Empty,
            currentPercent: 18,
            enabled: true);
        var duplicate = LowBatteryReminderRules.Evaluate(
            result.State,
            currentPercent: 17,
            enabled: true);

        Assert.Equal(20, result.Threshold);
        Assert.Null(duplicate.Threshold);
        Assert.Equal(18, result.State.LastObservedPercent);
    }

    [Fact]
    public void FirstCriticalObservationMarksBothThresholds()
    {
        var critical = LowBatteryReminderRules.Evaluate(
            LowBatteryReminderState.Empty,
            currentPercent: 10,
            enabled: true);
        var laterWarningBandReading = LowBatteryReminderRules.Evaluate(
            critical.State,
            currentPercent: 16,
            enabled: true);

        Assert.Equal(10, critical.Threshold);
        Assert.Equal(0b11, critical.State.NotifiedThresholds);
        Assert.Null(laterWarningBandReading.Threshold);
    }

    [Fact]
    public void DownwardCrossingNotifiesEachThresholdOnce()
    {
        var state = new LowBatteryReminderState(26, 0);

        var warning = LowBatteryReminderRules.Evaluate(state, 20, enabled: true);
        var duplicateWarning = LowBatteryReminderRules.Evaluate(warning.State, 18, enabled: true);
        var critical = LowBatteryReminderRules.Evaluate(duplicateWarning.State, 10, enabled: true);
        var duplicateCritical = LowBatteryReminderRules.Evaluate(critical.State, 8, enabled: true);

        Assert.Equal(20, warning.Threshold);
        Assert.Null(duplicateWarning.Threshold);
        Assert.Equal(10, critical.Threshold);
        Assert.Null(duplicateCritical.Threshold);
    }

    [Fact]
    public void DisabledReminderTracksBatteryWithoutNotifying()
    {
        var result = LowBatteryReminderRules.Evaluate(
            new LowBatteryReminderState(24, 0),
            currentPercent: 19,
            enabled: false);

        Assert.Null(result.Threshold);
        Assert.Equal(19, result.State.LastObservedPercent);
    }

    [Fact]
    public void HigherBatteryReadingRearmsNextDischargeCycle()
    {
        var warning = LowBatteryReminderRules.Evaluate(
            new LowBatteryReminderState(22, 0),
            currentPercent: 20,
            enabled: true);
        var rearmed = LowBatteryReminderRules.Evaluate(
            warning.State,
            currentPercent: 30,
            enabled: true);
        var repeated = LowBatteryReminderRules.Evaluate(
            rearmed.State,
            currentPercent: 20,
            enabled: true);

        Assert.Equal(20, repeated.Threshold);
    }

    [Fact]
    public void InvalidReadingDoesNotChangeState()
    {
        var state = new LowBatteryReminderState(30, 0);

        var result = LowBatteryReminderRules.Evaluate(state, 101, enabled: true);

        Assert.Equal(state, result.State);
        Assert.Null(result.Threshold);
    }
}
