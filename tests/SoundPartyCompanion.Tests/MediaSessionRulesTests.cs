using SoundPartyCompanion.Core.Media;
using Xunit;

namespace SoundPartyCompanion.Tests;

public sealed class MediaSessionRulesTests
{
    [Fact]
    public void SelectPrefersMostRecentlyObservedPlayingSession()
    {
        var now = DateTimeOffset.UtcNow;
        var result = MediaSessionRules.Select(
        [
            Candidate("paused", MediaPlaybackState.Paused, now.AddSeconds(2)),
            Candidate("playing-old", MediaPlaybackState.Playing, now),
            Candidate("playing-new", MediaPlaybackState.Playing, now.AddSeconds(1))
        ],
        "paused");

        Assert.NotNull(result);
        Assert.Equal("playing-new", result.SessionId);
    }

    [Fact]
    public void SelectFallsBackToCurrentSessionWhenNothingIsPlaying()
    {
        var now = DateTimeOffset.UtcNow;
        var result = MediaSessionRules.Select(
        [
            Candidate("recent", MediaPlaybackState.Paused, now.AddSeconds(1)),
            Candidate("current", MediaPlaybackState.Stopped, now)
        ],
        "current");

        Assert.NotNull(result);
        Assert.Equal("current", result.SessionId);
    }

    [Fact]
    public void SelectReturnsNullForEmptyCollection()
    {
        Assert.Null(MediaSessionRules.Select([], currentSessionId: null));
    }

    private static MediaSessionCandidate Candidate(
        string id,
        MediaPlaybackState state,
        DateTimeOffset observedAt) =>
        new(id, id, null, state, true, true, true, observedAt);
}
