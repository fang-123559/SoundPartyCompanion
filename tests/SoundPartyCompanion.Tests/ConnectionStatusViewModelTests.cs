using SoundPartyCompanion.App.ViewModels;
using SoundPartyCompanion.Core.Audio;
using SoundPartyCompanion.Core.Devices;
using SoundPartyCompanion.Core.Diagnostics;
using SoundPartyCompanion.Core.Media;
using SoundPartyCompanion.Core.Settings;
using Xunit;

namespace SoundPartyCompanion.Tests;

public sealed class ConnectionStatusViewModelTests
{
    [Fact]
    public async Task MediaCommandUsesDisplayedTargetAndShowsFailureWithoutParallelClicks()
    {
        var now = DateTimeOffset.Now;
        var monitor = new FakeConnectionMonitor(Snapshot(SpeakerConnectionState.Connected, now));
        var pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var requested = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var media = new FakeMediaController(new SpeakerMediaSnapshot(
            "displayed-series", "Series", null, MediaPlaybackState.Playing, true, true, true, now))
        {
            Control = id => { requested.TrySetResult(id); return new ValueTask<bool>(pending.Task); }
        };
        using var viewModel = new ConnectionStatusViewModel(monitor, new FakeAudioController(), media,
            new MemorySettingsStore(), new FakeStartupManager(), AppSettings.Default);
        await WaitUntilAsync(() => viewModel.NextCommand.CanExecute(null));
        viewModel.NextCommand.Execute(null);
        Assert.Equal("displayed-series", await requested.Task.WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.False(viewModel.PreviousCommand.CanExecute(null));
        Assert.False(viewModel.PlayPauseCommand.CanExecute(null));
        Assert.Equal("正在请求播放器…", viewModel.MediaDetailText);
        pending.SetResult(false);
        await WaitUntilAsync(() => viewModel.NextCommand.CanExecute(null));
        Assert.Equal("播放器未执行操作，请在播放器中操作", viewModel.MediaDetailText);
    }

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
        Assert.Equal("正在等待音频连接", viewModel.AudioSummaryText);

        audioController.Current = new SpeakerAudioSnapshot([endpoint], null, false, false, now);
        await viewModel.RefreshAudioAsync();
        Assert.Equal("已找到 A2DP 输出，暂时无法读取音量", viewModel.AudioSummaryText);
        Assert.True(viewModel.SetAsCurrentOutputCommand.CanExecute(null));
        Assert.True(viewModel.ToggleMuteCommand.CanExecute(null));
        Assert.False(viewModel.VolumeUpCommand.CanExecute(null));
        Assert.Null(viewModel.ReportedVolumePercent);
        Assert.Equal("--", viewModel.VolumeDisplayText);
        Assert.Equal("--", viewModel.VolumeValueText);
        Assert.False(viewModel.IsVolumeAvailable);
        Assert.False(viewModel.ReportedMuteState);

        audioController.Current = new SpeakerAudioSnapshot([endpoint], 26, null, false, now);
        await viewModel.RefreshAudioAsync();
        Assert.Equal("音量 26%", viewModel.AudioSummaryText);
        Assert.True(viewModel.VolumeUpCommand.CanExecute(null));
        Assert.False(viewModel.ToggleMuteCommand.CanExecute(null));
        Assert.Equal(26, viewModel.ReportedVolumePercent);
        Assert.Equal("26%", viewModel.VolumeDisplayText);
        audioController.Current = audioController.Current with { VolumePercent = 0 };
        await viewModel.RefreshAudioAsync();
        Assert.Equal("0%", viewModel.VolumeDisplayText);
        Assert.True(viewModel.IsVolumeAvailable);
        Assert.Null(viewModel.ReportedMuteState);
    }

    [Fact]
    public async Task HeaderAndVolumeUseTheSameOutputSnapshotEvenWhenConnectionStatusLags()
    {
        var now = DateTimeOffset.Now;
        var monitor = new FakeConnectionMonitor(Snapshot(SpeakerConnectionState.Connected, now));
        var endpoint = new AudioEndpointDescriptor(
            "test-a2dp", "Xiaomi Sound Party", AudioEndpointDirection.Render,
            SpeakerAudioEndpointKind.A2dpRender, IsActive: true, IsDefault: true);
        var audioController = new FakeAudioController(new SpeakerAudioSnapshot([endpoint], 20, false, true, now));
        using var viewModel = new ConnectionStatusViewModel(
            monitor, audioController, new FakeMediaController(SpeakerMediaSnapshot.Empty(now)),
            new MemorySettingsStore(), new FakeStartupManager(), AppSettings.Default);

        await viewModel.RefreshAudioAsync();
        Assert.Equal(SpeakerConnectionState.Connected, monitor.Current.State);
        Assert.Equal("立体声输出 · A2DP", viewModel.DetailText);
        Assert.Equal("音量 20% · 当前输出", viewModel.AudioSummaryText);

        audioController.Current = audioController.Current with { IsMuted = true };
        await viewModel.RefreshAudioAsync();
        Assert.Equal("已静音 · A2DP · 当前输出", viewModel.AudioSummaryText);

        monitor.RefreshSnapshot = Snapshot(SpeakerConnectionState.ConnectedAsOutput, now);
        audioController.Current = new SpeakerAudioSnapshot([endpoint with { IsDefault = false }], 20, false, false, now);
        await viewModel.RefreshAsync();
        await viewModel.RefreshAudioAsync();
        Assert.Equal(SpeakerConnectionState.ConnectedAsOutput, monitor.Current.State);
        Assert.Equal("当前声音未输出到此音箱", viewModel.DetailText);
        Assert.Equal("音量 20%", viewModel.AudioSummaryText);
    }

    [Fact]
    public async Task FirstVolumeReadIsLoggedOnceUntilAudioIsLost()
    {
        var now = new DateTimeOffset(2026, 10, 3, 12, 0, 19, TimeSpan.FromHours(8));
        var monitor = new FakeConnectionMonitor(Snapshot(SpeakerConnectionState.Connected, now));
        var endpoint = new AudioEndpointDescriptor(
            "test-a2dp", "Xiaomi Sound Party", AudioEndpointDirection.Render,
            SpeakerAudioEndpointKind.A2dpRender, IsActive: true, IsDefault: false);
        var audioController = new FakeAudioController(new SpeakerAudioSnapshot([endpoint], null, false, false, now));
        var logger = new MemoryLogger();
        using var viewModel = new ConnectionStatusViewModel(
            monitor, audioController, new FakeMediaController(SpeakerMediaSnapshot.Empty(now)),
            new MemorySettingsStore(), new FakeStartupManager(), AppSettings.Default, logger);

        await viewModel.RefreshAudioAsync();
        Assert.Empty(logger.Messages);

        audioController.Current = audioController.Current with { VolumePercent = 0 };
        await viewModel.RefreshAudioAsync();
        audioController.Current = audioController.Current with { VolumePercent = 20 };
        await viewModel.RefreshAudioAsync();
        Assert.StartsWith(
            "Speaker volume first read. ReadUtc=2026-10-03T04:00:19.0000000Z DisplayUtc=",
            Assert.Single(logger.Messages));

        audioController.Current = SpeakerAudioSnapshot.Unavailable(now);
        await viewModel.RefreshAudioAsync();
        audioController.Current = new SpeakerAudioSnapshot([endpoint], 20, false, false, now.AddSeconds(10));
        await viewModel.RefreshAudioAsync();
        Assert.Collection(logger.Messages,
            message => Assert.Contains("ReadUtc=2026-10-03T04:00:19.0000000Z", message),
            message => Assert.Contains("ReadUtc=2026-10-03T04:00:29.0000000Z", message));
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
        public ValueTask<bool> IsCurrentOutputAsync(SpeakerDevice device, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Current.IsCurrentOutput);

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

    private sealed class MemoryLogger : IAppLogger
    {
        public System.Collections.Concurrent.ConcurrentQueue<string> Messages { get; } = new();

        public void Write(AppLogLevel level, string message, Exception? exception = null) => Messages.Enqueue(message);
    }

    private sealed class FakeMediaController(SpeakerMediaSnapshot current, Task<SpeakerMediaSnapshot>? pending = null) : IMediaSessionController
    {
        public Func<string, ValueTask<bool>>? Control { get; init; }
        public TaskCompletionSource SnapshotRequested { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask<SpeakerMediaSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
        {
            SnapshotRequested.TrySetResult();
            return pending is null
                ? ValueTask.FromResult(current)
                : new ValueTask<SpeakerMediaSnapshot>(pending.WaitAsync(cancellationToken));
        }
        public ValueTask<bool> TogglePlayPauseAsync(string sessionId, CancellationToken cancellationToken = default) => ValueTask.FromResult(true);
        public ValueTask<bool> SkipPreviousAsync(string sessionId, CancellationToken cancellationToken = default) => ValueTask.FromResult(true);
        public ValueTask<bool> SkipNextAsync(string sessionId, CancellationToken cancellationToken = default) => Control?.Invoke(sessionId) ?? ValueTask.FromResult(true);
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
