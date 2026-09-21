using SoundPartyCompanion.Core.Devices;
using SoundPartyCompanion.Core.Settings;
using Xunit;

namespace SoundPartyCompanion.Tests;

public sealed class WidgetInteractionRulesTests
{
    [Fact]
    public void ConnectedCardCanRoundTripBetweenCompactAndExpanded()
    {
        var state = SpeakerConnectionState.ConnectedAsOutput;

        Assert.Equal(
            WidgetDisplayMode.Compact,
            WidgetStateRules.Resolve(state, autoCollapseOffline: true, expandedRequested: false));
        Assert.Equal(
            WidgetDisplayMode.Expanded,
            WidgetStateRules.Resolve(state, autoCollapseOffline: true, expandedRequested: true));
        Assert.Equal(
            WidgetDisplayMode.Compact,
            WidgetStateRules.Resolve(state, autoCollapseOffline: true, expandedRequested: false));
    }

    [Fact]
    public void DisconnectOverridesExpandedRequestAndReconnectCanReturnCompact()
    {
        Assert.Equal(
            WidgetDisplayMode.Offline,
            WidgetStateRules.Resolve(
                SpeakerConnectionState.PairedOffline,
                autoCollapseOffline: true,
                expandedRequested: true));
        Assert.Equal(
            WidgetDisplayMode.Compact,
            WidgetStateRules.Resolve(
                SpeakerConnectionState.Connected,
                autoCollapseOffline: true,
                expandedRequested: false));
    }

    [Fact]
    public void OfflineActivationOpensLocalDetailsWithoutChangingConnectionState()
    {
        Assert.Equal(
            WidgetDisplayMode.OfflineDetails,
            WidgetStateRules.Resolve(
                SpeakerConnectionState.PairedOffline,
                autoCollapseOffline: true,
                expandedRequested: false,
                offlineDetailsRequested: true));

        Assert.Equal(
            WidgetDisplayMode.Offline,
            WidgetStateRules.Resolve(
                SpeakerConnectionState.PairedOffline,
                autoCollapseOffline: true,
                expandedRequested: false,
                offlineDetailsRequested: false));
    }

    [Fact]
    public void FullInteractionLoopHasOneDeterministicModeAtEveryStep()
    {
        var sequence = new[]
        {
            WidgetStateRules.Resolve(SpeakerConnectionState.PairedOffline, true, false),
            WidgetStateRules.Resolve(SpeakerConnectionState.Connected, true, false),
            WidgetStateRules.Resolve(SpeakerConnectionState.Connected, true, true),
            WidgetStateRules.Resolve(SpeakerConnectionState.Connected, true, false),
            WidgetStateRules.Resolve(SpeakerConnectionState.PairedOffline, true, false),
            WidgetStateRules.Resolve(SpeakerConnectionState.ConnectedAsOutput, true, false)
        };

        Assert.Equal(
            [
                WidgetDisplayMode.Offline,
                WidgetDisplayMode.Compact,
                WidgetDisplayMode.Expanded,
                WidgetDisplayMode.Compact,
                WidgetDisplayMode.Offline,
                WidgetDisplayMode.Compact
            ],
            sequence);
    }
}
