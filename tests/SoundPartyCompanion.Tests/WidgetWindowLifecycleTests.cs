using System.Windows;
using System.Windows.Threading;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Automation;
using System.Windows.Media;
using SoundPartyCompanion.App;
using SoundPartyCompanion.App.Infrastructure;
using SoundPartyCompanion.App.ViewModels;
using SoundPartyCompanion.Core.Audio;
using SoundPartyCompanion.Core.Devices;
using SoundPartyCompanion.Core.Media;
using SoundPartyCompanion.Core.Settings;
using Xunit;

namespace SoundPartyCompanion.Tests;

public sealed class WidgetWindowLifecycleTests
{
    [Fact]
    public void FullWpfLifecycleMaintainsDimensionsAndOneVisiblePanel()
    {
        Exception? failure = null;
        using var completed = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            try
            {
                RunLifecycle();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                completed.Set();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Assert.True(completed.Wait(TimeSpan.FromSeconds(20)), "WPF lifecycle test timed out.");
        thread.Join();
        if (failure is not null) throw new Xunit.Sdk.XunitException(failure.ToString());
    }

    private static void RunLifecycle()
    {
        SoundPartyCompanion.App.App? application = null;
        ConnectionStatusViewModel? viewModel = null;
        MainWindow? window = null;
        SettingsWindow? settingsWindow = null;
        try
        {
            application = new SoundPartyCompanion.App.App();
            application.InitializeComponent();
            var monitor = new FakeConnectionMonitor(Snapshot(SpeakerConnectionState.PairedOffline));
            var mediaController = new FakeMediaController();
            viewModel = new ConnectionStatusViewModel(
                monitor,
                new FakeAudioController(),
                mediaController,
                new MemorySettingsStore(),
                new FakeStartupManager(),
                AppSettings.Default with { StartWithWindows = false });
            window = new MainWindow(viewModel, new WindowPlacementService()) { ShowActivated = false };

            window.Show();
            PumpDispatcher();
            AssertState(window, viewModel, WidgetDisplayMode.Offline, 66, 66);
            AssertContextMenuHeaders(window, "重新检测连接", "打开蓝牙设置", "设置");

            var bluetoothSettingsRequests = 0;
            viewModel.BluetoothSettingsRequested += (_, _) => bluetoothSettingsRequests++;
            viewModel.ActivateWidgetAsync().GetAwaiter().GetResult();
            PumpDispatcherFor(TimeSpan.FromMilliseconds(260));
            AssertState(window, viewModel, WidgetDisplayMode.OfflineDetails, 304, 156);
            Assert.Equal(0, bluetoothSettingsRequests);

            viewModel.OpenBluetoothSettingsCommand.Execute(null);
            PumpDispatcher();
            Assert.Equal(1, bluetoothSettingsRequests);

            viewModel.CloseOfflineDetailsAsync().GetAwaiter().GetResult();
            PumpDispatcherFor(TimeSpan.FromMilliseconds(220));
            AssertState(window, viewModel, WidgetDisplayMode.Offline, 66, 66);

            monitor.SetSnapshot(Snapshot(SpeakerConnectionState.Connected));
            PumpDispatcherFor(TimeSpan.FromMilliseconds(320));
            AssertState(window, viewModel, WidgetDisplayMode.Compact, 350, 125);

            viewModel.ActivateWidgetAsync().GetAwaiter().GetResult();
            PumpDispatcherFor(TimeSpan.FromMilliseconds(320));
            AssertState(window, viewModel, WidgetDisplayMode.Expanded, 388, 250);
            Assert.Equal(Visibility.Collapsed, ((Border)window.FindName("ActiveMediaPanel")).Visibility);
            Assert.Equal("收起组件", AutomationProperties.GetName(
                FindVisualDescendants<Button>(window).Single(button => Equals(button.Content, "‹"))));
            Assert.Equal("打开设置", AutomationProperties.GetName(
                FindVisualDescendants<Button>(window).Single(button => Equals(button.Content, "···"))));
            AssertContextMenuHeaders(window, "收起", "上一首", "播放", "下一首", "音量 +5",
                "音量 -5", "静音", "设为当前输出", "重新检测连接", "打开蓝牙设置", "设置");

            const string longTitle = "一想到这么好的萍萍会下线，真的让人舍不得但标题还要继续变长";
            mediaController.Current = new SpeakerMediaSnapshot(
                "test-session", longTitle, "一个同样很长的艺术家名称",
                MediaPlaybackState.Paused, CanPlayPause: true, CanSkipPrevious: true, CanSkipNext: true,
                DateTimeOffset.UtcNow);
            PumpDispatcherFor(TimeSpan.FromMilliseconds(900));
            AssertState(window, viewModel, WidgetDisplayMode.Expanded, 388, 432);
            Assert.Equal(Visibility.Visible, ((Border)window.FindName("ActiveMediaPanel")).Visibility);
            var title = FindVisualDescendants<TextBlock>(window).Single(text => Equals(text.Text, longTitle));
            Assert.Equal(longTitle, title.ToolTip);

            mediaController.Current = SpeakerMediaSnapshot.Empty(DateTimeOffset.UtcNow);
            PumpDispatcherFor(TimeSpan.FromMilliseconds(900));
            AssertState(window, viewModel, WidgetDisplayMode.Expanded, 388, 250);
            Assert.Equal(Visibility.Collapsed, ((Border)window.FindName("ActiveMediaPanel")).Visibility);

            viewModel.CollapseWidgetAsync().GetAwaiter().GetResult();
            PumpDispatcherFor(TimeSpan.FromMilliseconds(320));
            AssertState(window, viewModel, WidgetDisplayMode.Compact, 350, 125);

            for (var cycle = 0; cycle < 30; cycle++)
            {
                viewModel.ActivateWidgetAsync().GetAwaiter().GetResult();
                viewModel.CollapseWidgetAsync().GetAwaiter().GetResult();
            }
            PumpDispatcherFor(TimeSpan.FromMilliseconds(360));
            AssertState(window, viewModel, WidgetDisplayMode.Compact, 350, 125);

            monitor.SetSnapshot(Snapshot(SpeakerConnectionState.PairedOffline));
            PumpDispatcherFor(TimeSpan.FromMilliseconds(280));
            AssertState(window, viewModel, WidgetDisplayMode.Offline, 66, 66);

            monitor.SetSnapshot(Snapshot(SpeakerConnectionState.ConnectedAsOutput));
            PumpDispatcherFor(TimeSpan.FromMilliseconds(320));
            AssertState(window, viewModel, WidgetDisplayMode.Compact, 350, 125);

            settingsWindow = new SettingsWindow(viewModel)
            {
                Owner = window,
                ShowActivated = false
            };
            settingsWindow.Show();
            PumpDispatcher();
            AssertSettingsWindowLayout(settingsWindow);
        }
        finally
        {
            settingsWindow?.Close();
            if (window is not null)
            {
                window.AllowClose = true;
                window.Close();
            }
            viewModel?.Dispose();
            application?.Shutdown();
        }
    }

    private static void AssertSettingsWindowLayout(SettingsWindow settingsWindow)
    {
        settingsWindow.UpdateLayout();
        var scrollViewer = FindVisualDescendants<ScrollViewer>(settingsWindow).Single(viewer =>
            viewer.Name == "SettingsScrollViewer");
        var footerButton = FindVisualDescendants<Button>(settingsWindow).Single(button =>
            Equals(button.Content, "完成"));
        var scrollBar = FindVisualDescendants<ScrollBar>(scrollViewer).Single(bar =>
            bar.Orientation == Orientation.Vertical);
        var thumb = FindVisualDescendants<Thumb>(scrollBar).Single();
        var footerY = footerButton.TranslatePoint(new System.Windows.Point(), settingsWindow).Y;

        Assert.True(scrollViewer.ScrollableHeight > 0);
        Assert.Equal(10, scrollBar.Width);
        Assert.Equal(6, thumb.Width);
        Assert.Empty(FindVisualDescendants<System.Windows.Shapes.Path>(scrollBar));

        scrollViewer.ScrollToEnd();
        settingsWindow.UpdateLayout();
        var footerAfterScrollY = footerButton.TranslatePoint(new System.Windows.Point(), settingsWindow).Y;
        Assert.InRange(Math.Abs(footerAfterScrollY - footerY), 0, 0.1);
        Assert.True(footerButton.IsVisible);
    }

    private static void AssertContextMenuHeaders(Window window, params string[] expected)
    {
        var menu = window.ContextMenu!;
        menu.PlacementTarget = window;
        menu.IsOpen = true;
        PumpDispatcher();
        var actual = menu.Items.OfType<MenuItem>()
            .Where(item => item.Visibility == Visibility.Visible)
            .Select(item => item.Header?.ToString())
            .Where(header => header is not null)
            .Cast<string>()
            .ToArray();
        menu.IsOpen = false;
        Assert.Equal(expected, actual);
    }

    private static IEnumerable<T> FindVisualDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (var descendant in FindVisualDescendants<T>(child)) yield return descendant;
        }
    }

    private static void AssertState(
        MainWindow window,
        ConnectionStatusViewModel viewModel,
        WidgetDisplayMode expectedMode,
        double expectedWidth,
        double expectedHeight)
    {
        window.UpdateLayout();
        Assert.Equal(expectedMode, viewModel.WidgetMode);
        Assert.InRange(window.ActualWidth, expectedWidth - 0.5, expectedWidth + 0.5);
        Assert.InRange(window.ActualHeight, expectedHeight - 0.5, expectedHeight + 0.5);
        Assert.True(window.HasExpectedDimensions);
        Assert.Equal(1, window.VisibleStatePanelCount);
    }

    private static void PumpDispatcher()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static void PumpDispatcherFor(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle)
        {
            Interval = duration
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            frame.Continue = false;
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private static SpeakerConnectionSnapshot Snapshot(SpeakerConnectionState state) => new(
        state,
        "test-device",
        "Xiaomi Sound Party",
        DateTimeOffset.UtcNow);

    private sealed class FakeConnectionMonitor(SpeakerConnectionSnapshot current) : IConnectionMonitor
    {
        public event EventHandler<SpeakerConnectionChangedEventArgs>? ConnectionChanged;
        public SpeakerConnectionSnapshot Current { get; private set; } = current;
        public void SetSnapshot(SpeakerConnectionSnapshot snapshot)
        {
            Current = snapshot;
            ConnectionChanged?.Invoke(this, new SpeakerConnectionChangedEventArgs(snapshot));
        }
        public Task StartAsync(SpeakerSelection? selection, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SetSelectionAsync(SpeakerSelection? selection, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeAudioController : IAudioEndpointController
    {
        public ValueTask<SpeakerAudioSnapshot> GetSnapshotAsync(SpeakerDevice device, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(SpeakerAudioSnapshot.Unavailable(DateTimeOffset.UtcNow));
        public ValueTask<AudioControlResult> SetVolumeAsync(SpeakerDevice device, double volumePercent, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(AudioControlResult.Success);
        public ValueTask<AudioControlResult> SetMutedAsync(SpeakerDevice device, bool isMuted, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(AudioControlResult.Success);
        public ValueTask<AudioControlResult> SetAsDefaultOutputAsync(SpeakerDevice device, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(AudioControlResult.Success);
    }

    private sealed class FakeMediaController : IMediaSessionController
    {
        public SpeakerMediaSnapshot Current { get; set; } = SpeakerMediaSnapshot.Empty(DateTimeOffset.UtcNow);

        public ValueTask<SpeakerMediaSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Current);
        public ValueTask<bool> TogglePlayPauseAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(true);
        public ValueTask<bool> SkipPreviousAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(true);
        public ValueTask<bool> SkipNextAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(true);
    }

    private sealed class MemorySettingsStore : IAppSettingsStore
    {
        public ValueTask<AppSettings> LoadAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(AppSettings.Default);
        public ValueTask SaveAsync(AppSettings settings, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class FakeStartupManager : IStartupManager
    {
        public bool IsEnabled => false;
        public ValueTask<bool> SetEnabledAsync(bool isEnabled, CancellationToken cancellationToken = default) => ValueTask.FromResult(true);
    }
}
