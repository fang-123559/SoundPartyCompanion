using SoundPartyCompanion.App.ViewModels;
using SoundPartyCompanion.Core.Audio;
using SoundPartyCompanion.Core.Devices;
using SoundPartyCompanion.Core.Media;
using SoundPartyCompanion.Core.Settings;
using Xunit;

namespace SoundPartyCompanion.Tests;

public sealed class ConnectionStatusViewModelTests
{
    [Fact]
    public async Task RefreshUpdatesLastCheckedTextWhenConnectionStateDoesNotChange()
    {
        var initial = DateTimeOffset.Now.AddMinutes(-5);
        var refreshed = DateTimeOffset.Now;
        var monitor = new FakeConnectionMonitor(Snapshot(SpeakerConnectionState.PairedOffline, initial))
        {
            RefreshSnapshot = Snapshot(SpeakerConnectionState.PairedOffline, refreshed)
        };
        using var viewModel = CreateViewModel(monitor, SpeakerMediaSnapshot.Empty(initial));

        await viewModel.RefreshAsync();

        Assert.Equal($"{refreshed:HH:mm:ss} 更新", viewModel.LastCheckedText);
        Assert.Equal(WidgetDisplayMode.Offline, viewModel.WidgetMode);
    }

    [Fact]
    public async Task ActiveMediaSessionControlsMediaPanelVisibility()
    {
        var now = DateTimeOffset.Now;
        var monitor = new FakeConnectionMonitor(Snapshot(SpeakerConnectionState.ConnectedAsOutput, now));
        var media = new SpeakerMediaSnapshot(
            "test-session", "Long track title", "Artist", MediaPlaybackState.Playing,
            CanPlayPause: true, CanSkipPrevious: true, CanSkipNext: true, now);
        using var viewModel = CreateViewModel(monitor, media);

        await WaitUntilAsync(() => viewModel.HasMediaSession);

        Assert.True(viewModel.HasMediaSession);
        Assert.Equal("Long track title", viewModel.TrackTitle);
        Assert.Equal("Artist", viewModel.TrackArtist);
    }

    private static ConnectionStatusViewModel CreateViewModel(
        IConnectionMonitor monitor,
        SpeakerMediaSnapshot media) =>
        new(
            monitor,
            new FakeAudioController(),
            new FakeMediaController(media),
            new MemorySettingsStore(),
            new FakeStartupManager(),
            AppSettings.Default with { StartWithWindows = false });

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (!predicate() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(25);
        }
        Assert.True(predicate());
    }

    private static SpeakerConnectionSnapshot Snapshot(
        SpeakerConnectionState state,
        DateTimeOffset observedAt) =>
        new(state, "test-device", "Xiaomi Sound Party", observedAt);

    private sealed class FakeConnectionMonitor(SpeakerConnectionSnapshot current) : IConnectionMonitor
    {
        public event EventHandler<SpeakerConnectionChangedEventArgs>? ConnectionChanged
        {
            add { }
            remove { }
        }
        public SpeakerConnectionSnapshot Current { get; private set; } = current;
        public SpeakerConnectionSnapshot RefreshSnapshot { get; init; } = current;

        public Task StartAsync(SpeakerSelection? selection, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SetSelectionAsync(SpeakerSelection? selection, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public Task RefreshAsync(CancellationToken cancellationToken = default)
        {
            Current = RefreshSnapshot;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeAudioController : IAudioEndpointController
    {
        public ValueTask<SpeakerAudioSnapshot> GetSnapshotAsync(
            SpeakerDevice device,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(SpeakerAudioSnapshot.Unavailable(DateTimeOffset.Now));

        public ValueTask<AudioControlResult> SetVolumeAsync(
            SpeakerDevice device,
            double volumePercent,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(AudioControlResult.Success);

        public ValueTask<AudioControlResult> SetMutedAsync(
            SpeakerDevice device,
            bool isMuted,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(AudioControlResult.Success);

        public ValueTask<AudioControlResult> SetAsDefaultOutputAsync(
            SpeakerDevice device,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(AudioControlResult.Success);
    }

    private sealed class FakeMediaController(SpeakerMediaSnapshot current) : IMediaSessionController
    {
        public ValueTask<SpeakerMediaSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(current);
        public ValueTask<bool> TogglePlayPauseAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(true);
        public ValueTask<bool> SkipPreviousAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(true);
        public ValueTask<bool> SkipNextAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(true);
    }

    private sealed class MemorySettingsStore : IAppSettingsStore
    {
        public ValueTask<AppSettings> LoadAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(AppSettings.Default);
        public ValueTask SaveAsync(AppSettings settings, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }

    private sealed class FakeStartupManager : IStartupManager
    {
        public bool IsEnabled => false;
        public ValueTask<bool> SetEnabledAsync(bool isEnabled, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(true);
    }
}
