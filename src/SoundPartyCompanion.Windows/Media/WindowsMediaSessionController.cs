using SoundPartyCompanion.Core.Diagnostics;
using SoundPartyCompanion.Core.Media;
using Windows.Media.Control;

namespace SoundPartyCompanion.Windows.Media;

public sealed class WindowsMediaSessionController(IAppLogger? logger = null) : IMediaSessionController
{
    private readonly IAppLogger? _logger = logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SemaphoreSlim _controlGate = new(1, 1);
    private readonly MediaSessionTargets<GlobalSystemMediaTransportControlsSession> _targets = new();
    private GlobalSystemMediaTransportControlsSessionManager? _manager;

    public async ValueTask<SpeakerMediaSnapshot> GetSnapshotAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var selected = await GetSelectedSessionAsync(cancellationToken);
            return selected?.Snapshot ?? SpeakerMediaSnapshot.Empty(DateTimeOffset.UtcNow);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogFailure("Media session snapshot", exception);
            return SpeakerMediaSnapshot.Empty(DateTimeOffset.UtcNow);
        }
        finally
        {
            _gate.Release();
        }
    }

    public ValueTask<bool> TogglePlayPauseAsync(string sessionId, CancellationToken cancellationToken = default) =>
        ExecuteAsync(sessionId, MediaAction.TogglePlayPause, cancellationToken);

    public ValueTask<bool> SkipPreviousAsync(string sessionId, CancellationToken cancellationToken = default) =>
        ExecuteAsync(sessionId, MediaAction.SkipPrevious, cancellationToken);

    public ValueTask<bool> SkipNextAsync(string sessionId, CancellationToken cancellationToken = default) =>
        ExecuteAsync(sessionId, MediaAction.SkipNext, cancellationToken);

    private async ValueTask<bool> ExecuteAsync(
        string sessionId,
        MediaAction action,
        CancellationToken cancellationToken)
    {
        if (!await _controlGate.WaitAsync(0, cancellationToken).ConfigureAwait(false)) return false;
        try
        {
            // Control exactly the displayed target, without waiting on media metadata or reselecting.
            var manager = _manager;
            if (manager is null) return false;
            var session = _targets.Resolve(sessionId);
            if (session is null || !manager.GetSessions().Contains(session)) return false;
            var playback = session.GetPlaybackInfo();
            var controls = playback.Controls;
            bool result;
            switch (action)
            {
                case MediaAction.TogglePlayPause when playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing && controls.IsPauseEnabled:
                    result = await session.TryPauseAsync(); break;
                case MediaAction.TogglePlayPause when playback.PlaybackStatus != GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing && controls.IsPlayEnabled:
                    result = await session.TryPlayAsync(); break;
                case MediaAction.TogglePlayPause when controls.IsPlayPauseToggleEnabled:
                    result = await session.TryTogglePlayPauseAsync(); break;
                case MediaAction.SkipPrevious when controls.IsPreviousEnabled:
                    result = await session.TrySkipPreviousAsync(); break;
                case MediaAction.SkipNext when controls.IsNextEnabled:
                    result = await session.TrySkipNextAsync(); break;
                default: return false;
            }
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            LogFailure("Media session control", exception);
            return false;
        }
        finally { _controlGate.Release(); }
    }

    private async ValueTask<SelectedSession?> GetSelectedSessionAsync(
        CancellationToken cancellationToken)
    {
        _manager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        cancellationToken.ThrowIfCancellationRequested();

        var sessions = _manager.GetSessions();
        _targets.Update(sessions);
        var observedAt = DateTimeOffset.UtcNow;
        var candidates = new List<MediaSessionCandidate>(sessions.Count);
        var sessionLookup = new List<(string Id, GlobalSystemMediaTransportControlsSession Session)>();

        foreach (var session in sessions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sessionId = _targets.GetId(session)!;
            var playbackInfo = session.GetPlaybackInfo();
            var controls = playbackInfo.Controls;
            var candidate = new MediaSessionCandidate(
                sessionId,
                null,
                null,
                MapPlaybackState(playbackInfo.PlaybackStatus),
                controls.IsPlayPauseToggleEnabled ||
                    (playbackInfo.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing
                        ? controls.IsPauseEnabled : controls.IsPlayEnabled),
                controls.IsPreviousEnabled,
                controls.IsNextEnabled,
                observedAt);
            candidates.Add(candidate);
            sessionLookup.Add((sessionId, session));
        }

        var currentSessionId = _targets.GetId(_manager.GetCurrentSession());
        var selectedCandidate = MediaSessionRules.Select(candidates, currentSessionId);
        if (selectedCandidate is null)
        {
            return null;
        }

        var selectedSession = sessionLookup.First(item => string.Equals(
            item.Id,
            selectedCandidate.SessionId,
            StringComparison.Ordinal)).Session;
        var properties = await selectedSession.TryGetMediaPropertiesAsync();
        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = new SpeakerMediaSnapshot(
            selectedCandidate.SessionId,
            EmptyToNull(properties.Title),
            EmptyToNull(properties.Artist),
            selectedCandidate.PlaybackState,
            selectedCandidate.CanPlayPause,
            selectedCandidate.CanSkipPrevious,
            selectedCandidate.CanSkipNext,
            observedAt);
        return new SelectedSession(selectedSession, snapshot);
    }

    private static MediaPlaybackState MapPlaybackState(
        GlobalSystemMediaTransportControlsSessionPlaybackStatus playbackStatus) =>
        playbackStatus switch
        {
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing =>
                MediaPlaybackState.Playing,
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused =>
                MediaPlaybackState.Paused,
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Stopped =>
                MediaPlaybackState.Stopped,
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed =>
                MediaPlaybackState.Stopped,
            _ => MediaPlaybackState.None
        };

    private static string? EmptyToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private void LogFailure(string operation, Exception exception)
    {
        _logger?.Write(
            AppLogLevel.Warning,
            $"{operation} failed ({exception.GetType().Name}).");
    }

    private enum MediaAction
    {
        TogglePlayPause,
        SkipPrevious,
        SkipNext
    }

    private sealed record SelectedSession(
        GlobalSystemMediaTransportControlsSession Session,
        SpeakerMediaSnapshot Snapshot);
}
