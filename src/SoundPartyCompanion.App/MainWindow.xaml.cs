using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Media.Animation;
using Microsoft.Win32;
using SoundPartyCompanion.App.Infrastructure;
using SoundPartyCompanion.App.ViewModels;
using SoundPartyCompanion.Core.Settings;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfPoint = System.Windows.Point;

namespace SoundPartyCompanion.App;

public partial class MainWindow : Window
{
    private const int DeviceChangeMessage = 0x0219;
    private const int PowerBroadcastMessage = 0x0218;
    private const int ResumeAutomatic = 0x0012;
    private const int SessionChangeMessage = 0x02B1;
    private const int SessionUnlock = 0x0008;
    private const int NotifyForThisSession = 0;
    private const int ExtendedStyleIndex = -20;
    private const int ToolWindowStyle = 0x00000080;
    private const int SystemCommandMessage = 0x0112;
    private const int MaximizeCommand = 0xF030;
    private const double DragThreshold = 6;
    private readonly ConnectionStatusViewModel _viewModel;
    private readonly WindowPlacementService _placementService;
    private readonly DispatcherTimer _placementSaveTimer;
    private readonly bool _animationsEnabled;
    private HwndSource? _windowSource;
    private WpfPoint _pointerStart;
    private WindowPosition _dragAnchor = new(0, 0);
    private bool _pointerPressed;
    private bool _isDragging;
    private bool _applyingBounds;
    private bool _transitioning;
    private bool _placementRestored;
    private bool _allowClose;
    private int _transitionVersion;
    private int _positionAnimationVersion;
    private WindowSnapCandidate? _snapCandidate;
    private WindowSnapEdges _visibleSnapEdges;

    public MainWindow(
        ConnectionStatusViewModel viewModel,
        WindowPlacementService placementService,
        bool animationsEnabled = true)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(placementService);
        InitializeComponent();
        _viewModel = viewModel;
        _placementService = placementService;
        _animationsEnabled = animationsEnabled && SystemParameters.ClientAreaAnimation;
        DataContext = viewModel;
        _placementSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _placementSaveTimer.Tick += HandlePlacementSaveTimer;
        _viewModel.PropertyChanged += HandleViewModelPropertyChanged;
        _viewModel.WindowPositionResetRequested += HandleWindowPositionResetRequested;
        LocationChanged += HandleLocationChanged;
        SystemEvents.DisplaySettingsChanged += HandleDisplaySettingsChanged;
        ApplyWidgetDimensions();
    }

    public bool AllowClose { get => _allowClose; set => _allowClose = value; }

    public int VisibleStatePanelCount =>
        new[] { OfflinePanel, OfflineDetailsPanel, CompactPanel, ExpandedPanel }
            .Count(panel => panel.Visibility == Visibility.Visible);

    public bool HasExpectedDimensions =>
        Math.Abs(ActualWidth - _viewModel.WidgetWidth) < 0.5 &&
        Math.Abs(ActualHeight - _viewModel.WidgetHeight) < 0.5;

    public void ShowFromTray()
    {
        WindowState = WindowState.Normal;
        Show();
        Activate();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        _windowSource = HwndSource.FromHwnd(handle);
        _windowSource?.AddHook(HandleWindowMessage);
        WTSRegisterSessionNotification(handle, NotifyForThisSession);
        SetWindowLong(handle, ExtendedStyleIndex, GetWindowLong(handle, ExtendedStyleIndex) | ToolWindowStyle);
        _placementService.Restore(this, _viewModel.WindowPlacement);
        _placementRestored = true;
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        var oldRight = Left + sizeInfo.PreviousSize.Width;
        base.OnRenderSizeChanged(sizeInfo);
        if (_pointerPressed || _applyingBounds || !_placementRestored || !sizeInfo.WidthChanged || sizeInfo.PreviousSize.Width <= 0) return;
        Left = oldRight - sizeInfo.NewSize.Width;
        _placementService.ClampAndSnap(this);
        SchedulePlacementSave();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _placementSaveTimer.Stop();
        LocationChanged -= HandleLocationChanged;
        SystemEvents.DisplaySettingsChanged -= HandleDisplaySettingsChanged;
        _viewModel.PropertyChanged -= HandleViewModelPropertyChanged;
        _viewModel.WindowPositionResetRequested -= HandleWindowPositionResetRequested;
        var handle = new WindowInteropHelper(this).Handle;
        WTSUnRegisterSessionNotification(handle);
        _windowSource?.RemoveHook(HandleWindowMessage);
        _windowSource = null;
        base.OnClosed(e);
    }

    private void HandlePointerDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || IsInteractive(e.OriginalSource as DependencyObject)) return;
        StopPositionAnimationPreservingCurrent();
        _pointerPressed = true;
        _isDragging = false;
        _pointerStart = GetCursorPositionInPixels();
        _dragAnchor = _placementService.GetDragAnchor(this, new WindowPosition(_pointerStart.X, _pointerStart.Y));
        CaptureMouse();
        e.Handled = true;
    }

    private void HandlePointerMove(object sender, WpfMouseEventArgs e)
    {
        if (!_pointerPressed || e.LeftButton != MouseButtonState.Pressed) return;
        var current = GetCursorPositionInPixels();
        var delta = current - _pointerStart;
        var scale = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice.M11 ?? 1;
        if (!_isDragging && Math.Abs(delta.X) < DragThreshold * scale && Math.Abs(delta.Y) < DragThreshold * scale) return;
        _isDragging = true;
        _placementService.MoveDraggedWindow(this, new WindowPosition(current.X, current.Y), _dragAnchor);
        _snapCandidate = _placementService.GetSnapCandidate(this);
        UpdateSnapGuide(_snapCandidate);
        e.Handled = true;
    }

    private async void HandlePointerUp(object sender, MouseButtonEventArgs e)
    {
        if (!_pointerPressed || e.ChangedButton != MouseButton.Left) return;
        _pointerPressed = false;
        ReleaseMouseCapture();
        if (_isDragging)
        {
            UpdateSnapGuide(null);
            // Re-evaluate after crossing screens; the preview is not a cached
            // release constraint. Ensure full visibility only after mouse-up.
            AnimatePosition(_placementService.GetReleasePosition(this));
        }
        else
        {
            await _viewModel.ActivateWidgetAsync();
        }
        _isDragging = false;
        _snapCandidate = null;
        e.Handled = true;
    }

    private void HandleWindowStateChanged(object sender, EventArgs e)
    {
        if (WindowState != WindowState.Normal) WindowState = WindowState.Normal;
    }

    private async void HandleWindowDeactivated(object? sender, EventArgs e)
    {
        if (!_isDragging && _viewModel.WidgetMode == WidgetDisplayMode.OfflineDetails)
        {
            await _viewModel.CloseOfflineDetailsAsync();
        }
    }

    private void HandleViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not nameof(ConnectionStatusViewModel.WidgetMode) and
            not nameof(ConnectionStatusViewModel.WidgetWidth) and
            not nameof(ConnectionStatusViewModel.WidgetHeight)) return;
        _ = Dispatcher.BeginInvoke(ApplyWidgetDimensions, DispatcherPriority.Render);
    }

    private void ApplyWidgetDimensions()
    {
        if (!_animationsEnabled || !IsLoaded || ActualWidth <= 0 || ActualHeight <= 0)
        {
            ApplyWidgetDimensionsImmediately();
            return;
        }

        AnimateWidgetDimensions();
    }

    private void ApplyWidgetDimensionsImmediately()
    {
        _transitionVersion++;
        _transitioning = false;
        StopSurfaceAnimations();
        ApplyWindowBounds(_viewModel.WidgetWidth, _viewModel.WidgetHeight);
    }

    private void AnimateWidgetDimensions()
    {
        var wasTransitioning = _transitioning;
        var version = ++_transitionVersion;
        var duration = TimeSpan.FromMilliseconds(
            _viewModel.WidgetMode == WidgetDisplayMode.Expanded ? 250 : 167);

        PreserveSurfaceAnimationValues();
        ApplyWindowBounds(_viewModel.WidgetWidth, _viewModel.WidgetHeight);

        if (!wasTransitioning)
        {
            WidgetSurface.Opacity = 0.82;
            WidgetSurfaceScale.ScaleX = 0.985;
            WidgetSurfaceScale.ScaleY = 0.985;
            WidgetSurfaceTranslate.Y = _viewModel.WidgetMode == WidgetDisplayMode.Expanded ? 8 : 4;
        }

        _transitioning = true;
        WidgetSurface.BeginAnimation(OpacityProperty, CreateAnimation(WidgetSurface.Opacity, 1, duration));
        WidgetSurfaceScale.BeginAnimation(ScaleTransform.ScaleXProperty, CreateAnimation(WidgetSurfaceScale.ScaleX, 1, duration));
        WidgetSurfaceScale.BeginAnimation(ScaleTransform.ScaleYProperty, CreateAnimation(WidgetSurfaceScale.ScaleY, 1, duration));
        var translateAnimation = CreateAnimation(WidgetSurfaceTranslate.Y, 0, duration);
        translateAnimation.Completed += (_, _) =>
        {
            if (version != _transitionVersion) return;
            StopSurfaceAnimations();
            _transitioning = false;
            if (!_pointerPressed) _placementService.ClampAndSnap(this);
            SchedulePlacementSave();
        };
        WidgetSurfaceTranslate.BeginAnimation(TranslateTransform.YProperty, translateAnimation);
    }

    private void ApplyWindowBounds(double targetWidth, double targetHeight)
    {
        var currentWidth = ActualWidth > 0 ? ActualWidth : Width;
        var right = Left + currentWidth;
        _applyingBounds = true;
        SetCurrentValue(WidthProperty, targetWidth);
        SetCurrentValue(HeightProperty, targetHeight);
        Left = right - targetWidth;
        InvalidateMeasure();
        InvalidateArrange();
        UpdateLayout();
        _applyingBounds = false;
        if (!_pointerPressed) _placementService.ClampAndSnap(this);
    }

    private void PreserveSurfaceAnimationValues()
    {
        var opacity = WidgetSurface.Opacity;
        var scaleX = WidgetSurfaceScale.ScaleX;
        var scaleY = WidgetSurfaceScale.ScaleY;
        var translateY = WidgetSurfaceTranslate.Y;
        WidgetSurface.BeginAnimation(OpacityProperty, null);
        WidgetSurfaceScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        WidgetSurfaceScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        WidgetSurfaceTranslate.BeginAnimation(TranslateTransform.YProperty, null);
        WidgetSurface.Opacity = opacity;
        WidgetSurfaceScale.ScaleX = scaleX;
        WidgetSurfaceScale.ScaleY = scaleY;
        WidgetSurfaceTranslate.Y = translateY;
    }

    private void StopSurfaceAnimations()
    {
        WidgetSurface.BeginAnimation(OpacityProperty, null);
        WidgetSurfaceScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        WidgetSurfaceScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        WidgetSurfaceTranslate.BeginAnimation(TranslateTransform.YProperty, null);
        WidgetSurface.Opacity = 1;
        WidgetSurfaceScale.ScaleX = 1;
        WidgetSurfaceScale.ScaleY = 1;
        WidgetSurfaceTranslate.Y = 0;
    }

    private void AnimatePosition(WindowPosition target)
    {
        var currentLeft = Left;
        var currentTop = Top;
        StopPositionAnimationPreservingCurrent();
        if (!_animationsEnabled ||
            (Math.Abs(currentLeft - target.Left) < 0.1 && Math.Abs(currentTop - target.Top) < 0.1))
        {
            Left = target.Left;
            Top = target.Top;
            SchedulePlacementSave();
            return;
        }

        var version = ++_positionAnimationVersion;
        var duration = TimeSpan.FromMilliseconds(150);
        Left = target.Left;
        Top = target.Top;
        BeginAnimation(LeftProperty, CreateAnimation(currentLeft, target.Left, duration));
        var topAnimation = CreateAnimation(currentTop, target.Top, duration);
        topAnimation.Completed += (_, _) =>
        {
            if (version == _positionAnimationVersion) SchedulePlacementSave();
        };
        BeginAnimation(TopProperty, topAnimation);
    }

    private void StopPositionAnimationPreservingCurrent()
    {
        var currentLeft = Left;
        var currentTop = Top;
        _positionAnimationVersion++;
        BeginAnimation(LeftProperty, null);
        BeginAnimation(TopProperty, null);
        Left = currentLeft;
        Top = currentTop;
    }

    private void UpdateSnapGuide(WindowSnapCandidate? candidate)
    {
        var edges = candidate?.Edges ?? WindowSnapEdges.None;
        if (edges == _visibleSnapEdges) return;
        _visibleSnapEdges = edges;
        SnapGuide.BorderThickness = new Thickness(
            edges.HasFlag(WindowSnapEdges.Left) ? 2 : 0,
            edges.HasFlag(WindowSnapEdges.Top) ? 2 : 0,
            edges.HasFlag(WindowSnapEdges.Right) ? 2 : 0,
            edges.HasFlag(WindowSnapEdges.Bottom) ? 2 : 0);

        var targetOpacity = edges == WindowSnapEdges.None ? 0 : 0.78;
        if (!_animationsEnabled)
        {
            SnapGuide.Opacity = targetOpacity;
            return;
        }

        SnapGuide.BeginAnimation(
            OpacityProperty,
            CreateAnimation(SnapGuide.Opacity, targetOpacity, TimeSpan.FromMilliseconds(83)));
    }

    private static DoubleAnimation CreateAnimation(
        double from,
        double to,
        TimeSpan duration) => new(from, to, new Duration(duration))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.HoldEnd
        };

    private async void HandleVolumeSliderReleased(object sender, MouseButtonEventArgs e)
    {
        if (sender is Slider slider) await _viewModel.SetVolumeAsync(slider.Value);
    }

    private async void HandleVolumeSliderKeyUp(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (sender is Slider slider && e.Key is Key.Left or Key.Right or Key.Up or Key.Down or
            Key.PageUp or Key.PageDown or Key.Home or Key.End)
        {
            await _viewModel.SetVolumeAsync(slider.Value);
        }
    }

    private void HandleLocationChanged(object? sender, EventArgs e)
    {
        if (_placementRestored) SchedulePlacementSave();
    }

    private void HandleWindowPositionResetRequested(object? sender, EventArgs e)
    {
        _placementService.ResetToPreferredScreen(this);
        SchedulePlacementSave();
    }

    private void HandleDisplaySettingsChanged(object? sender, EventArgs e)
    {
        _ = Dispatcher.BeginInvoke(() =>
        {
            WindowState = WindowState.Normal;
            _placementService.ClampAndSnap(this);
            SchedulePlacementSave();
        });
    }

    private void SchedulePlacementSave()
    {
        _placementSaveTimer.Stop();
        _placementSaveTimer.Start();
    }

    private async void HandlePlacementSaveTimer(object? sender, EventArgs e)
    {
        _placementSaveTimer.Stop();
        if (_pointerPressed || !_placementRestored || ActualWidth <= 0 || ActualHeight <= 0) return;
        await _viewModel.SaveWindowPlacementAsync(_placementService.Capture(this));
    }

    private nint HandleWindowMessage(nint window, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == SystemCommandMessage && ((int)wParam & 0xFFF0) == MaximizeCommand)
        {
            handled = true;
            return nint.Zero;
        }
        var shouldRefresh = message == DeviceChangeMessage ||
            (message == PowerBroadcastMessage && wParam == ResumeAutomatic) ||
            (message == SessionChangeMessage && wParam == SessionUnlock);
        if (shouldRefresh) _ = RefreshConnectionSafelyAsync();
        return nint.Zero;
    }

    private static WpfPoint GetCursorPositionInPixels()
    {
        GetCursorPos(out var cursor);
        return new WpfPoint(cursor.X, cursor.Y);
    }

    private async Task RefreshConnectionSafelyAsync()
    {
        try { await _viewModel.RefreshAsync(); }
        catch (ObjectDisposedException) { }
    }

    private static bool IsInteractive(DependencyObject? source)
    {
        for (var current = source; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is System.Windows.Controls.Primitives.ButtonBase or Slider or MenuItem) return true;
            if (current is Window) break;
        }
        return false;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X; public int Y; }

    [DllImport("User32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("Wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSRegisterSessionNotification(nint window, int flags);

    [DllImport("Wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSUnRegisterSessionNotification(nint window);

    [DllImport("User32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(nint window, int index);

    [DllImport("User32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong(nint window, int index, int newStyle);
}
