using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using SoundPartyCompanion.App.Infrastructure;
using SoundPartyCompanion.App.ViewModels;
using SoundPartyCompanion.Core.Devices;
using SoundPartyCompanion.Core.Diagnostics;
using SoundPartyCompanion.Core.Settings;
using SoundPartyCompanion.Windows.Audio;
using SoundPartyCompanion.Windows.Bluetooth;
using SoundPartyCompanion.Windows.Media;
using SoundPartyCompanion.Windows.Startup;
using SoundPartyCompanion.Windows.Storage;

namespace SoundPartyCompanion.App;

public partial class App : System.Windows.Application
{
    private const string SingleInstanceId = "SoundPartyCompanion.App.v1";
    private SingleInstanceCoordinator? _singleInstance;
    private IAppLogger? _logger;
    private IConnectionMonitor? _connectionMonitor;
    private ConnectionStatusViewModel? _connectionViewModel;
    private MainWindow? _widgetWindow;
    private SettingsWindow? _settingsWindow;
    private TrayIconService? _trayIcon;
    private bool _activationPending;
    private bool _exiting;
    private bool _exportingDiagnostics;
    private string? _logDirectory;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _singleInstance = SingleInstanceCoordinator.TryAcquire(
            SingleInstanceId,
            QueueExistingInstanceActivation);
        if (_singleInstance is null)
        {
            Shutdown();
            return;
        }

        var appDataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SoundPartyCompanion");
        _logDirectory = Path.Combine(appDataDirectory, "Logs");
        _logger = new RollingFileLogger(_logDirectory);
        DispatcherUnhandledException += HandleDispatcherException;
        AppDomain.CurrentDomain.UnhandledException += HandleDomainException;
        TaskScheduler.UnobservedTaskException += HandleTaskException;
        _logger.Write(AppLogLevel.Information, "Application starting.");

        var discovery = new WindowsBluetoothDiscovery(_logger);
        var batteryReader = new WindowsSpeakerBatteryReader();
        var audioController = new WindowsAudioEndpointController(_logger);
        var mediaController = new WindowsMediaSessionController(_logger);
        var outputProbe = new WindowsDefaultOutputProbe(audioController, _logger);
        var selectionStore = new LocalSpeakerSelectionStore(_logger);
        var settingsStore = new LocalAppSettingsStore(_logger);
        var initialSettings = await settingsStore.LoadAsync();
        var executablePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Application executable path is unavailable.");
        var startupManager = new WindowsStartupManager(executablePath, _logger);
        if (startupManager.IsEnabled != initialSettings.StartWithWindows)
        {
            await startupManager.SetEnabledAsync(initialSettings.StartWithWindows);
        }

        _connectionMonitor = new SpeakerConnectionMonitor(
            discovery,
            outputProbe,
            TimeSpan.FromSeconds(3),
            _logger,
            batteryReader);
        _connectionViewModel = new ConnectionStatusViewModel(
            _connectionMonitor,
            audioController,
            mediaController,
            settingsStore,
            startupManager,
            initialSettings);
        _connectionViewModel.SettingsRequested += HandleSettingsRequested;
        _connectionViewModel.PresentationSettingsChanged += HandlePresentationSettingsChanged;
        _connectionViewModel.BluetoothSettingsRequested += HandleBluetoothSettingsRequested;
        _connectionViewModel.LowBatteryReminderRequested += HandleLowBatteryReminderRequested;
        _connectionViewModel.PropertyChanged += HandleConnectionViewModelPropertyChanged;

        _widgetWindow = new MainWindow(_connectionViewModel, new WindowPlacementService());
        MainWindow = _widgetWindow;
        _widgetWindow.IsVisibleChanged += HandleWidgetVisibilityChanged;
        _widgetWindow.Show();
        if (_activationPending)
        {
            _activationPending = false;
            ShowExistingInstance();
        }

        _trayIcon = new TrayIconService(
            ToggleWidgetVisibility,
            value => _connectionViewModel.SetAlwaysOnTop(value),
            () => _connectionViewModel.SetAsCurrentOutputCommand.Execute(null),
            ShowSettings,
            OpenBluetoothSettings,
            ExportDiagnostics,
            ExitApplication);
        UpdateTrayIcon();

        var savedSelection = await selectionStore.LoadAsync();
        var initialDiscovery = await discovery.DiscoverAsync();
        var selection = SpeakerDeviceSelector.Select(
            initialDiscovery,
            savedSelection,
            ["Xiaomi蓝牙音箱Pro", "Xiaomi 蓝牙音箱 Pro", "Xiaomi Sound Party"]);
        if (selection is not null && selection != savedSelection)
        {
            await selectionStore.SaveAsync(selection);
        }
        await _connectionMonitor.StartAsync(selection);
        _connectionViewModel.CheckLowBatteryReminder();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _exiting = true;
        _singleInstance?.Dispose();
        _singleInstance = null;
        _trayIcon?.Dispose();
        if (_connectionViewModel is not null)
        {
            _connectionViewModel.SettingsRequested -= HandleSettingsRequested;
            _connectionViewModel.PresentationSettingsChanged -= HandlePresentationSettingsChanged;
            _connectionViewModel.BluetoothSettingsRequested -= HandleBluetoothSettingsRequested;
            _connectionViewModel.LowBatteryReminderRequested -= HandleLowBatteryReminderRequested;
            _connectionViewModel.PropertyChanged -= HandleConnectionViewModelPropertyChanged;
            _connectionViewModel.Dispose();
        }
        if (_connectionMonitor is not null)
        {
            _connectionMonitor.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        _logger?.Write(AppLogLevel.Information, $"Application exiting with code {e.ApplicationExitCode}.");
        base.OnExit(e);
    }

    private void ToggleWidgetVisibility()
    {
        if (_widgetWindow is null) return;
        if (_widgetWindow.IsVisible) _widgetWindow.Hide();
        else _widgetWindow.ShowFromTray();
        UpdateTrayIcon();
    }

    private void QueueExistingInstanceActivation()
    {
        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;
        _ = Dispatcher.InvokeAsync(ShowExistingInstance);
    }

    private void ShowExistingInstance()
    {
        if (_exiting) return;
        if (_widgetWindow is null)
        {
            _activationPending = true;
            return;
        }
        _widgetWindow.ShowFromTray();
        UpdateTrayIcon();
    }

    private void ShowSettings()
    {
        if (_connectionViewModel is null || _widgetWindow is null) return;
        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow(_connectionViewModel) { Owner = _widgetWindow };
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        }
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private static void OpenBluetoothSettings()
    {
        Process.Start(new ProcessStartInfo("ms-settings:bluetooth") { UseShellExecute = true });
    }

    private async void ExportDiagnostics()
    {
        if (_exportingDiagnostics || _exiting || _logDirectory is null ||
            _connectionMonitor is null || _connectionViewModel is null || _widgetWindow is null) return;
        _exportingDiagnostics = true;
        _trayIcon?.SetDiagnosticExportEnabled(false);
        var owner = _settingsWindow is { IsVisible: true } ? _settingsWindow : (Window)_widgetWindow;
        try
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "导出本地脱敏诊断包（不会上传）",
                Filter = "诊断 ZIP 文件 (*.zip)|*.zip",
                DefaultExt = ".zip",
                AddExtension = true,
                FileName = $"SoundPartyCompanion-Diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.zip",
                InitialDirectory = Directory.Exists(@"E:\") ? @"E:\" : AppContext.BaseDirectory,
                OverwritePrompt = false,
                CheckPathExists = true
            };
            if (dialog.ShowDialog(owner) != true) return;
            if (File.Exists(dialog.FileName))
            {
                System.Windows.MessageBox.Show(owner, "该文件已存在。为保护原文件，请重新导出并使用不同文件名。",
                    "导出诊断", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var connection = _connectionMonitor.Current;
            var hasAudioControl = _connectionViewModel.ToggleMuteCommand.CanExecute(null);
            var snapshot = new DiagnosticExportSnapshot(
                typeof(App).Assembly.GetName().Version ?? new Version(0, 1, 0),
                Environment.OSVersion.Version,
                RuntimeInformation.ProcessArchitecture,
                connection.State,
                connection.Battery.Percent,
                connection.Battery.IsStale,
                connection.Battery.ObservedAt,
                hasAudioControl ? _connectionViewModel.VolumePercent : null,
                hasAudioControl ? _connectionViewModel.IsMuted : null);
            var logDirectory = _logDirectory;
            var destination = dialog.FileName;
            var result = await Task.Run(() => new DiagnosticExportService().ExportAsync(logDirectory, destination, snapshot));
            _logger?.Write(AppLogLevel.Information, "Diagnostic export completed.");
            if (_exiting) return;
            var summary = result.SkippedLogs > 0
                ? $"已导出 {result.IncludedLogs} 份脱敏日志；{result.SkippedLogs} 份不可读日志已跳过。"
                : $"已导出 {result.IncludedLogs} 份脱敏日志与基础状态。";
            System.Windows.MessageBox.Show(owner, summary + "\n文件仅保存在你选择的位置，分享前请检查内容。",
                "导出诊断完成", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            _logger?.Write(AppLogLevel.Warning, $"Diagnostic export failed ({exception.GetType().Name}).");
            if (!_exiting)
                System.Windows.MessageBox.Show(owner, "导出未完成，请选择可写位置和未使用的文件名后重试。基础功能不受影响。",
                    "导出诊断", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _exportingDiagnostics = false;
            _trayIcon?.SetDiagnosticExportEnabled(true);
        }
    }

    private void ExitApplication()
    {
        if (_exiting) return;
        _exiting = true;
        _settingsWindow?.Close();
        if (_widgetWindow is not null)
        {
            _widgetWindow.AllowClose = true;
            _widgetWindow.Close();
        }
        Shutdown();
    }

    private void HandleSettingsRequested(object? sender, EventArgs e) => ShowSettings();

    private void HandleBluetoothSettingsRequested(object? sender, EventArgs e) => OpenBluetoothSettings();

    private void HandleLowBatteryReminderRequested(object? sender, LowBatteryReminderEventArgs e)
    {
        _trayIcon?.ShowLowBatteryReminder(
            _connectionViewModel?.DeviceName ?? "Xiaomi蓝牙音箱Pro",
            e.Percent,
            e.Threshold);
    }

    private void HandlePresentationSettingsChanged(object? sender, EventArgs e) => UpdateTrayIcon();

    private void HandleConnectionViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ConnectionStatusViewModel.DeviceName)
            or nameof(ConnectionStatusViewModel.StatusText)
            or nameof(ConnectionStatusViewModel.BatteryText)
            or nameof(ConnectionStatusViewModel.BatteryDetailText)
            or nameof(ConnectionStatusViewModel.HasBattery)
            or nameof(ConnectionStatusViewModel.IsConnected)
            or nameof(ConnectionStatusViewModel.IsCurrentOutput)
            or nameof(ConnectionStatusViewModel.AudioSummaryText))
        {
            UpdateTrayIcon();
        }
    }

    private void HandleWidgetVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e) => UpdateTrayIcon();

    private void UpdateTrayIcon()
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.InvokeAsync(UpdateTrayIcon);
            return;
        }
        if (_widgetWindow is null || _connectionViewModel is null) return;
        _trayIcon?.Update(new TrayMenuState(
            _widgetWindow.IsVisible,
            _connectionViewModel.AlwaysOnTop,
            _connectionViewModel.DeviceName,
            _connectionViewModel.StatusText,
            _connectionViewModel.BatteryText,
            _connectionViewModel.BatteryDetailText,
            _connectionViewModel.HasBattery,
            _connectionViewModel.IsConnected,
            _connectionViewModel.IsCurrentOutput,
            _connectionViewModel.SetAsCurrentOutputCommand.CanExecute(null)));
    }

    private void HandleDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _logger?.Write(AppLogLevel.Critical, "Unhandled UI exception.", e.Exception);
        e.Handled = true;
        System.Windows.MessageBox.Show(
            "应用遇到无法恢复的错误，详细信息已写入本地日志。",
            "Sound Party 桌面伴侣",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        ExitApplication();
    }

    private void HandleDomainException(object? sender, UnhandledExceptionEventArgs e)
    {
        _logger?.Write(AppLogLevel.Critical, "Unhandled application-domain exception.", e.ExceptionObject as Exception);
    }

    private void HandleTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        _logger?.Write(AppLogLevel.Error, "Unobserved task exception.", e.Exception);
        e.SetObserved();
    }
}
