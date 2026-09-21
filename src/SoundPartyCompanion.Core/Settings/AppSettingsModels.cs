namespace SoundPartyCompanion.Core.Settings;

public enum WidgetDisplayMode
{
    Offline,
    OfflineDetails,
    Compact,
    Expanded
}

[Flags]
public enum WindowSnapEdges
{
    None = 0,
    Left = 1,
    Top = 2,
    Right = 4,
    Bottom = 8
}

public sealed record NormalizedWindowPlacement(
    string MonitorId,
    double X,
    double Y);

public sealed record AppSettings(
    bool StartWithWindows,
    bool AutoCollapseOffline,
    bool AlwaysOnTop,
    double Opacity,
    bool LowBatteryNotifications,
    bool HandsFreeAdvanced,
    bool ExperimentalProtocol,
    bool ManualCollapsed,
    NormalizedWindowPlacement? WindowPlacement)
{
    public static AppSettings Default { get; } = new(
        StartWithWindows: true,
        AutoCollapseOffline: true,
        AlwaysOnTop: false,
        Opacity: 1,
        LowBatteryNotifications: true,
        HandsFreeAdvanced: false,
        ExperimentalProtocol: false,
        ManualCollapsed: false,
        WindowPlacement: null);
}

public static class AppSettingsRules
{
    public const double MinimumOpacity = 0.65;

    public static AppSettings Normalize(AppSettings? settings)
    {
        if (settings is null)
        {
            return AppSettings.Default;
        }

        return settings with
        {
            Opacity = NormalizeOpacity(settings.Opacity),
            WindowPlacement = NormalizePlacement(settings.WindowPlacement)
        };
    }

    public static double NormalizeOpacity(double value)
    {
        if (!double.IsFinite(value))
        {
            return 1;
        }

        return Math.Clamp(value, MinimumOpacity, 1);
    }

    public static NormalizedWindowPlacement? NormalizePlacement(
        NormalizedWindowPlacement? placement)
    {
        if (placement is null || string.IsNullOrWhiteSpace(placement.MonitorId))
        {
            return null;
        }

        return placement with
        {
            MonitorId = placement.MonitorId.Trim(),
            X = NormalizeCoordinate(placement.X),
            Y = NormalizeCoordinate(placement.Y)
        };
    }

    private static double NormalizeCoordinate(double value) =>
        double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;
}

public sealed record WindowWorkArea(
    double Left,
    double Top,
    double Width,
    double Height);

public sealed record WindowPosition(double Left, double Top);

public sealed record WindowSnapCandidate(
    WindowPosition Position,
    WindowSnapEdges Edges)
{
    public bool HasTarget => Edges != WindowSnapEdges.None;
}

public static class WindowPlacementRules
{
    public static WindowPosition Restore(
        WindowWorkArea workArea,
        double windowWidth,
        double windowHeight,
        NormalizedWindowPlacement? placement,
        double defaultMargin = 24)
    {
        var availableWidth = Math.Max(0, workArea.Width - windowWidth);
        var availableHeight = Math.Max(0, workArea.Height - windowHeight);

        if (placement is null)
        {
            return new WindowPosition(
                workArea.Left + Math.Max(0, availableWidth - defaultMargin),
                workArea.Top + Math.Min(defaultMargin, availableHeight));
        }

        return new WindowPosition(
            workArea.Left + availableWidth * placement.X,
            workArea.Top + availableHeight * placement.Y);
    }

    public static NormalizedWindowPlacement Normalize(
        string monitorId,
        WindowWorkArea workArea,
        double windowWidth,
        double windowHeight,
        WindowPosition position)
    {
        var availableWidth = Math.Max(0, workArea.Width - windowWidth);
        var availableHeight = Math.Max(0, workArea.Height - windowHeight);
        var x = availableWidth <= 0 ? 0 : (position.Left - workArea.Left) / availableWidth;
        var y = availableHeight <= 0 ? 0 : (position.Top - workArea.Top) / availableHeight;

        return new NormalizedWindowPlacement(
            monitorId,
            Math.Clamp(x, 0, 1),
            Math.Clamp(y, 0, 1));
    }

    public static WindowPosition ClampAndSnap(
        WindowWorkArea workArea,
        double windowWidth,
        double windowHeight,
        WindowPosition position,
        double snapDistance = 14)
    {
        var maximumLeft = Math.Max(workArea.Left, workArea.Left + workArea.Width - windowWidth);
        var maximumTop = Math.Max(workArea.Top, workArea.Top + workArea.Height - windowHeight);
        var left = Math.Clamp(position.Left, workArea.Left, maximumLeft);
        var top = Math.Clamp(position.Top, workArea.Top, maximumTop);

        if (Math.Abs(left - workArea.Left) <= snapDistance)
        {
            left = workArea.Left;
        }
        else if (Math.Abs(left - maximumLeft) <= snapDistance)
        {
            left = maximumLeft;
        }

        if (Math.Abs(top - workArea.Top) <= snapDistance)
        {
            top = workArea.Top;
        }
        else if (Math.Abs(top - maximumTop) <= snapDistance)
        {
            top = maximumTop;
        }

        return new WindowPosition(left, top);
    }
}

public static class WindowMotionRules
{
    public const double DefaultSnapDistance = 14;

    // All arguments use the same physical-pixel coordinate system. The anchor
    // is a fraction of the window so a DPI resize does not break the grab point.
    public static WindowPosition ResolveDragPosition(
        WindowPosition cursor, WindowPosition anchor, double width, double height) =>
        new(cursor.Left - anchor.Left * width, cursor.Top - anchor.Top * height);

    public static WindowSnapCandidate ResolveDragPreview(
        WindowWorkArea workArea, double width, double height,
        WindowPosition position, double snapDistance = DefaultSnapDistance)
    {
        var clamped = ClampDragPosition(workArea, width, height, position);
        // A straddling window must not preview a snap caused by clamping it
        // back into one monitor. Containment is applied only on release.
        return clamped != position
            ? new WindowSnapCandidate(position, WindowSnapEdges.None)
            : ResolveSnapCandidate(workArea, width, height, position, snapDistance);
    }

    public static WindowPosition ClampDragPosition(
        WindowWorkArea workArea,
        double windowWidth,
        double windowHeight,
        WindowPosition desiredPosition)
    {
        var maximumLeft = Math.Max(workArea.Left, workArea.Left + workArea.Width - windowWidth);
        var maximumTop = Math.Max(workArea.Top, workArea.Top + workArea.Height - windowHeight);
        return new WindowPosition(
            Math.Clamp(desiredPosition.Left, workArea.Left, maximumLeft),
            Math.Clamp(desiredPosition.Top, workArea.Top, maximumTop));
    }

    public static WindowSnapCandidate ResolveSnapCandidate(
        WindowWorkArea workArea,
        double windowWidth,
        double windowHeight,
        WindowPosition position,
        double snapDistance = DefaultSnapDistance)
    {
        var clamped = ClampDragPosition(workArea, windowWidth, windowHeight, position);
        var maximumLeft = Math.Max(workArea.Left, workArea.Left + workArea.Width - windowWidth);
        var maximumTop = Math.Max(workArea.Top, workArea.Top + workArea.Height - windowHeight);
        var threshold = Math.Max(0, snapDistance);
        var edges = WindowSnapEdges.None;
        var left = clamped.Left;
        var top = clamped.Top;

        if (Math.Abs(left - workArea.Left) <= threshold)
        {
            left = workArea.Left;
            edges |= WindowSnapEdges.Left;
        }
        else if (Math.Abs(left - maximumLeft) <= threshold)
        {
            left = maximumLeft;
            edges |= WindowSnapEdges.Right;
        }

        if (Math.Abs(top - workArea.Top) <= threshold)
        {
            top = workArea.Top;
            edges |= WindowSnapEdges.Top;
        }
        else if (Math.Abs(top - maximumTop) <= threshold)
        {
            top = maximumTop;
            edges |= WindowSnapEdges.Bottom;
        }

        return new WindowSnapCandidate(new WindowPosition(left, top), edges);
    }
}
