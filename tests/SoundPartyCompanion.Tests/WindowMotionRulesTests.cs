using SoundPartyCompanion.Core.Settings;
using Xunit;
using SoundPartyCompanion.App.Infrastructure;

namespace SoundPartyCompanion.Tests;

public sealed class WindowMotionRulesTests
{
    private static readonly WindowWorkArea WorkArea = new(0, 0, 1000, 800);

    [Fact]
    public void NativeWindowDragTraversesRealMonitorCoordinatesInBothDirections()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            System.Windows.Window? window = null;
            try
            {
                window = new System.Windows.Window
                {
                    Width = 200, Height = 100, WindowStyle = System.Windows.WindowStyle.None,
                    ResizeMode = System.Windows.ResizeMode.NoResize, AllowsTransparency = true,
                    Background = System.Windows.Media.Brushes.Transparent, Opacity = 0,
                    ShowActivated = false, ShowInTaskbar = false
                };
                window.Show();
                var service = new WindowPlacementService();
                var anchor = new WindowPosition(0, 0);
                var points = System.Windows.Forms.Screen.AllScreens.OrderBy(screen => screen.Bounds.Left)
                    .SelectMany(screen => new[]
                    {
                        new WindowPosition(screen.Bounds.Left - 1, screen.WorkingArea.Top + 50),
                        new WindowPosition(screen.Bounds.Left + 1, screen.WorkingArea.Top + 50),
                        new WindowPosition(screen.WorkingArea.Left + screen.WorkingArea.Width / 2, screen.WorkingArea.Top + 50)
                    }).ToArray();
                foreach (var point in points.Concat(points.Reverse()))
                {
                    service.MoveDraggedWindow(window, point, anchor);
                    var actual = window.PointToScreen(new System.Windows.Point(0, 0));
                    Assert.InRange(Math.Abs(actual.X - point.Left), 0, 1);
                    Assert.InRange(Math.Abs(actual.Y - point.Top), 0, 1);
                }
            }
            catch (Exception exception) { failure = exception; }
            finally { window?.Close(); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "Native drag regression timed out.");
        if (failure is not null) throw new Xunit.Sdk.XunitException(failure.ToString());
    }

    [Theory]
    [InlineData(1100, 1000)]
    [InlineData(1000, 900)]
    [InlineData(900, 800)]
    [InlineData(-100, -200)]
    public void DragCrossesMonitorSeamsAndNegativeCoordinates(double cursorX, double expectedLeft)
    {
        var result = WindowMotionRules.ResolveDragPosition(
            new WindowPosition(cursorX, 350), new WindowPosition(0.5, 0.5), 200, 100);
        Assert.Equal(new WindowPosition(expectedLeft, 300), result);
    }

    [Fact]
    public void DpiResizeKeepsNormalizedGrabPointUnderCursor()
    {
        var cursor = new WindowPosition(1100, 400);
        var anchor = new WindowPosition(0.25, 0.5);
        var before = WindowMotionRules.ResolveDragPosition(cursor, anchor, 200, 100);
        var after = WindowMotionRules.ResolveDragPosition(cursor, anchor, 300, 150);
        Assert.Equal(cursor.Left, before.Left + 200 * anchor.Left);
        Assert.Equal(cursor.Left, after.Left + 300 * anchor.Left);
        Assert.Equal(cursor.Top, after.Top + 150 * anchor.Top);
    }

    [Theory]
    [InlineData(950)]
    [InlineData(1050)]
    public void StraddlingMonitorSeamDoesNotPreviewClampAsSnap(double left)
    {
        var area = new WindowWorkArea(1000, 0, 1000, 800);
        var position = new WindowPosition(left, 300);
        var preview = WindowMotionRules.ResolveDragPreview(area, 200, 100, position);
        if (left < 1000)
        {
            Assert.False(preview.HasTarget);
            Assert.Equal(position, preview.Position);
        }
        else Assert.Equal(position, preview.Position);
    }

    [Fact]
    public void ReleaseContainsWindowInDestinationMonitor()
    {
        var area = new WindowWorkArea(-1000, 0, 1000, 800);
        var candidate = WindowMotionRules.ResolveSnapCandidate(area, 200, 100, new WindowPosition(-150, 300));
        Assert.Equal(new WindowPosition(-200, 300), candidate.Position);
        Assert.Equal(WindowSnapEdges.Right, candidate.Edges);
    }

    [Fact]
    public void PreviewScalesThresholdWithoutChangingPhysicalPosition()
    {
        var candidate = WindowMotionRules.ResolveDragPreview(WorkArea, 200, 150, new WindowPosition(20, 300), 14 * 1.5);
        Assert.Equal(WindowSnapEdges.Left, candidate.Edges);
        Assert.Equal(new WindowPosition(0, 300), candidate.Position);
    }

    [Fact]
    public void DragRemainsOneToOneInsideWorkArea()
    {
        var desired = new WindowPosition(20, 300);

        var result = WindowMotionRules.ClampDragPosition(
            WorkArea,
            100,
            100,
            desired);

        Assert.Equal(desired, result);
    }

    [Fact]
    public void DragClampsOnlyAtWorkAreaBoundary()
    {
        var result = WindowMotionRules.ClampDragPosition(
            WorkArea,
            100,
            100,
            new WindowPosition(-500, 1200));

        Assert.Equal(new WindowPosition(0, 700), result);
    }

    [Fact]
    public void CandidatePreviewsEdgesWithoutMovingOutsideThreshold()
    {
        var result = WindowMotionRules.ResolveSnapCandidate(
            WorkArea,
            100,
            100,
            new WindowPosition(12, 690));

        Assert.True(result.HasTarget);
        Assert.Equal(WindowSnapEdges.Left | WindowSnapEdges.Bottom, result.Edges);
        Assert.Equal(new WindowPosition(0, 700), result.Position);
    }

    [Fact]
    public void SnapTargetSnapsOnlyInsideReleaseThreshold()
    {
        Assert.Equal(
            new WindowSnapCandidate(new WindowPosition(0, 0), WindowSnapEdges.Left | WindowSnapEdges.Top),
            WindowMotionRules.ResolveSnapCandidate(
                WorkArea,
                100,
                100,
                new WindowPosition(12, 9)));

        Assert.Equal(
            new WindowSnapCandidate(new WindowPosition(40, 45), WindowSnapEdges.None),
            WindowMotionRules.ResolveSnapCandidate(
                WorkArea,
                100,
                100,
                new WindowPosition(40, 45)));
    }

    [Fact]
    public void CandidateUsesStrictReleaseThreshold()
    {
        var result = WindowMotionRules.ResolveSnapCandidate(
            WorkArea,
            100,
            100,
            new WindowPosition(WindowMotionRules.DefaultSnapDistance + 0.1, 300));

        Assert.False(result.HasTarget);
        Assert.Equal(new WindowPosition(14.1, 300), result.Position);
    }
}
