using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using SoundPartyCompanion.App.Infrastructure;
using SoundPartyCompanion.App.ViewModels;
using WpfButtonBase = System.Windows.Controls.Primitives.ButtonBase;

namespace SoundPartyCompanion.App;

public partial class SettingsWindow : Window
{
    private readonly WindowPlacementService _placementService = new();
    private readonly MainWindow _widgetWindow;
    private bool _restoreWidgetOnClose;
    private const double WheelStep = 64;
    private const double SettleThreshold = 0.35;
    private const double ScrollTimeConstantSeconds = 0.055;
    private double _scrollTarget;
    private TimeSpan? _lastRenderingTime;
    private bool _isSmoothScrolling;
    private bool _isApplyingSmoothOffset;

    public SettingsWindow(ConnectionStatusViewModel viewModel, MainWindow widgetWindow)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(widgetWindow);
        _widgetWindow = widgetWindow;
        InitializeComponent();
        DataContext = viewModel;
        Loaded += HandleWindowLoaded;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        PositionInPlace();
    }

    public void ShowInPlace()
    {
        if (IsVisible)
        {
            WindowState = WindowState.Normal;
            if (ShowActivated) Activate();
            return;
        }

        // Keep the same widget instance and its position/presentation state.
        // This window is deliberately unowned: hiding a WPF owner also hides its owned windows.
        _restoreWidgetOnClose = _widgetWindow.IsVisible;
        _widgetWindow.Hide();
        try
        {
            Show();
            PositionInPlace();
            if (ShowActivated) Activate();
        }
        catch
        {
            RestoreWidget();
            throw;
        }
    }

    public void CloseWithoutRestoringWidget()
    {
        _restoreWidgetOnClose = false;
        Close();
    }

    private void PositionInPlace() =>
        _placementService.ReplaceAtAnchor(this, _widgetWindow, SettingsSurface.Margin.Top);

    private void RestoreWidget()
    {
        var restore = _restoreWidgetOnClose;
        _restoreWidgetOnClose = false;
        if (restore && !_widgetWindow.AllowClose) _widgetWindow.ShowFromTray();
    }

    protected override void OnPreviewKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
            return;
        }
        base.OnPreviewKeyDown(e);
    }

    private void HandleCloseClick(object sender, RoutedEventArgs e) => Close();

    private void HandleWindowDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && FindAncestor<WpfButtonBase>(e.OriginalSource as DependencyObject) is null)
        {
            DragMove();
        }
    }

    private void HandleWindowLoaded(object sender, RoutedEventArgs e)
    {
        PositionInPlace();
        _scrollTarget = SettingsScrollViewer.VerticalOffset;
    }

    private void HandleSettingsMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (SettingsScrollViewer.ScrollableHeight <= 0) return;

        var origin = _isSmoothScrolling
            ? _scrollTarget
            : SettingsScrollViewer.VerticalOffset;
        _scrollTarget = Math.Clamp(
            origin - (e.Delta / 120d * WheelStep),
            0,
            SettingsScrollViewer.ScrollableHeight);

        if (!SystemParameters.ClientAreaAnimation)
        {
            SettingsScrollViewer.ScrollToVerticalOffset(_scrollTarget);
        }
        else if (!_isSmoothScrolling)
        {
            _isSmoothScrolling = true;
            _lastRenderingTime = null;
            CompositionTarget.Rendering += HandleRendering;
        }

        e.Handled = true;
    }

    private void HandleRendering(object? sender, EventArgs e)
    {
        if (!_isSmoothScrolling || e is not RenderingEventArgs rendering) return;

        if (_lastRenderingTime is null)
        {
            _lastRenderingTime = rendering.RenderingTime;
            return;
        }

        var elapsed = Math.Clamp(
            (rendering.RenderingTime - _lastRenderingTime.Value).TotalSeconds,
            0,
            0.05);
        _lastRenderingTime = rendering.RenderingTime;
        var current = SettingsScrollViewer.VerticalOffset;
        var distance = _scrollTarget - current;
        if (Math.Abs(distance) <= SettleThreshold)
        {
            ApplyScrollOffset(_scrollTarget);
            StopSmoothScroll(updateTarget: false);
            return;
        }

        var progress = 1 - Math.Exp(-elapsed / ScrollTimeConstantSeconds);
        ApplyScrollOffset(current + distance * progress);
    }

    private void HandleScrollPointerDown(object sender, MouseButtonEventArgs e) =>
        StopSmoothScroll(updateTarget: true);

    private void HandleSettingsScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (!_isApplyingSmoothOffset && !_isSmoothScrolling)
        {
            _scrollTarget = SettingsScrollViewer.VerticalOffset;
        }
    }

    private void ApplyScrollOffset(double offset)
    {
        _isApplyingSmoothOffset = true;
        SettingsScrollViewer.ScrollToVerticalOffset(offset);
        _isApplyingSmoothOffset = false;
    }

    private void StopSmoothScroll(bool updateTarget)
    {
        if (_isSmoothScrolling)
        {
            CompositionTarget.Rendering -= HandleRendering;
            _isSmoothScrolling = false;
            _lastRenderingTime = null;
        }

        if (updateTarget)
        {
            _scrollTarget = SettingsScrollViewer.VerticalOffset;
        }
    }

    private void HandleWindowClosed(object? sender, EventArgs e)
    {
        StopSmoothScroll(updateTarget: false);
        Loaded -= HandleWindowLoaded;
        RestoreWidget();
    }

    private static T? FindAncestor<T>(DependencyObject? source) where T : DependencyObject
    {
        for (var current = source; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is T match) return match;
        }
        return null;
    }
}
