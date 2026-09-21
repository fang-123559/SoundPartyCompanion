using SoundPartyCompanion.Core.Devices;
using SoundPartyCompanion.Core.Settings;
using Xunit;

namespace SoundPartyCompanion.Tests;

public sealed class WidgetStateRulesTests
{
    [Theory]
    [InlineData(SpeakerConnectionState.BluetoothUnavailable)]
    [InlineData(SpeakerConnectionState.PairedOffline)]
    public void OfflineConnectionsCollapseWhenEnabled(SpeakerConnectionState state)
    {
        Assert.Equal(
            WidgetDisplayMode.Offline,
            WidgetStateRules.Resolve(state, autoCollapseOffline: true, expandedRequested: true));
    }

    [Fact]
    public void ConnectedWidgetExpandsOnlyWhenRequested()
    {
        Assert.Equal(
            WidgetDisplayMode.Compact,
            WidgetStateRules.Resolve(
                SpeakerConnectionState.ConnectedAsOutput,
                autoCollapseOffline: true,
                expandedRequested: false));
        Assert.Equal(
            WidgetDisplayMode.Expanded,
            WidgetStateRules.Resolve(
                SpeakerConnectionState.ConnectedAsOutput,
                autoCollapseOffline: true,
                expandedRequested: true));
    }

    [Fact]
    public void OfflineConnectionCanRemainCompactWhenAutoCollapseIsDisabled()
    {
        Assert.Equal(
            WidgetDisplayMode.Compact,
            WidgetStateRules.Resolve(
                SpeakerConnectionState.PairedOffline,
                autoCollapseOffline: false,
                expandedRequested: false));
    }
}
