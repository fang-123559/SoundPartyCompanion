using System.Windows;
using System.Windows.Threading;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
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
        System.Windows.Application? application = null;
        ConnectionStatusViewModel? viewModel = null;
        MainWindow? window = null;
        SettingsWindow? settingsWindow = null;
        try
        {
            application = new System.Windows.Application
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown
            };
            var monitor = new FakeConnectionMonitor(Snapshot(SpeakerConnectionState.PairedOffline));
            var mediaController = new FakeMediaController();
            var settingsStore = new MemorySettingsStore();
            viewModel = new ConnectionStatusViewModel(
                monitor,
                new FakeAudioController(),
                mediaController,
                settingsStore,
                new FakeStartupManager(),
                AppSettings.Default with { StartWithWindows = false });
            window = new MainWindow(viewModel, new WindowPlacementService());
            application.MainWindow = window;

            window.Show();
            PumpDispatcher();
            Assert.NotNull(window.Icon);
            Assert.False(window.ShowInTaskbar);
            AssertState(window, viewModel, WidgetDisplayMode.Offline, 66, 66);
            Assert.Null(((Border)window.FindName("WidgetChrome")).Effect);
            Assert.True(window.IsVisible, "Widget must be visible before context-menu inspection.");
            AssertContextMenuHeaders(window, "重新检测连接", "打开蓝牙设置", "设置");
            Assert.True(window.IsVisible, "Widget must remain visible after its context menu closes.");

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
            var railBrand = (TextBlock)window.FindName("RailBrandLabel");
            Assert.Equal(11, railBrand.FontSize);
            Assert.Equal(FontWeights.Medium, railBrand.FontWeight);
            Assert.Equal(byte.MaxValue, ((SolidColorBrush)railBrand.Foreground).Color.A);
            Assert.False(railBrand.IsHitTestVisible);
            Assert.Equal(90, ((RotateTransform)railBrand.LayoutTransform).Angle);
            Assert.Equal(Visibility.Collapsed, ((Border)window.FindName("ActiveMediaPanel")).Visibility);
            Assert.Equal("收起组件", AutomationProperties.GetName(
                FindVisualDescendants<Button>(window).Single(button => Equals(button.Content, "‹"))));
            Assert.Equal("打开设置", AutomationProperties.GetName(
                FindVisualDescendants<Button>(window).Single(button =>
                    AutomationProperties.GetName(button) == "打开设置")));
            Assert.Contains(
                FindVisualDescendants<Button>(window),
                button => AutomationProperties.GetName(button) == "静音" && button.Content is Canvas);
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
            AssertSettingsWindowLayout(settingsWindow, viewModel, settingsStore);
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

    private static void AssertSettingsWindowLayout(
        SettingsWindow settingsWindow,
        ConnectionStatusViewModel viewModel,
        MemorySettingsStore settingsStore)
    {
        settingsWindow.UpdateLayout();
        Assert.NotNull(settingsWindow.Icon);
        Assert.Equal(settingsWindow.Owner!.Icon.ToString(), settingsWindow.Icon.ToString());
        Assert.True(settingsWindow.ShowInTaskbar);
        var surface = (Border)settingsWindow.FindName("SettingsSurface");
        Assert.Equal(388, surface.ActualWidth);
        Assert.Equal(448, surface.ActualHeight);
        var scrollViewer = FindVisualDescendants<ScrollViewer>(settingsWindow).Single(viewer =>
            viewer.Name == "SettingsScrollViewer");
        var closeButton = (Button)settingsWindow.FindName("CloseSettingsButton");
        var saveHint = (TextBlock)settingsWindow.FindName("SettingsSaveHint");
        var scrollBar = FindVisualDescendants<ScrollBar>(scrollViewer).Single(bar =>
            bar.Orientation == Orientation.Vertical);
        var closeY = closeButton.TranslatePoint(new System.Windows.Point(), settingsWindow).Y;

        AssertNoCapabilitySection(settingsWindow);
        Assert.InRange(scrollViewer.ScrollableHeight, 0, 0.1);
        AssertSettingsContentSpacing(settingsWindow);
        Assert.DoesNotContain(FindVisualDescendants<Button>(settingsWindow), button => Equals(button.Content, "完成"));
        Assert.Equal("关闭设置", AutomationProperties.GetName(closeButton));
        Assert.Equal("本地设置 · 自动保存", saveHint.Text);
        AssertSettingsBindings(settingsWindow, viewModel, settingsStore);

        settingsWindow.Height = 408;
        settingsWindow.UpdateLayout();
        Assert.True(scrollViewer.ScrollableHeight > 0, "A reduced viewport must scroll instead of clipping settings.");
        Assert.Equal(Visibility.Visible, scrollBar.Visibility);
        var thumb = FindVisualDescendants<Thumb>(scrollBar).Single();
        Assert.Equal(10, scrollBar.Width);
        Assert.Equal(6, thumb.Width);
        Assert.Empty(FindVisualDescendants<System.Windows.Shapes.Path>(scrollBar));
        var footerY = saveHint.TranslatePoint(new System.Windows.Point(), settingsWindow).Y;

        scrollViewer.ScrollToEnd();
        settingsWindow.UpdateLayout();
        var footerAfterScrollY = saveHint.TranslatePoint(new System.Windows.Point(), settingsWindow).Y;
        Assert.InRange(Math.Abs(footerAfterScrollY - footerY), 0, 0.1);
        Assert.InRange(Math.Abs(closeButton.TranslatePoint(new System.Windows.Point(), settingsWindow).Y - closeY), 0, 0.1);
        Assert.True(saveHint.IsVisible);
        Assert.True(closeButton.IsVisible);

        Assert.True(saveHint.TranslatePoint(new System.Windows.Point(), settingsWindow).Y + saveHint.ActualHeight <= surface.Margin.Top + surface.ActualHeight);
        settingsWindow.Height = 476;
        settingsWindow.UpdateLayout();
        Assert.InRange(scrollViewer.ScrollableHeight, 0, 0.1);
        Assert.InRange(scrollViewer.VerticalOffset, 0, 0.1);
        AssertSettingsContentSpacing(settingsWindow);

        ((IInvokeProvider)new ButtonAutomationPeer(closeButton).GetPattern(PatternInterface.Invoke)).Invoke();
        PumpDispatcher();
        Assert.False(settingsWindow.IsVisible);
        var reopened = new SettingsWindow(viewModel) { Owner = settingsWindow.Owner, ShowActivated = false };
        try
        {
            reopened.Show();
            PumpDispatcher();
            AssertNoCapabilitySection(reopened);
            AssertSettingsContentSpacing(reopened);
            Assert.Equal(viewModel.LowBatteryNotifications, ((CheckBox)reopened.FindName("LowBatteryToggle")).IsChecked);
        }
        finally
        {
            reopened.Close();
        }
    }

    private static void AssertSettingsContentSpacing(SettingsWindow settingsWindow)
    {
        settingsWindow.UpdateLayout();
        var hint = (TextBlock)settingsWindow.FindName("LowBatteryHint");
        var footer = (Border)settingsWindow.FindName("SettingsFooter");
        var scrollViewer = (ScrollViewer)settingsWindow.FindName("SettingsScrollViewer");
        var hintBottom = hint.TranslatePoint(new System.Windows.Point(0, hint.ActualHeight), settingsWindow).Y;
        var footerTop = footer.TranslatePoint(new System.Windows.Point(), settingsWindow).Y;
        Assert.InRange(footerTop - hintBottom, 12, 24);
        Assert.True(hintBottom <= scrollViewer.TranslatePoint(new System.Windows.Point(0, scrollViewer.ActualHeight), settingsWindow).Y,
            "The low-battery explanation must fit above the footer without clipping.");
        Assert.InRange(scrollViewer.ScrollableHeight, 0, 0.1);
    }

    private static void AssertNoCapabilitySection(SettingsWindow settingsWindow)
    {
        Assert.Null(settingsWindow.FindName("DeviceCapabilities"));
        Assert.Empty(FindVisualDescendants<Expander>(settingsWindow));
        var visibleText = string.Join("\n", FindVisualDescendants<TextBlock>(settingsWindow).Select(block => block.Text));
        foreach (var capabilityLabel in new[] { "设备能力", "Windows HFP", "充电识别", "Bass", "灯效" })
        {
            Assert.DoesNotContain(capabilityLabel, visibleText);
        }
    }

    private static void AssertSettingsBindings(
        SettingsWindow settingsWindow,
        ConnectionStatusViewModel viewModel,
        MemorySettingsStore settingsStore)
    {
        var toggles = new[]
        {
            ("StartupToggle", new Func<bool>(() => viewModel.StartWithWindows)),
            ("OfflineCollapseToggle", new Func<bool>(() => viewModel.AutoCollapseOffline)),
            ("AlwaysOnTopToggle", new Func<bool>(() => viewModel.AlwaysOnTop)),
            ("LowBatteryToggle", new Func<bool>(() => viewModel.LowBatteryNotifications))
        };
        foreach (var (name, readSetting) in toggles)
        {
            var toggle = (CheckBox)settingsWindow.FindName(name);
            var original = readSetting();
            Assert.Equal(original, toggle.IsChecked);
            Assert.True(toggle.Focusable && toggle.IsTabStop);
            var track = (Border)toggle.Template.FindName("SwitchTrack", toggle);
            var knob = (System.Windows.Shapes.Ellipse)toggle.Template.FindName("SwitchKnob", toggle);
            Assert.True(track.TranslatePoint(new System.Windows.Point(), toggle).X > toggle.ActualWidth / 2);
            var provider = (IToggleProvider)new CheckBoxAutomationPeer(toggle).GetPattern(PatternInterface.Toggle);
            provider.Toggle();
            PumpDispatcher();
            Assert.Equal(!original, readSetting());
            Assert.Equal(!original ? HorizontalAlignment.Right : HorizontalAlignment.Left, knob.HorizontalAlignment);
            Assert.NotNull(settingsStore.LastSaved);
            provider.Toggle();
            PumpDispatcher();
            Assert.Equal(original, readSetting());
        }

        var opacitySlider = (Slider)settingsWindow.FindName("OpacitySlider");
        var originalOpacity = viewModel.WindowOpacity;
        opacitySlider.SetCurrentValue(Slider.ValueProperty, 0.85);
        PumpDispatcher();
        Assert.Equal(0.85, viewModel.WindowOpacity, 2);
        Assert.Equal(0.85, settingsStore.LastSaved!.Opacity, 2);
        opacitySlider.SetCurrentValue(Slider.ValueProperty, originalOpacity);
        PumpDispatcher();
    }

    private static void AssertContextMenuHeaders(Window window, params string[] expected)
    {
        var menu = window.ContextMenu!;
        menu.PlacementTarget = window;
        menu.DataContext = window.DataContext;
        menu.ApplyTemplate();
        PumpDispatcher();
        var actual = menu.Items.OfType<MenuItem>()
            .Where(item => item.Visibility == Visibility.Visible)
            .Select(item => item.Header?.ToString())
            .Where(header => header is not null)
            .Cast<string>()
            .ToArray();
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
        Assert.True(
            window.ActualWidth >= expectedWidth - 0.5 && window.ActualWidth <= expectedWidth + 0.5,
            $"Width mismatch: expected={expectedWidth}, Width={window.Width}, ActualWidth={window.ActualWidth}, " +
            $"ViewModelWidth={viewModel.WidgetWidth}, IsLoaded={window.IsLoaded}, IsVisible={window.IsVisible}.");
        Assert.True(
            window.ActualHeight >= expectedHeight - 0.5 && window.ActualHeight <= expectedHeight + 0.5,
            $"Height mismatch: expected={expectedHeight}, Height={window.Height}, ActualHeight={window.ActualHeight}, " +
            $"ViewModelHeight={viewModel.WidgetHeight}, IsLoaded={window.IsLoaded}, IsVisible={window.IsVisible}.");
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
        public AppSettings? LastSaved { get; private set; }
        public ValueTask<AppSettings> LoadAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(AppSettings.Default);
        public ValueTask SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
        {
            LastSaved = settings;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeStartupManager : IStartupManager
    {
        public bool IsEnabled => false;
        public ValueTask<bool> SetEnabledAsync(bool isEnabled, CancellationToken cancellationToken = default) => ValueTask.FromResult(true);
    }
}
