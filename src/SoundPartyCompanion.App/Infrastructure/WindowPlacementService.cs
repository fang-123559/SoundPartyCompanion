using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using FormsScreen = System.Windows.Forms.Screen;
using SoundPartyCompanion.Core.Settings;

namespace SoundPartyCompanion.App.Infrastructure;

public sealed class WindowPlacementService
{
    public void Restore(Window window, NormalizedWindowPlacement? placement)
    {
        ArgumentNullException.ThrowIfNull(window);
        var screen = SelectScreen(placement?.MonitorId);
        var workArea = ToPixelWorkArea(screen);
        var bounds = GetPixelBounds(window);
        var position = WindowPlacementRules.Restore(
            workArea,
            bounds.Width,
            bounds.Height,
            placement,
            defaultMargin: 24 * GetWindowScale(window));
        MoveInPixels(window, position);
    }

    public NormalizedWindowPlacement Capture(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        var screen = GetScreen(window);
        var workArea = ToPixelWorkArea(screen);
        var bounds = GetPixelBounds(window);
        return WindowPlacementRules.Normalize(
            screen.DeviceName,
            workArea,
            bounds.Width,
            bounds.Height,
            new WindowPosition(bounds.Left, bounds.Top));
    }

    public void ClampAndSnap(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        MoveInPixels(window, ResolvePixelCandidate(window, preview: false).Position);
    }

    public WindowPosition GetDragAnchor(Window window, WindowPosition cursor)
    {
        var bounds = GetPixelBounds(window);
        return new WindowPosition(
            (cursor.Left - bounds.Left) / Math.Max(1, bounds.Width),
            (cursor.Top - bounds.Top) / Math.Max(1, bounds.Height));
    }

    public void MoveDraggedWindow(Window window, WindowPosition cursor, WindowPosition anchor)
    {
        var bounds = GetPixelBounds(window);
        MoveInPixels(window, WindowMotionRules.ResolveDragPosition(cursor, anchor, bounds.Width, bounds.Height));
        // Moving may synchronously resize a per-monitor-aware window. Reapply
        // the same grab point using its new physical size, not its old DPI.
        var updated = GetPixelBounds(window);
        if (updated.Width != bounds.Width || updated.Height != bounds.Height)
            MoveInPixels(window, WindowMotionRules.ResolveDragPosition(cursor, anchor, updated.Width, updated.Height));
    }

    public WindowSnapCandidate GetSnapCandidate(Window window) =>
        ToDipCandidate(window, ResolvePixelCandidate(window, preview: true));

    public WindowPosition GetReleasePosition(Window window) =>
        ToDipCandidate(window, ResolvePixelCandidate(window, preview: false)).Position;

    private static WindowSnapCandidate ResolvePixelCandidate(Window window, bool preview)
    {
        var bounds = GetPixelBounds(window);
        var area = ToPixelWorkArea(GetScreen(window));
        var position = new WindowPosition(bounds.Left, bounds.Top);
        var threshold = WindowMotionRules.DefaultSnapDistance * GetWindowScale(window);
        return preview
            ? WindowMotionRules.ResolveDragPreview(area, bounds.Width, bounds.Height, position, threshold)
            : WindowMotionRules.ResolveSnapCandidate(area, bounds.Width, bounds.Height, position, threshold);
    }

    private static WindowSnapCandidate ToDipCandidate(Window window, WindowSnapCandidate candidate)
    {
        var scale = GetWindowScale(window);
        return candidate with { Position = new WindowPosition(candidate.Position.Left / scale, candidate.Position.Top / scale) };
    }

    public void ResetToPreferredScreen(Window window)
    {
        Restore(window, placement: null);
    }

    private static FormsScreen SelectScreen(string? monitorId)
    {
        if (!string.IsNullOrWhiteSpace(monitorId))
        {
            var saved = FormsScreen.AllScreens.FirstOrDefault(screen =>
                string.Equals(screen.DeviceName, monitorId, StringComparison.OrdinalIgnoreCase));
            if (saved is not null)
            {
                return saved;
            }
        }

        return FormsScreen.AllScreens
            .Where(screen => !screen.Primary)
            .OrderByDescending(screen => screen.WorkingArea.Right)
            .FirstOrDefault()
            ?? FormsScreen.PrimaryScreen
            ?? FormsScreen.AllScreens[0];
    }

    private static FormsScreen GetScreen(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        return handle != nint.Zero
            ? FormsScreen.FromHandle(handle)
            : SelectScreen(monitorId: null);
    }

    private static WindowWorkArea ToPixelWorkArea(FormsScreen screen)
    {
        var area = screen.WorkingArea;
        return new WindowWorkArea(area.Left, area.Top, area.Width, area.Height);
    }

    private static double GetWindowScale(Window window) =>
        PresentationSource.FromVisual(window)?.CompositionTarget?.TransformToDevice.M11 ?? 1;

    private static NativeRect GetPixelBounds(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle != nint.Zero && GetWindowRect(handle, out var bounds)) return bounds;
        var scale = GetWindowScale(window);
        return new NativeRect { Left = (int)(window.Left * scale), Top = (int)(window.Top * scale),
            Right = (int)((window.Left + window.Width) * scale), Bottom = (int)((window.Top + window.Height) * scale) };
    }

    private static void MoveInPixels(Window window, WindowPosition position)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == nint.Zero)
        {
            window.Left = position.Left / GetWindowScale(window);
            window.Top = position.Top / GetWindowScale(window);
            return;
        }
        if (!SetWindowPos(handle, nint.Zero, (int)Math.Round(position.Left), (int)Math.Round(position.Top),
                0, 0, 0x0001 | 0x0004 | 0x0010))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
    }

    [DllImport("User32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint window, out NativeRect bounds);

    [DllImport("User32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint window, nint insertAfter, int x, int y, int width, int height, uint flags);
}
