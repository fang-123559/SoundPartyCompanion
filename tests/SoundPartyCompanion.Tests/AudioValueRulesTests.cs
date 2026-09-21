using SoundPartyCompanion.Core.Audio;
using Xunit;

namespace SoundPartyCompanion.Tests;

public sealed class AudioValueRulesTests
{
    [Theory]
    [InlineData(-25, 0)]
    [InlineData(0, 0)]
    [InlineData(42.5, 42.5)]
    [InlineData(100, 100)]
    [InlineData(125, 100)]
    public void ClampVolumePercentKeepsValuesWithinRange(double input, double expected)
    {
        Assert.Equal(expected, AudioValueRules.ClampVolumePercent(input));
    }

    [Fact]
    public void ClampVolumePercentHandlesNonFiniteValues()
    {
        Assert.Equal(0, AudioValueRules.ClampVolumePercent(double.NaN));
        Assert.Equal(0, AudioValueRules.ClampVolumePercent(double.NegativeInfinity));
        Assert.Equal(100, AudioValueRules.ClampVolumePercent(double.PositiveInfinity));
    }
}
