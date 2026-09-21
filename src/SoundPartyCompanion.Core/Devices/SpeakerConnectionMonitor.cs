using SoundPartyCompanion.Core.Diagnostics;

namespace SoundPartyCompanion.Core.Devices;

public sealed class SpeakerConnectionMonitor : IConnectionMonitor
{
    private readonly ISpeakerDiscovery _discovery;
    private readonly IOutputDeviceProbe _outputProbe;
    private readonly IAppLogger? _logger;
    private readonly TimeSpan _pollInterval;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly object _stateGate = new();
    private CancellationTokenSource? _pollCancellation;
    private Task? _pollTask;
    private SpeakerSelection? _selection;
    private bool _disposed;

    public SpeakerConnectionMonitor(
        ISpeakerDiscovery discovery,
        IOutputDeviceProbe outputProbe,
        TimeSpan? pollInterval = null,
        IAppLogger? logger = null)
    {
        _discovery = discovery ?? throw new ArgumentNullException(nameof(discovery));
        _outputProbe = outputProbe ?? throw new ArgumentNullException(nameof(outputProbe));
        _logger = logger;
        _pollInterval = pollInterval ?? TimeSpan.FromSeconds(3);

        if (_pollInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(pollInterval));
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

            Publish(new SpeakerConnectionSnapshot(
                SpeakerConnectionState.BluetoothUnavailable,
                _selection?.DeviceKey,
                _selection?.DisplayName,
                DateTimeOffset.UtcNow));
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
            return new SpeakerConnectionSnapshot(
                SpeakerConnectionState.BluetoothUnavailable,
                _selection?.DeviceKey,
                _selection?.DisplayName,
                discovery.ObservedAt);
        }

        if (_selection is null)
        {
            return new SpeakerConnectionSnapshot(
                SpeakerConnectionState.PairedOffline,
                null,
                null,
                discovery.ObservedAt);
        }

        var device = discovery.Devices.FirstOrDefault(candidate =>
            string.Equals(
                candidate.DeviceKey,
                _selection.DeviceKey,
                StringComparison.Ordinal));

        if (device is null || !device.IsPaired || !device.IsConnected)
        {
            return new SpeakerConnectionSnapshot(
                SpeakerConnectionState.PairedOffline,
                _selection.DeviceKey,
                device?.DisplayName ?? _selection.DisplayName,
                discovery.ObservedAt);
        }

        var isCurrentOutput = await _outputProbe
            .IsCurrentOutputAsync(device, cancellationToken)
            .ConfigureAwait(false);

        return new SpeakerConnectionSnapshot(
            isCurrentOutput
                ? SpeakerConnectionState.ConnectedAsOutput
                : SpeakerConnectionState.Connected,
            device.DeviceKey,
            device.DisplayName,
            discovery.ObservedAt);
    }

    private void Publish(SpeakerConnectionSnapshot next)
    {
        bool changed;

        lock (_stateGate)
        {
            changed = Current.State != next.State ||
                !string.Equals(Current.DeviceKey, next.DeviceKey, StringComparison.Ordinal) ||
                !string.Equals(Current.DisplayName, next.DisplayName, StringComparison.Ordinal);
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
