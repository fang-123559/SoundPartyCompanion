using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using SoundPartyCompanion.App.ViewModels;
using WpfButtonBase = System.Windows.Controls.Primitives.ButtonBase;

namespace SoundPartyCompanion.App;

public partial class SettingsWindow : Window
{
    private const double WheelStep = 64;
    private const double SettleThreshold = 0.35;
    private const double ScrollTimeConstantSeconds = 0.055;
    private double _scrollTarget;
    private TimeSpan? _lastRenderingTime;
    private bool _isSmoothScrolling;
    private bool _isApplyingSmoothOffset;

    public SettingsWindow(ConnectionStatusViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        InitializeComponent();
        DataContext = viewModel;
        Loaded += HandleWindowLoaded;
    }

    private void HandleCloseClick(object sender, RoutedEventArgs e) => Close();

    private void HandleWindowDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && FindAncestor<WpfButtonBase>(e.OriginalSource as DependencyObject) is null)
        {
            DragMove();
        }
    }

    private void HandleWindowLoaded(object sender, RoutedEventArgs e) =>
        _scrollTarget = SettingsScrollViewer.VerticalOffset;

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
