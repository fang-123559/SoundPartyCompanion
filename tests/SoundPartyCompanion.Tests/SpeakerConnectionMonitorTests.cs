using SoundPartyCompanion.Core.Devices;
using Xunit;

namespace SoundPartyCompanion.Tests;

public sealed class SpeakerConnectionMonitorTests
{
    private static readonly SpeakerSelection Selection = new(
        "bt-11111111111111111111111111111111",
        "Xiaomi Sound Party");

    [Fact]
    public async Task StartMapsUnavailableBluetooth()
    {
        var discovery = new FakeDiscovery(Snapshot(available: false));
        var output = new FakeOutputProbe(isCurrentOutput: false);
        await using var monitor = CreateMonitor(discovery, output);

        await monitor.StartAsync(Selection);

        Assert.Equal(SpeakerConnectionState.BluetoothUnavailable, monitor.Current.State);
        Assert.Equal(0, output.CallCount);
    }

    [Fact]
    public async Task StartMapsRememberedButDisconnectedDevice()
    {
        var discovery = new FakeDiscovery(Snapshot(
            available: true,
            Device(connected: false)));
        var output = new FakeOutputProbe(isCurrentOutput: false);
        await using var monitor = CreateMonitor(discovery, output);

        await monitor.StartAsync(Selection);

        Assert.Equal(SpeakerConnectionState.PairedOffline, monitor.Current.State);
        Assert.Equal(0, output.CallCount);
    }

    [Fact]
    public async Task StartMapsConnectedDeviceThatIsNotCurrentOutput()
    {
        var discovery = new FakeDiscovery(Snapshot(
            available: true,
            Device(connected: true)));
        var output = new FakeOutputProbe(isCurrentOutput: false);
        await using var monitor = CreateMonitor(discovery, output);

        await monitor.StartAsync(Selection);

        Assert.Equal(SpeakerConnectionState.Connected, monitor.Current.State);
        Assert.Equal(1, output.CallCount);
    }

    [Fact]
    public async Task StartMapsConnectedDeviceThatIsCurrentOutput()
    {
        var discovery = new FakeDiscovery(Snapshot(
            available: true,
            Device(connected: true)));
        var output = new FakeOutputProbe(isCurrentOutput: true);
        await using var monitor = CreateMonitor(discovery, output);

        await monitor.StartAsync(Selection);

        Assert.Equal(SpeakerConnectionState.ConnectedAsOutput, monitor.Current.State);
        Assert.True(monitor.Current.IsCurrentOutput);
    }

    [Fact]
    public async Task RefreshPublishesOnlySemanticChanges()
    {
        var discovery = new FakeDiscovery(Snapshot(
            available: true,
            Device(connected: false)));
        var output = new FakeOutputProbe(isCurrentOutput: false);
        await using var monitor = CreateMonitor(discovery, output);
        var eventCount = 0;
        monitor.ConnectionChanged += (_, _) => eventCount++;

        await monitor.StartAsync(Selection);
        await monitor.RefreshAsync();
        discovery.Current = Snapshot(available: true, Device(connected: true));
        await monitor.RefreshAsync();

        Assert.Equal(2, eventCount);
        Assert.Equal(SpeakerConnectionState.Connected, monitor.Current.State);
    }

    private static SpeakerConnectionMonitor CreateMonitor(
        ISpeakerDiscovery discovery,
        IOutputDeviceProbe outputProbe) =>
        new(discovery, outputProbe, TimeSpan.FromHours(1));

    private static SpeakerDiscoverySnapshot Snapshot(
        bool available,
        params SpeakerDevice[] devices) =>
        new(available, devices, DateTimeOffset.UtcNow);

    private static SpeakerDevice Device(bool connected) =>
        new(
            Selection.DeviceKey,
            Selection.DisplayName,
            IsPaired: true,
            connected,
            DateTimeOffset.UtcNow);

    private sealed class FakeDiscovery(SpeakerDiscoverySnapshot current) : ISpeakerDiscovery
    {
        public SpeakerDiscoverySnapshot Current { get; set; } = current;

        public ValueTask<SpeakerDiscoverySnapshot> DiscoverAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Current);
    }

    private sealed class FakeOutputProbe(bool isCurrentOutput) : IOutputDeviceProbe
    {
        public int CallCount { get; private set; }

        public ValueTask<bool> IsCurrentOutputAsync(
            SpeakerDevice device,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return ValueTask.FromResult(isCurrentOutput);
        }
    }
}
