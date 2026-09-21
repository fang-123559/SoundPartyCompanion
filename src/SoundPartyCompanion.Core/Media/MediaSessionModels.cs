namespace SoundPartyCompanion.Core.Media;

public enum MediaPlaybackState
{
    None,
    Paused,
    Playing,
    Stopped
}

public sealed record MediaSessionCandidate(
    string SessionId,
    string? TrackTitle,
    string? TrackArtist,
    MediaPlaybackState PlaybackState,
    bool CanPlayPause,
    bool CanSkipPrevious,
    bool CanSkipNext,
    DateTimeOffset ObservedAt);

public sealed record SpeakerMediaSnapshot(
    string? SessionId,
    string? TrackTitle,
    string? TrackArtist,
    MediaPlaybackState PlaybackState,
    bool CanPlayPause,
    bool CanSkipPrevious,
    bool CanSkipNext,
    DateTimeOffset ObservedAt)
{
    public bool HasSession => !string.IsNullOrWhiteSpace(SessionId);

    public bool IsPlaying => PlaybackState == MediaPlaybackState.Playing;

    public static SpeakerMediaSnapshot Empty(DateTimeOffset observedAt) =>
        new(null, null, null, MediaPlaybackState.None, false, false, false, observedAt);
}

public static class MediaSessionRules
{
    public static MediaSessionCandidate? Select(
        IEnumerable<MediaSessionCandidate> candidates,
        string? currentSessionId)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        var sessions = candidates.ToArray();

        return sessions
            .Where(session => session.PlaybackState == MediaPlaybackState.Playing)
            .OrderByDescending(session => session.ObservedAt)
            .FirstOrDefault()
            ?? sessions.FirstOrDefault(session => string.Equals(
                session.SessionId,
                currentSessionId,
                StringComparison.Ordinal))
            ?? sessions.OrderByDescending(session => session.ObservedAt).FirstOrDefault();
    }
}
