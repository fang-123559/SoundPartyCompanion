using System.ComponentModel;
using System.Runtime.CompilerServices;
using SoundPartyCompanion.App.Infrastructure;
using SoundPartyCompanion.Core.Audio;
using SoundPartyCompanion.Core.Devices;
using SoundPartyCompanion.Core.Media;
using SoundPartyCompanion.Core.Settings;

namespace SoundPartyCompanion.App.ViewModels;

public sealed class ConnectionStatusViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IConnectionMonitor _monitor;
    private readonly IAudioEndpointController _audioController;
    private readonly IMediaSessionController _mediaController;
    private readonly IAppSettingsStore _settingsStore;
    private readonly IStartupManager _startupManager;
    private readonly CancellationTokenSource _runtimeCancellation = new();
    private readonly SemaphoreSlim _runtimeGate = new(1, 1);
    private readonly SemaphoreSlim _settingsGate = new(1, 1);
    private SpeakerAudioSnapshot _audioSnapshot = SpeakerAudioSnapshot.Unavailable(DateTimeOffset.UtcNow);
    private SpeakerMediaSnapshot _mediaSnapshot = SpeakerMediaSnapshot.Empty(DateTimeOffset.UtcNow);
    private AppSettings _settings;
    private SpeakerConnectionState _connectionState = SpeakerConnectionState.PairedOffline;
    private WidgetDisplayMode _widgetMode = WidgetDisplayMode.Compact;
    private bool _expandedRequested;
    private bool _offlineDetailsRequested;
    private string _deviceName = "小米蓝牙音箱 Pro";
    private string _statusText = "正在检查连接";
    private string _detailText = "等待 Windows 蓝牙状态";
    private string _audioSummaryText = "音频控制将在连接后可用";
    private string _nowPlayingText = "无活动媒体";
    private string _trackTitle = "无活动媒体";
    private string _trackArtist = "等待播放器";
    private string _muteCommandText = "静音";
    private string _playPauseCommandText = "播放";
    private string _playPauseGlyph = "▶";
    private string _lastCheckedText = string.Empty;
    private double _volumePercent;
    private bool _isMuted;
    private bool _isCurrentOutput;
    private bool _isConnected;
    private bool _hasMediaSession;
    private bool _disposed;

    public ConnectionStatusViewModel(
        IConnectionMonitor monitor,
        IAudioEndpointController audioController,
        IMediaSessionController mediaController,
        IAppSettingsStore settingsStore,
        IStartupManager startupManager,
        AppSettings initialSettings)
    {
        _monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));
        _audioController = audioController ?? throw new ArgumentNullException(nameof(audioController));
        _mediaController = mediaController ?? throw new ArgumentNullException(nameof(mediaController));
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        _startupManager = startupManager ?? throw new ArgumentNullException(nameof(startupManager));
        _settings = AppSettingsRules.Normalize(initialSettings) with
        {
            StartWithWindows = startupManager.IsEnabled
        };

        VolumeUpCommand = new AsyncRelayCommand(() => ChangeVolumeAsync(5), () => CanAdjustVolume);
        VolumeDownCommand = new AsyncRelayCommand(() => ChangeVolumeAsync(-5), () => CanAdjustVolume);
        ToggleMuteCommand = new AsyncRelayCommand(ToggleMuteAsync, () => CanAdjustVolume);
        SetAsCurrentOutputCommand = new AsyncRelayCommand(SetAsCurrentOutputAsync, () => CanSetAsCurrentOutput);
        PreviousCommand = new AsyncRelayCommand(PreviousAsync, () => _mediaSnapshot.CanSkipPrevious);
        PlayPauseCommand = new AsyncRelayCommand(PlayPauseAsync, () => _mediaSnapshot.CanPlayPause);
        NextCommand = new AsyncRelayCommand(NextAsync, () => _mediaSnapshot.CanSkipNext);
        ToggleExpandedCommand = new AsyncRelayCommand(ToggleExpandedAsync, () => IsConnected);
        CollapseCommand = new AsyncRelayCommand(CollapseAsync, () => WidgetMode == WidgetDisplayMode.Expanded);
        CloseOfflineDetailsCommand = new AsyncRelayCommand(CloseOfflineDetailsAsync, () => WidgetMode == WidgetDisplayMode.OfflineDetails);
        RefreshConnectionCommand = new AsyncRelayCommand(() => RefreshAsync(_runtimeCancellation.Token));
        OpenBluetoothSettingsCommand = new AsyncRelayCommand(() => RaiseAndCompleteAsync(BluetoothSettingsRequested));
        OpenSettingsCommand = new AsyncRelayCommand(() => RaiseAndCompleteAsync(SettingsRequested));
        ResetPositionCommand = new AsyncRelayCommand(() => RaiseAndCompleteAsync(WindowPositionResetRequested));

        _monitor.ConnectionChanged += HandleConnectionChanged;
        ApplySnapshot(_monitor.Current);
        _ = MonitorRuntimeAsync(_runtimeCancellation.Token);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? SettingsRequested;
    public event EventHandler? WindowPositionResetRequested;
    public event EventHandler? PresentationSettingsChanged;
    public event EventHandler? BluetoothSettingsRequested;

    public string DeviceName { get => _deviceName; private set => SetField(ref _deviceName, value); }
    public string StatusText { get => _statusText; private set => SetField(ref _statusText, value); }
    public string DetailText { get => _detailText; private set => SetField(ref _detailText, value); }
    public string AudioSummaryText { get => _audioSummaryText; private set => SetField(ref _audioSummaryText, value); }
    public string NowPlayingText { get => _nowPlayingText; private set => SetField(ref _nowPlayingText, value); }
    public string TrackTitle { get => _trackTitle; private set => SetField(ref _trackTitle, value); }
    public string TrackArtist { get => _trackArtist; private set => SetField(ref _trackArtist, value); }
    public string MuteCommandText { get => _muteCommandText; private set => SetField(ref _muteCommandText, value); }
    public string PlayPauseCommandText { get => _playPauseCommandText; private set => SetField(ref _playPauseCommandText, value); }
    public string PlayPauseGlyph { get => _playPauseGlyph; private set => SetField(ref _playPauseGlyph, value); }
    public string ToggleExpandedCommandText => WidgetMode == WidgetDisplayMode.Expanded ? "收起" : "展开";
    public string LastCheckedText { get => _lastCheckedText; private set => SetField(ref _lastCheckedText, value); }
    public string BatteryText => "不可用";
    public string BatteryDetailText => "未发现标准电量服务 180F · 私有协议待验证";
    public string OptionalFeaturesText => "Bass 与灯效等待协议验证";
    public bool IsConnected { get => _isConnected; private set => SetField(ref _isConnected, value); }
    public bool IsMuted { get => _isMuted; private set => SetField(ref _isMuted, value); }
    public bool IsCurrentOutput { get => _isCurrentOutput; private set => SetField(ref _isCurrentOutput, value); }
    public bool HasMediaSession
    {
        get => _hasMediaSession;
        private set
        {
            if (!SetField(ref _hasMediaSession, value)) return;
            OnPropertyChanged(nameof(WidgetHeight));
        }
    }
    public double VolumePercent { get => _volumePercent; private set => SetField(ref _volumePercent, value); }

    public WidgetDisplayMode WidgetMode
    {
        get => _widgetMode;
        private set
        {
            if (!SetField(ref _widgetMode, value)) return;
            OnPropertyChanged(nameof(WidgetWidth));
            OnPropertyChanged(nameof(WidgetHeight));
            OnPropertyChanged(nameof(ToggleExpandedCommandText));
            CollapseCommand.NotifyCanExecuteChanged();
            CloseOfflineDetailsCommand.NotifyCanExecuteChanged();
        }
    }

    public double WidgetWidth => WidgetMode switch
    {
        WidgetDisplayMode.Offline => 66,
        WidgetDisplayMode.OfflineDetails => 304,
        WidgetDisplayMode.Expanded => 388,
        _ => 350
    };

    public double WidgetHeight => WidgetMode switch
    {
        WidgetDisplayMode.Offline => 66,
        WidgetDisplayMode.OfflineDetails => 156,
        WidgetDisplayMode.Expanded => HasMediaSession ? 432 : 250,
        _ => 125
    };

    public bool StartWithWindows
    {
        get => _settings.StartWithWindows;
        set
        {
            if (value != _settings.StartWithWindows) _ = SetStartWithWindowsAsync(value);
        }
    }

    public bool AutoCollapseOffline
    {
        get => _settings.AutoCollapseOffline;
        set => UpdateSettings(_settings with { AutoCollapseOffline = value }, nameof(AutoCollapseOffline), updateMode: true);
    }

    public bool AlwaysOnTop
    {
        get => _settings.AlwaysOnTop;
        set => UpdateSettings(_settings with { AlwaysOnTop = value }, nameof(AlwaysOnTop), presentationChanged: true);
    }

    public double WindowOpacity
    {
        get => _settings.Opacity;
        set => UpdateSettings(_settings with { Opacity = AppSettingsRules.NormalizeOpacity(value) }, nameof(WindowOpacity), presentationChanged: true);
    }

    public bool LowBatteryNotifications
    {
        get => _settings.LowBatteryNotifications;
        set => UpdateSettings(_settings with { LowBatteryNotifications = value }, nameof(LowBatteryNotifications));
    }

    public bool HandsFreeAdvanced
    {
        get => _settings.HandsFreeAdvanced;
        set => UpdateSettings(_settings with { HandsFreeAdvanced = value }, nameof(HandsFreeAdvanced));
    }

    public bool ExperimentalProtocol
    {
        get => _settings.ExperimentalProtocol;
        set => UpdateSettings(_settings with { ExperimentalProtocol = value }, nameof(ExperimentalProtocol));
    }

    public NormalizedWindowPlacement? WindowPlacement => _settings.WindowPlacement;
    public AsyncRelayCommand VolumeUpCommand { get; }
    public AsyncRelayCommand VolumeDownCommand { get; }
    public AsyncRelayCommand ToggleMuteCommand { get; }
    public AsyncRelayCommand SetAsCurrentOutputCommand { get; }
    public AsyncRelayCommand PreviousCommand { get; }
    public AsyncRelayCommand PlayPauseCommand { get; }
    public AsyncRelayCommand NextCommand { get; }
    public AsyncRelayCommand ToggleExpandedCommand { get; }
    public AsyncRelayCommand CollapseCommand { get; }
    public AsyncRelayCommand CloseOfflineDetailsCommand { get; }
    public AsyncRelayCommand RefreshConnectionCommand { get; }
    public AsyncRelayCommand OpenBluetoothSettingsCommand { get; }
    public AsyncRelayCommand OpenSettingsCommand { get; }
    public AsyncRelayCommand ResetPositionCommand { get; }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await _monitor.RefreshAsync(cancellationToken);
        await RunOnUiAsync(() => ApplySnapshot(_monitor.Current));
    }

    public Task ActivateWidgetAsync()
    {
        if (WidgetMode == WidgetDisplayMode.Offline)
        {
            _offlineDetailsRequested = true;
            UpdateWidgetMode();
            return Task.CompletedTask;
        }

        return WidgetMode == WidgetDisplayMode.Compact
            ? ToggleExpandedAsync()
            : Task.CompletedTask;
    }

    public Task CollapseWidgetAsync() => CollapseAsync();

    public Task CloseOfflineDetailsAsync()
    {
        _offlineDetailsRequested = false;
        UpdateWidgetMode();
        return Task.CompletedTask;
    }

    public async Task SetVolumeAsync(double value)
    {
        var device = GetConnectedDevice();
        if (device is null || !CanAdjustVolume) return;
        var result = await _audioController.SetVolumeAsync(device, value, _runtimeCancellation.Token);
        ShowAudioControlResult(result);
        await RefreshRuntimeSafelyAsync(_runtimeCancellation.Token);
    }

    public Task SaveWindowPlacementAsync(NormalizedWindowPlacement placement)
    {
        UpdateSettings(_settings with { WindowPlacement = placement }, propertyName: null);
        return Task.CompletedTask;
    }

    public void SetAlwaysOnTop(bool value) => AlwaysOnTop = value;

    public void Dispose()
    {
        if (_disposed) return;
        _monitor.ConnectionChanged -= HandleConnectionChanged;
        _runtimeCancellation.Cancel();
        _disposed = true;
    }

    private async Task SetStartWithWindowsAsync(bool value)
    {
        var applied = await _startupManager.SetEnabledAsync(value, _runtimeCancellation.Token);
        if (!applied || _disposed) return;
        await RunOnUiAsync(() => UpdateSettings(_settings with { StartWithWindows = value }, nameof(StartWithWindows)));
    }

    private Task ToggleExpandedAsync()
    {
        _expandedRequested = WidgetMode != WidgetDisplayMode.Expanded;
        UpdateSettings(_settings with { ManualCollapsed = !_expandedRequested }, propertyName: null);
        UpdateWidgetMode();
        return Task.CompletedTask;
    }

    private Task CollapseAsync()
    {
        _expandedRequested = false;
        UpdateSettings(_settings with { ManualCollapsed = true }, propertyName: null);
        UpdateWidgetMode();
        return Task.CompletedTask;
    }

    private Task RaiseAndCompleteAsync(EventHandler? handler)
    {
        handler?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    private void UpdateSettings(AppSettings settings, string? propertyName, bool updateMode = false, bool presentationChanged = false)
    {
        var normalized = AppSettingsRules.Normalize(settings);
        if (normalized == _settings) return;
        _settings = normalized;
        if (propertyName is not null) OnPropertyChanged(propertyName);
        if (updateMode) UpdateWidgetMode();
        if (presentationChanged) PresentationSettingsChanged?.Invoke(this, EventArgs.Empty);
        _ = PersistSettingsAsync();
    }

    private async Task PersistSettingsAsync()
    {
        var entered = false;
        try
        {
            await _settingsGate.WaitAsync(_runtimeCancellation.Token);
            entered = true;
            await _settingsStore.SaveAsync(_settings, _runtimeCancellation.Token);
        }
        catch (OperationCanceledException) when (_runtimeCancellation.IsCancellationRequested) { }
        finally
        {
            if (entered) _settingsGate.Release();
        }
    }

    private void HandleConnectionChanged(object? sender, SpeakerConnectionChangedEventArgs e)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            _ = dispatcher.BeginInvoke(() => ApplySnapshot(e.Snapshot));
            return;
        }
        ApplySnapshot(e.Snapshot);
    }

    private void ApplySnapshot(SpeakerConnectionSnapshot snapshot)
    {
        var wasConnected = IsConnected;
        _connectionState = snapshot.State;
        DeviceName = string.IsNullOrWhiteSpace(snapshot.DisplayName) ? "小米蓝牙音箱 Pro" : snapshot.DisplayName;
        IsConnected = snapshot.IsConnected;
        if (!snapshot.IsConnected) _expandedRequested = false;
        if (snapshot.IsConnected || wasConnected) _offlineDetailsRequested = false;
        LastCheckedText = $"{snapshot.ObservedAt.ToLocalTime():HH:mm:ss} 更新";
        (StatusText, DetailText) = snapshot.State switch
        {
            SpeakerConnectionState.BluetoothUnavailable => ("电脑蓝牙已关闭", "打开蓝牙后，组件会自动重新检测"),
            SpeakerConnectionState.PairedOffline when snapshot.DeviceKey is null => ("未找到目标音箱", "请确认音箱已与此电脑配对"),
            SpeakerConnectionState.PairedOffline => ("音箱未连接", "打开音箱后会自动恢复连接"),
            SpeakerConnectionState.Connected => ("已连接", "当前声音未输出到此音箱"),
            SpeakerConnectionState.ConnectedAsOutput => ("已连接 · 当前输出", "立体声输出 · A2DP"),
            _ => throw new ArgumentOutOfRangeException(nameof(snapshot))
        };
        UpdateWidgetMode();
        if (!snapshot.IsConnected)
        {
            _audioSnapshot = SpeakerAudioSnapshot.Unavailable(DateTimeOffset.UtcNow);
            _mediaSnapshot = SpeakerMediaSnapshot.Empty(DateTimeOffset.UtcNow);
            ApplyRuntimeSnapshots();
        }
        NotifyCommandsCanExecuteChanged();
        _ = RefreshRuntimeSafelyAsync(_runtimeCancellation.Token);
    }

    private void UpdateWidgetMode() => WidgetMode = WidgetStateRules.Resolve(
        _connectionState,
        _settings.AutoCollapseOffline,
        _expandedRequested,
        _offlineDetailsRequested);

    private bool CanAdjustVolume => IsConnected && _audioSnapshot.HasA2dpEndpoint && _audioSnapshot.VolumePercent.HasValue;
    private bool CanSetAsCurrentOutput => IsConnected && _audioSnapshot.HasA2dpEndpoint && !_audioSnapshot.IsCurrentOutput;

    private async Task MonitorRuntimeAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(750));
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                await RefreshRuntimeSafelyAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private async Task RefreshRuntimeSafelyAsync(CancellationToken cancellationToken)
    {
        var entered = false;
        try
        {
            entered = await _runtimeGate.WaitAsync(0, cancellationToken).ConfigureAwait(false);
            if (!entered) return;
            var device = GetConnectedDevice();
            if (device is null)
            {
                await RunOnUiAsync(() =>
                {
                    _audioSnapshot = SpeakerAudioSnapshot.Unavailable(DateTimeOffset.UtcNow);
                    _mediaSnapshot = SpeakerMediaSnapshot.Empty(DateTimeOffset.UtcNow);
                    ApplyRuntimeSnapshots();
                });
                return;
            }
            var audioTask = _audioController.GetSnapshotAsync(device, cancellationToken).AsTask();
            var mediaTask = _mediaController.GetSnapshotAsync(cancellationToken).AsTask();
            await Task.WhenAll(audioTask, mediaTask).ConfigureAwait(false);
            await RunOnUiAsync(() =>
            {
                _audioSnapshot = audioTask.Result;
                _mediaSnapshot = mediaTask.Result;
                ApplyRuntimeSnapshots();
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch
        {
            await RunOnUiAsync(() =>
            {
                _audioSnapshot = SpeakerAudioSnapshot.Unavailable(DateTimeOffset.UtcNow);
                _mediaSnapshot = SpeakerMediaSnapshot.Empty(DateTimeOffset.UtcNow);
                ApplyRuntimeSnapshots();
            });
        }
        finally
        {
            if (entered) _runtimeGate.Release();
        }
    }

    private void ApplyRuntimeSnapshots()
    {
        if (!IsConnected) AudioSummaryText = "音频控制将在连接后可用";
        else if (!_audioSnapshot.HasA2dpEndpoint || !_audioSnapshot.VolumePercent.HasValue) AudioSummaryText = "未找到可控的 A2DP 输出";
        else if (_audioSnapshot.IsMuted) AudioSummaryText = "已静音 · A2DP";
        else AudioSummaryText = $"音量 {Math.Round(_audioSnapshot.VolumePercent.Value):0}%{(_audioSnapshot.IsCurrentOutput ? " · 当前输出" : string.Empty)}";

        VolumePercent = _audioSnapshot.VolumePercent ?? 0;
        IsMuted = _audioSnapshot.IsMuted;
        IsCurrentOutput = _audioSnapshot.IsCurrentOutput;
        MuteCommandText = _audioSnapshot.IsMuted ? "取消静音" : "静音";
        PlayPauseCommandText = _mediaSnapshot.IsPlaying ? "暂停" : "播放";
        PlayPauseGlyph = _mediaSnapshot.IsPlaying ? "Ⅱ" : "▶";
        HasMediaSession = _mediaSnapshot.HasSession;
        NowPlayingText = !_mediaSnapshot.HasSession ? "无活动媒体" : FormatNowPlaying(_mediaSnapshot.TrackTitle, _mediaSnapshot.TrackArtist);
        TrackTitle = _mediaSnapshot.HasSession
            ? (string.IsNullOrWhiteSpace(_mediaSnapshot.TrackTitle) ? "活动媒体" : _mediaSnapshot.TrackTitle.Trim())
            : "无活动媒体";
        TrackArtist = _mediaSnapshot.HasSession
            ? (string.IsNullOrWhiteSpace(_mediaSnapshot.TrackArtist) ? "播放器未提供艺术家" : _mediaSnapshot.TrackArtist.Trim())
            : "等待播放器";
        NotifyCommandsCanExecuteChanged();
    }

    private SpeakerDevice? GetConnectedDevice()
    {
        var snapshot = _monitor.Current;
        return snapshot.IsConnected && !string.IsNullOrWhiteSpace(snapshot.DeviceKey) && !string.IsNullOrWhiteSpace(snapshot.DisplayName)
            ? new SpeakerDevice(snapshot.DeviceKey, snapshot.DisplayName, true, true, snapshot.ObservedAt)
            : null;
    }

    private Task ChangeVolumeAsync(double offset) => SetVolumeAsync((_audioSnapshot.VolumePercent ?? 50) + offset);

    private async Task ToggleMuteAsync()
    {
        var device = GetConnectedDevice();
        if (device is null) return;
        var result = await _audioController.SetMutedAsync(device, !_audioSnapshot.IsMuted, _runtimeCancellation.Token);
        ShowAudioControlResult(result);
        await RefreshRuntimeSafelyAsync(_runtimeCancellation.Token);
    }

    private async Task SetAsCurrentOutputAsync()
    {
        var device = GetConnectedDevice();
        if (device is null) return;
        var result = await _audioController.SetAsDefaultOutputAsync(device, _runtimeCancellation.Token);
        ShowAudioControlResult(result);
        if (result == AudioControlResult.Success)
        {
            await Task.Delay(250, _runtimeCancellation.Token);
            await _monitor.RefreshAsync(_runtimeCancellation.Token);
        }
        await RefreshRuntimeSafelyAsync(_runtimeCancellation.Token);
    }

    private async Task PreviousAsync() { await _mediaController.SkipPreviousAsync(_runtimeCancellation.Token); await RefreshRuntimeSafelyAsync(_runtimeCancellation.Token); }
    private async Task PlayPauseAsync() { await _mediaController.TogglePlayPauseAsync(_runtimeCancellation.Token); await RefreshRuntimeSafelyAsync(_runtimeCancellation.Token); }
    private async Task NextAsync() { await _mediaController.SkipNextAsync(_runtimeCancellation.Token); await RefreshRuntimeSafelyAsync(_runtimeCancellation.Token); }

    private void ShowAudioControlResult(AudioControlResult result)
    {
        AudioSummaryText = result switch
        {
            AudioControlResult.Success => "操作已应用",
            AudioControlResult.SpeakerOffline => "音箱当前离线",
            AudioControlResult.EndpointNotFound => "未找到 A2DP 输出",
            AudioControlResult.UserActionRequired => "请在声音设置中选择音箱",
            _ => "音频操作失败"
        };
    }

    private void NotifyCommandsCanExecuteChanged()
    {
        VolumeUpCommand.NotifyCanExecuteChanged();
        VolumeDownCommand.NotifyCanExecuteChanged();
        ToggleMuteCommand.NotifyCanExecuteChanged();
        SetAsCurrentOutputCommand.NotifyCanExecuteChanged();
        PreviousCommand.NotifyCanExecuteChanged();
        PlayPauseCommand.NotifyCanExecuteChanged();
        NextCommand.NotifyCanExecuteChanged();
        ToggleExpandedCommand.NotifyCanExecuteChanged();
    }

    private static string FormatNowPlaying(string? title, string? artist)
    {
        var displayTitle = string.IsNullOrWhiteSpace(title) ? "活动媒体" : title.Trim();
        return string.IsNullOrWhiteSpace(artist) ? displayTitle : $"{displayTitle} · {artist.Trim()}";
    }

    private static Task RunOnUiAsync(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }
        return dispatcher.InvokeAsync(action).Task;
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
