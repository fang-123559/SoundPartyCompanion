namespace SoundPartyCompanion.Core.Devices;

public interface ISpeakerDiscovery
{
    ValueTask<SpeakerDiscoverySnapshot> DiscoverAsync(CancellationToken cancellationToken = default);
}

public interface IOutputDeviceProbe
{
    ValueTask<bool> IsCurrentOutputAsync(
        SpeakerDevice device,
        CancellationToken cancellationToken = default);
}

public interface ISpeakerSelectionStore
{
    ValueTask<SpeakerSelection?> LoadAsync(CancellationToken cancellationToken = default);

    ValueTask SaveAsync(SpeakerSelection selection, CancellationToken cancellationToken = default);
}

public interface IConnectionMonitor : IAsyncDisposable
{
    event EventHandler<SpeakerConnectionChangedEventArgs>? ConnectionChanged;

    SpeakerConnectionSnapshot Current { get; }

    Task StartAsync(SpeakerSelection? selection, CancellationToken cancellationToken = default);

    Task SetSelectionAsync(SpeakerSelection? selection, CancellationToken cancellationToken = default);

    Task RefreshAsync(CancellationToken cancellationToken = default);

    Task StopAsync();
}
