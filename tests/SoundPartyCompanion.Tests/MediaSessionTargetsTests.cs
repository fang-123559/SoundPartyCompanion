using SoundPartyCompanion.Windows.Media;
using Xunit;

namespace SoundPartyCompanion.Tests;

public sealed class MediaSessionTargetsTests
{
    private sealed class Session(string appId) { public string AppId { get; } = appId; }

    [Fact]
    public void SameApplicationSessionsHaveDistinctStableTargetsEvenAfterReordering()
    {
        var movie = new Session("browser");
        var series = new Session("browser");
        var targets = new MediaSessionTargets<Session>();
        targets.Update([movie, series]);
        var movieId = targets.GetId(movie)!;
        var seriesId = targets.GetId(series)!;
        Assert.Equal(movie.AppId, series.AppId);
        Assert.NotEqual(movieId, seriesId);
        targets.Update([series, movie]);
        Assert.Same(series, targets.Resolve(seriesId));
        Assert.Same(movie, targets.Resolve(movieId));
    }

    [Fact]
    public void DisappearedTargetNeverFallsBackToAnotherTabOrReusesItsToken()
    {
        var movie = new Session("browser");
        var series = new Session("browser");
        var targets = new MediaSessionTargets<Session>();
        targets.Update([movie, series]);
        var seriesId = targets.GetId(series)!;
        targets.Update([movie]);
        Assert.Null(targets.Resolve(seriesId));
        targets.Update([movie, series]);
        Assert.NotEqual(seriesId, targets.GetId(series));
    }
}
