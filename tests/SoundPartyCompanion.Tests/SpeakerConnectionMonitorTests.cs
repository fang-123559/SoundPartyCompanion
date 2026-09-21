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

    [Fact]
    public async Task StartReadsBatteryImmediatelyForConnectedDevice()
    {
        var now = new DateTimeOffset(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);
        var discovery = new FakeDiscovery(Snapshot(
            available: true,
            Device(connected: true)));
        var battery = new FakeBatteryReader(new SpeakerBatteryReading(100, now));
        var output = new FakeOutputProbe(isCurrentOutput: true);
        await using var monitor = new SpeakerConnectionMonitor(
            discovery,
            output,
            TimeSpan.FromHours(1),
            batteryReader: battery,
            timeProvider: new FakeTimeProvider(now));

        await monitor.StartAsync(Selection);

        Assert.Equal(1, battery.CallCount);
        Assert.Equal(100, monitor.Current.Battery.Percent);
        Assert.False(monitor.Current.Battery.IsStale);
        Assert.Equal(SpeakerChargingState.Unknown, monitor.Current.Battery.Charging);
    }

    [Fact]
    public async Task RefreshKeepsBatteryCacheUntilRefreshIntervalExpires()
    {
        var now = new DateTimeOffset(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);
        var time = new FakeTimeProvider(now);
        var discovery = new FakeDiscovery(Snapshot(
            available: true,
            Device(connected: true)));
        var battery = new FakeBatteryReader(new SpeakerBatteryReading(80, now));
        await using var monitor = new SpeakerConnectionMonitor(
            discovery,
            new FakeOutputProbe(isCurrentOutput: false),
            TimeSpan.FromHours(1),
            batteryReader: battery,
            batteryRefreshInterval: TimeSpan.FromMinutes(1),
            timeProvider: time);

        await monitor.StartAsync(Selection);
        time.Advance(TimeSpan.FromSeconds(30));
        await monitor.RefreshAsync();

        Assert.Equal(1, battery.CallCount);

        time.Advance(TimeSpan.FromSeconds(31));
        battery.Current = new SpeakerBatteryReading(70, time.GetUtcNow());
        await monitor.RefreshAsync();

        Assert.Equal(2, battery.CallCount);
        Assert.Equal(70, monitor.Current.Battery.Percent);
    }

    [Fact]
    public async Task DisconnectKeepsLastBatteryAndMarksItStale()
    {
        var now = new DateTimeOffset(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);
        var discovery = new FakeDiscovery(Snapshot(
            available: true,
            Device(connected: true)));
        var battery = new FakeBatteryReader(new SpeakerBatteryReading(60, now));
        await using var monitor = new SpeakerConnectionMonitor(
            discovery,
            new FakeOutputProbe(isCurrentOutput: false),
            TimeSpan.FromHours(1),
            batteryReader: battery,
            timeProvider: new FakeTimeProvider(now));

        await monitor.StartAsync(Selection);
        discovery.Current = Snapshot(available: true, Device(connected: false));
        await monitor.RefreshAsync();

        Assert.Equal(60, monitor.Current.Battery.Percent);
        Assert.True(monitor.Current.Battery.IsStale);
        Assert.Equal(1, battery.CallCount);
    }

    [Fact]
    public void BatteryRulesHideOfflinePercentageAfterThirtyMinutes()
    {
        var observedAt = new DateTimeOffset(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);

        var snapshot = SpeakerBatteryStateRules.Resolve(
            new SpeakerBatteryReading(40, observedAt),
            isConnected: false,
            observedAt.AddMinutes(31),
            TimeSpan.FromMinutes(2),
            TimeSpan.FromMinutes(30));

        Assert.Null(snapshot.Percent);
        Assert.True(snapshot.IsStale);
        Assert.Equal(observedAt, snapshot.ObservedAt);
    }

    [Fact]
    public void BatteryRulesMarkConnectedReadingStaleAfterTwoMinutes()
    {
        var observedAt = new DateTimeOffset(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);

        var snapshot = SpeakerBatteryStateRules.Resolve(
            new SpeakerBatteryReading(30, observedAt),
            isConnected: true,
            observedAt.AddMinutes(3),
            TimeSpan.FromMinutes(2),
            TimeSpan.FromMinutes(30));

        Assert.Equal(30, snapshot.Percent);
        Assert.True(snapshot.IsStale);
    }

    [Fact]
    public void BatteryRulesRejectOutOfRangeReading()
    {
        var now = new DateTimeOffset(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);

        var snapshot = SpeakerBatteryStateRules.Resolve(
            new SpeakerBatteryReading(101, now),
            isConnected: true,
            now,
            TimeSpan.FromMinutes(2),
            TimeSpan.FromMinutes(30));

        Assert.Equal(SpeakerBatterySnapshot.Unavailable, snapshot);
    }

    [Fact]
    public async Task BatteryReaderFailureDoesNotReplaceConnectedState()
    {
        var discovery = new FakeDiscovery(Snapshot(
            available: true,
            Device(connected: true)));
        await using var monitor = new SpeakerConnectionMonitor(
            discovery,
            new FakeOutputProbe(isCurrentOutput: false),
            TimeSpan.FromHours(1),
            batteryReader: new ThrowingBatteryReader());

        await monitor.StartAsync(Selection);

        Assert.Equal(SpeakerConnectionState.Connected, monitor.Current.State);
        Assert.Equal(SpeakerBatterySnapshot.Unavailable, monitor.Current.Battery);
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

    private sealed class FakeBatteryReader(SpeakerBatteryReading? current) : ISpeakerBatteryReader
    {
        public SpeakerBatteryReading? Current { get; set; } = current;
        public int CallCount { get; private set; }

        public ValueTask<SpeakerBatteryReading?> ReadAsync(
            SpeakerDevice device,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return ValueTask.FromResult(Current);
        }
    }

    private sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan elapsed) => _now += elapsed;
    }

    private sealed class ThrowingBatteryReader : ISpeakerBatteryReader
    {
        public ValueTask<SpeakerBatteryReading?> ReadAsync(
            SpeakerDevice device,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Simulated adapter failure.");
    }
}
