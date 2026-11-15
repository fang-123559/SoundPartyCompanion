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

    [Fact]
    public async Task AudioRefreshAndPollingContinueWhileMediaReadIsPending()
    {
        var now = DateTimeOffset.Now;
        var monitor = new FakeConnectionMonitor(Snapshot(SpeakerConnectionState.ConnectedAsOutput, now));
        var endpoint = new AudioEndpointDescriptor(
            "test-a2dp", "Xiaomi Sound Party", AudioEndpointDirection.Render,
            SpeakerAudioEndpointKind.A2dpRender, IsActive: true, IsDefault: true);
        var audioController = new FakeAudioController(new SpeakerAudioSnapshot([endpoint], 4, false, true, now));
        var pendingMedia = new TaskCompletionSource<SpeakerMediaSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var mediaController = new FakeMediaController(SpeakerMediaSnapshot.Empty(now), pendingMedia.Task);
        using var viewModel = new ConnectionStatusViewModel(
            monitor, audioController, mediaController, new MemorySettingsStore(),
            new FakeStartupManager(), AppSettings.Default);

        await mediaController.SnapshotRequested.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await WaitUntilAsync(() => viewModel.VolumePercent == 4);

        audioController.Current = audioController.Current with { VolumePercent = 28, IsMuted = true };
        await viewModel.RefreshAudioAsync().WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(28, viewModel.VolumePercent);
        Assert.True(viewModel.IsMuted);

        audioController.Current = audioController.Current with { VolumePercent = 63, IsMuted = false };
        await WaitUntilAsync(() => viewModel.VolumePercent == 63 && !viewModel.IsMuted);
        Assert.False(pendingMedia.Task.IsCompleted);

        pendingMedia.SetResult(new SpeakerMediaSnapshot(
            "test-session", "Track", "Artist", MediaPlaybackState.Playing,
            CanPlayPause: true, CanSkipPrevious: true, CanSkipNext: true, now));
        await WaitUntilAsync(() => viewModel.TrackTitle == "Track");
        Assert.Equal(63, viewModel.VolumePercent);
    }

    [Fact]
    public async Task EndpointNotificationUpdatesVolumeBeforeAnOlderReadCompletes()
    {
        var now = DateTimeOffset.Now;
        var monitor = new FakeConnectionMonitor(Snapshot(SpeakerConnectionState.ConnectedAsOutput, now));
        var endpoint = new AudioEndpointDescriptor(
            "test-a2dp", "Xiaomi Sound Party", AudioEndpointDirection.Render,
            SpeakerAudioEndpointKind.A2dpRender, IsActive: true, IsDefault: true);
        var audioController = new FakeAudioController(new SpeakerAudioSnapshot([endpoint], 26, false, true, now));
        using var viewModel = new ConnectionStatusViewModel(
            monitor, audioController, new FakeMediaController(SpeakerMediaSnapshot.Empty(now)),
            new MemorySettingsStore(), new FakeStartupManager(), AppSettings.Default);
        var onVolumeChanged = await audioController.VolumeCallback.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(26, viewModel.VolumePercent);

        var readStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pendingRead = new TaskCompletionSource<SpeakerAudioSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        audioController.ReadSnapshot = cancellationToken =>
        {
            readStarted.TrySetResult();
            return new ValueTask<SpeakerAudioSnapshot>(pendingRead.Task.WaitAsync(cancellationToken));
        };
        var refresh = viewModel.RefreshAudioAsync();
        await readStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));

        onVolumeChanged(34, true);
        await WaitUntilAsync(() => viewModel.VolumePercent == 34 && viewModel.IsMuted);
        Assert.False(refresh.IsCompleted);

        var oldSnapshot = audioController.Current;
        audioController.Current = oldSnapshot with { VolumePercent = 34, IsMuted = true };
        audioController.ReadSnapshot = null;
        pendingRead.SetResult(oldSnapshot);
        await refresh.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(34, viewModel.VolumePercent);
        Assert.True(viewModel.IsMuted);

        monitor.RefreshSnapshot = Snapshot(SpeakerConnectionState.PairedOffline, now);
        await viewModel.RefreshAsync();
        onVolumeChanged(49, false);
        Assert.Equal(0, viewModel.VolumePercent);
    }

    [Fact]
    public async Task AudioControlsAndMessagesReflectIndependentReadFailures()
    {
        var now = DateTimeOffset.Now;
        var monitor = new FakeConnectionMonitor(Snapshot(SpeakerConnectionState.Connected, now));
        var endpoint = new AudioEndpointDescriptor(
            "test-a2dp", "Xiaomi Sound Party", AudioEndpointDirection.Render,
            SpeakerAudioEndpointKind.A2dpRender, IsActive: true, IsDefault: false);
        var audioController = new FakeAudioController();
        using var viewModel = new ConnectionStatusViewModel(
            monitor, audioController, new FakeMediaController(SpeakerMediaSnapshot.Empty(now)),
            new MemorySettingsStore(), new FakeStartupManager(), AppSettings.Default);

        await viewModel.RefreshAudioAsync();
        Assert.Equal("未找到可控的 A2DP 输出", viewModel.AudioSummaryText);

        audioController.Current = new SpeakerAudioSnapshot([endpoint], null, false, false, now);
        await viewModel.RefreshAudioAsync();
        Assert.Equal("已找到 A2DP 输出，暂时无法读取音量", viewModel.AudioSummaryText);
        Assert.True(viewModel.SetAsCurrentOutputCommand.CanExecute(null));
        Assert.True(viewModel.ToggleMuteCommand.CanExecute(null));
        Assert.False(viewModel.VolumeUpCommand.CanExecute(null));
        Assert.Null(viewModel.ReportedVolumePercent);
        Assert.False(viewModel.ReportedMuteState);

        audioController.Current = new SpeakerAudioSnapshot([endpoint], 26, null, false, now);
        await viewModel.RefreshAudioAsync();
        Assert.Equal("音量 26%", viewModel.AudioSummaryText);
        Assert.True(viewModel.VolumeUpCommand.CanExecute(null));
        Assert.False(viewModel.ToggleMuteCommand.CanExecute(null));
        Assert.Equal(26, viewModel.ReportedVolumePercent);
        Assert.Null(viewModel.ReportedMuteState);
    }

    [Fact]
    public void ConnectedBatteryIsShownWithWindowsSource()
    {
        var now = DateTimeOffset.Now;
        var snapshot = Snapshot(SpeakerConnectionState.Connected, now) with
        {
            Battery = new SpeakerBatterySnapshot(
                100,
                SpeakerChargingState.Unknown,
                now,
                IsStale: false)
        };
        var monitor = new FakeConnectionMonitor(snapshot);

        using var viewModel = CreateViewModel(monitor, SpeakerMediaSnapshot.Empty(now));

        Assert.True(viewModel.HasBattery);
        Assert.Equal(100, viewModel.BatteryPercent);
        Assert.Equal("100%", viewModel.BatteryText);
        Assert.Contains("Windows HFP", viewModel.BatteryDetailText, StringComparison.Ordinal);
    }

    [Fact]
    public void OfflineBatteryCacheIsMarkedAsStale()
    {
        var now = DateTimeOffset.Now;
        var snapshot = Snapshot(SpeakerConnectionState.PairedOffline, now) with
        {
            Battery = new SpeakerBatterySnapshot(
                50,
                SpeakerChargingState.Unknown,
                now.AddMinutes(-1),
                IsStale: true)
        };
        var monitor = new FakeConnectionMonitor(snapshot);

        using var viewModel = CreateViewModel(monitor, SpeakerMediaSnapshot.Empty(now));

        Assert.Equal("50%", viewModel.BatteryText);
        Assert.Contains("音箱已离线", viewModel.BatteryDetailText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FreshBatteryCrossingRaisesOneLowBatteryReminder()
    {
        var now = DateTimeOffset.Now;
        var initial = Snapshot(SpeakerConnectionState.Connected, now) with
        {
            Battery = new SpeakerBatterySnapshot(
                26,
                SpeakerChargingState.Unknown,
                now,
                IsStale: false)
        };
        var monitor = new FakeConnectionMonitor(initial)
        {
            RefreshSnapshot = initial with
            {
                Battery = new SpeakerBatterySnapshot(
                    20,
                    SpeakerChargingState.Unknown,
                    now.AddMinutes(1),
                    IsStale: false)
            }
        };
        using var viewModel = CreateViewModel(monitor, SpeakerMediaSnapshot.Empty(now));
        var reminders = new List<LowBatteryReminderEventArgs>();
        viewModel.LowBatteryReminderRequested += (_, args) => reminders.Add(args);

        await viewModel.RefreshAsync();
        await viewModel.RefreshAsync();

        var reminder = Assert.Single(reminders);
        Assert.Equal(20, reminder.Percent);
        Assert.Equal(20, reminder.Threshold);
    }

    [Fact]
    public void InitialCriticalBatteryCanBeCheckedAfterEventSubscription()
    {
        var now = DateTimeOffset.Now;
        var initial = Snapshot(SpeakerConnectionState.Connected, now) with
        {
            Battery = new SpeakerBatterySnapshot(
                10,
                SpeakerChargingState.Unknown,
                now,
                IsStale: false)
        };
        using var viewModel = CreateViewModel(
            new FakeConnectionMonitor(initial),
            SpeakerMediaSnapshot.Empty(now));
        var reminders = new List<LowBatteryReminderEventArgs>();
        viewModel.LowBatteryReminderRequested += (_, args) => reminders.Add(args);

        Assert.True(viewModel.IsLowBattery);
        Assert.True(viewModel.IsCriticalBattery);
        Assert.Equal("10%", viewModel.BatteryDisplayText);

        viewModel.CheckLowBatteryReminder();
        viewModel.CheckLowBatteryReminder();

        var reminder = Assert.Single(reminders);
        Assert.Equal(10, reminder.Percent);
        Assert.Equal(10, reminder.Threshold);
    }

    [Fact]
    public void StaleBatteryDoesNotShowLowBatteryIndicator()
    {
        var now = DateTimeOffset.Now;
        var initial = Snapshot(SpeakerConnectionState.Connected, now) with
        {
            Battery = new SpeakerBatterySnapshot(
                10,
                SpeakerChargingState.Unknown,
                now.AddHours(-1),
                IsStale: true)
        };

        using var viewModel = CreateViewModel(
            new FakeConnectionMonitor(initial),
            SpeakerMediaSnapshot.Empty(now));

        Assert.False(viewModel.IsLowBattery);
        Assert.False(viewModel.IsCriticalBattery);
        Assert.Equal("10%", viewModel.BatteryDisplayText);
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
        public SpeakerConnectionSnapshot RefreshSnapshot { get; set; } = current;

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

    private sealed class FakeAudioController(SpeakerAudioSnapshot? initial = null) : IAudioEndpointController
    {
        public SpeakerAudioSnapshot Current { get; set; } = initial ?? SpeakerAudioSnapshot.Unavailable(DateTimeOffset.Now);
        public Func<CancellationToken, ValueTask<SpeakerAudioSnapshot>>? ReadSnapshot { get; set; }
        public TaskCompletionSource<Action<double, bool>> VolumeCallback { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IDisposable? WatchVolume(string endpointId, Action<double, bool> volumeChanged)
        {
            VolumeCallback.TrySetResult(volumeChanged);
            return null;
        }

        public ValueTask<SpeakerAudioSnapshot> GetSnapshotAsync(
            SpeakerDevice device,
            CancellationToken cancellationToken = default) =>
            ReadSnapshot?.Invoke(cancellationToken) ?? ValueTask.FromResult(Current);

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

    private sealed class FakeMediaController(SpeakerMediaSnapshot current, Task<SpeakerMediaSnapshot>? pending = null) : IMediaSessionController
    {
        public TaskCompletionSource SnapshotRequested { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask<SpeakerMediaSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
        {
            SnapshotRequested.TrySetResult();
            return pending is null
                ? ValueTask.FromResult(current)
                : new ValueTask<SpeakerMediaSnapshot>(pending.WaitAsync(cancellationToken));
        }
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
