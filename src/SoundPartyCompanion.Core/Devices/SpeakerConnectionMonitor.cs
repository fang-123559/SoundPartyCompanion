using SoundPartyCompanion.Core.Diagnostics;

namespace SoundPartyCompanion.Core.Devices;

public sealed class SpeakerConnectionMonitor : IConnectionMonitor
{
    private readonly ISpeakerDiscovery _discovery;
    private readonly IOutputDeviceProbe _outputProbe;
    private readonly ISpeakerBatteryReader? _batteryReader;
    private readonly IAppLogger? _logger;
    private readonly TimeSpan _pollInterval;
    private readonly TimeSpan _batteryRefreshInterval;
    private readonly TimeSpan _batteryStaleAfter;
    private readonly TimeSpan _offlineBatteryHideAfter;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly object _stateGate = new();
    private CancellationTokenSource? _pollCancellation;
    private Task? _pollTask;
    private SpeakerSelection? _selection;
    private SpeakerBatteryReading? _lastBatteryReading;
    private DateTimeOffset? _lastBatteryAttemptAt;
    private bool _batteryRefreshRequired = true;
    private bool _disposed;

    public SpeakerConnectionMonitor(
        ISpeakerDiscovery discovery,
        IOutputDeviceProbe outputProbe,
        TimeSpan? pollInterval = null,
        IAppLogger? logger = null,
        ISpeakerBatteryReader? batteryReader = null,
        TimeSpan? batteryRefreshInterval = null,
        TimeSpan? batteryStaleAfter = null,
        TimeSpan? offlineBatteryHideAfter = null,
        TimeProvider? timeProvider = null)
    {
        _discovery = discovery ?? throw new ArgumentNullException(nameof(discovery));
        _outputProbe = outputProbe ?? throw new ArgumentNullException(nameof(outputProbe));
        _batteryReader = batteryReader;
        _logger = logger;
        _pollInterval = pollInterval ?? TimeSpan.FromSeconds(3);
        _batteryRefreshInterval = batteryRefreshInterval ?? TimeSpan.FromSeconds(60);
        _batteryStaleAfter = batteryStaleAfter ?? TimeSpan.FromMinutes(2);
        _offlineBatteryHideAfter = offlineBatteryHideAfter ?? TimeSpan.FromMinutes(30);
        _timeProvider = timeProvider ?? TimeProvider.System;

        if (_pollInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(pollInterval));
        }

        if (_batteryRefreshInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(batteryRefreshInterval));
        }

        if (_batteryStaleAfter <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(batteryStaleAfter));
        }

        if (_offlineBatteryHideAfter <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(offlineBatteryHideAfter));
        }

        Current = new SpeakerConnectionSnapshot(
            SpeakerConnectionState.PairedOffline,
            null,
            null,
            DateTimeOffset.UtcNow);
    }

    public event EventHandler<SpeakerConnectionChangedEventArgs>? ConnectionChanged;

    public SpeakerConnectionSnapshot Current { get; private set; }

    public async Task StartAsync(
        SpeakerSelection? selection,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (_pollCancellation is not null)
        {
            throw new InvalidOperationException("The connection monitor is already running.");
        }

        ResetBatteryWhenSelectionChanges(selection);
        _selection = selection;
        _pollCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        await RefreshAsync(_pollCancellation.Token).ConfigureAwait(false);
        _pollTask = PollAsync(_pollCancellation.Token);
    }

    public async Task SetSelectionAsync(
        SpeakerSelection? selection,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ResetBatteryWhenSelectionChanges(selection);
        _selection = selection;
        await RefreshAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var discovery = await _discovery.DiscoverAsync(cancellationToken).ConfigureAwait(false);
            var next = await CreateSnapshotAsync(discovery, cancellationToken).ConfigureAwait(false);
            Publish(next);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger?.Write(
                AppLogLevel.Warning,
                $"Connection refresh failed ({exception.GetType().Name}).");

            Publish(WithBattery(new SpeakerConnectionSnapshot(
                SpeakerConnectionState.BluetoothUnavailable,
                _selection?.DeviceKey,
                _selection?.DisplayName,
                DateTimeOffset.UtcNow), isConnected: false));
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    public async Task StopAsync()
    {
        if (_pollCancellation is null)
        {
            return;
        }

        await _pollCancellation.CancelAsync().ConfigureAwait(false);

        if (_pollTask is not null)
        {
            try
            {
                await _pollTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _pollCancellation.Dispose();
        _pollCancellation = null;
        _pollTask = null;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await StopAsync().ConfigureAwait(false);
        _refreshGate.Dispose();
        _disposed = true;
    }

    private async Task PollAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(_pollInterval);

        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            await RefreshAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask<SpeakerConnectionSnapshot> CreateSnapshotAsync(
        SpeakerDiscoverySnapshot discovery,
        CancellationToken cancellationToken)
    {
        if (!discovery.IsBluetoothAvailable)
        {
            _batteryRefreshRequired = true;
            return WithBattery(new SpeakerConnectionSnapshot(
                SpeakerConnectionState.BluetoothUnavailable,
                _selection?.DeviceKey,
                _selection?.DisplayName,
                discovery.ObservedAt), isConnected: false);
        }

        if (_selection is null)
        {
            _batteryRefreshRequired = true;
            return WithBattery(new SpeakerConnectionSnapshot(
                SpeakerConnectionState.PairedOffline,
                null,
                null,
                discovery.ObservedAt), isConnected: false);
        }

        var device = discovery.Devices.FirstOrDefault(candidate =>
            string.Equals(
                candidate.DeviceKey,
                _selection.DeviceKey,
                StringComparison.Ordinal));

        if (device is null || !device.IsPaired || !device.IsConnected)
        {
            _batteryRefreshRequired = true;
            return WithBattery(new SpeakerConnectionSnapshot(
                SpeakerConnectionState.PairedOffline,
                _selection.DeviceKey,
                device?.DisplayName ?? _selection.DisplayName,
                discovery.ObservedAt), isConnected: false);
        }

        await RefreshBatteryIfNeededAsync(device, cancellationToken).ConfigureAwait(false);
        _batteryRefreshRequired = false;

        var isCurrentOutput = await _outputProbe
            .IsCurrentOutputAsync(device, cancellationToken)
            .ConfigureAwait(false);

        return WithBattery(new SpeakerConnectionSnapshot(
            isCurrentOutput
                ? SpeakerConnectionState.ConnectedAsOutput
                : SpeakerConnectionState.Connected,
            device.DeviceKey,
            device.DisplayName,
            discovery.ObservedAt), isConnected: true);
    }

    private async ValueTask RefreshBatteryIfNeededAsync(
        SpeakerDevice device,
        CancellationToken cancellationToken)
    {
        if (_batteryReader is null)
        {
            return;
        }

        var now = _timeProvider.GetUtcNow();
        if (!_batteryRefreshRequired &&
            _lastBatteryAttemptAt is DateTimeOffset lastAttempt &&
            now - lastAttempt < _batteryRefreshInterval)
        {
            return;
        }

        _lastBatteryAttemptAt = now;
        try
        {
            var reading = await _batteryReader.ReadAsync(device, cancellationToken).ConfigureAwait(false);
            if (reading is not null && reading.Percent is >= 0 and <= 100)
            {
                _lastBatteryReading = reading;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger?.Write(
                AppLogLevel.Warning,
                $"Battery refresh failed ({exception.GetType().Name}).");
        }
    }

    private SpeakerConnectionSnapshot WithBattery(
        SpeakerConnectionSnapshot snapshot,
        bool isConnected) =>
        snapshot with
        {
            Battery = SpeakerBatteryStateRules.Resolve(
                _lastBatteryReading,
                isConnected,
                _timeProvider.GetUtcNow(),
                _batteryStaleAfter,
                _offlineBatteryHideAfter)
        };

    private void ResetBatteryWhenSelectionChanges(SpeakerSelection? selection)
    {
        if (string.Equals(_selection?.DeviceKey, selection?.DeviceKey, StringComparison.Ordinal))
        {
            return;
        }

        _lastBatteryReading = null;
        _lastBatteryAttemptAt = null;
        _batteryRefreshRequired = true;
    }

    private void Publish(SpeakerConnectionSnapshot next)
    {
        bool changed;

        lock (_stateGate)
        {
            changed = Current.State != next.State ||
                !string.Equals(Current.DeviceKey, next.DeviceKey, StringComparison.Ordinal) ||
                !string.Equals(Current.DisplayName, next.DisplayName, StringComparison.Ordinal) ||
                Current.Battery != next.Battery;
            Current = next;
        }

        if (changed)
        {
            ConnectionChanged?.Invoke(this, new SpeakerConnectionChangedEventArgs(next));
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
