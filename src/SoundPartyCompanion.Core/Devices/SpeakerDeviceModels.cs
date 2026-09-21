namespace SoundPartyCompanion.Core.Devices;

public enum SpeakerConnectionState
{
    BluetoothUnavailable,
    PairedOffline,
    Connected,
    ConnectedAsOutput
}

public sealed record SpeakerDevice(
    string DeviceKey,
    string DisplayName,
    bool IsPaired,
    bool IsConnected,
    DateTimeOffset ObservedAt);

public sealed record SpeakerDiscoverySnapshot(
    bool IsBluetoothAvailable,
    IReadOnlyList<SpeakerDevice> Devices,
    DateTimeOffset ObservedAt);

public sealed record SpeakerSelection(string DeviceKey, string DisplayName);

public sealed record SpeakerConnectionSnapshot(
    SpeakerConnectionState State,
    string? DeviceKey,
    string? DisplayName,
    DateTimeOffset ObservedAt)
{
    public SpeakerBatterySnapshot Battery { get; init; } = SpeakerBatterySnapshot.Unavailable;

    public bool IsConnected =>
        State is SpeakerConnectionState.Connected or SpeakerConnectionState.ConnectedAsOutput;

    public bool IsCurrentOutput => State is SpeakerConnectionState.ConnectedAsOutput;
}

public sealed class SpeakerConnectionChangedEventArgs(SpeakerConnectionSnapshot snapshot) : EventArgs
{
    public SpeakerConnectionSnapshot Snapshot { get; } = snapshot;
}
