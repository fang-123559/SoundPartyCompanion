using SoundPartyCompanion.Core.Devices;

namespace SoundPartyCompanion.Core.Settings;

public static class WidgetStateRules
{
    public static WidgetDisplayMode Resolve(
        SpeakerConnectionState connectionState,
        bool autoCollapseOffline,
        bool expandedRequested,
        bool offlineDetailsRequested = false)
    {
        var isConnected = connectionState is
            SpeakerConnectionState.Connected or
            SpeakerConnectionState.ConnectedAsOutput;

        if (!isConnected && autoCollapseOffline)
        {
            return offlineDetailsRequested
                ? WidgetDisplayMode.OfflineDetails
                : WidgetDisplayMode.Offline;
        }

        return isConnected && expandedRequested
            ? WidgetDisplayMode.Expanded
            : WidgetDisplayMode.Compact;
    }
}
