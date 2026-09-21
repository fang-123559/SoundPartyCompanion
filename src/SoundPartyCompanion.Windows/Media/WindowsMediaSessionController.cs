using SoundPartyCompanion.Core.Diagnostics;
using SoundPartyCompanion.Core.Media;
using Windows.Media.Control;

namespace SoundPartyCompanion.Windows.Media;

public sealed class WindowsMediaSessionController(IAppLogger? logger = null) : IMediaSessionController
{
    private readonly IAppLogger? _logger = logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
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

    public ValueTask<bool> TogglePlayPauseAsync(CancellationToken cancellationToken = default) =>
        ExecuteAsync(MediaAction.TogglePlayPause, cancellationToken);

    public ValueTask<bool> SkipPreviousAsync(CancellationToken cancellationToken = default) =>
        ExecuteAsync(MediaAction.SkipPrevious, cancellationToken);

    public ValueTask<bool> SkipNextAsync(CancellationToken cancellationToken = default) =>
        ExecuteAsync(MediaAction.SkipNext, cancellationToken);

    private async ValueTask<bool> ExecuteAsync(
        MediaAction action,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var selected = await GetSelectedSessionAsync(cancellationToken);
            if (selected is null)
            {
                return false;
            }

            var controls = selected.Session.GetPlaybackInfo().Controls;
            var isAllowed = action switch
            {
                MediaAction.TogglePlayPause => controls.IsPlayEnabled || controls.IsPauseEnabled,
                MediaAction.SkipPrevious => controls.IsPreviousEnabled,
                MediaAction.SkipNext => controls.IsNextEnabled,
                _ => false
            };

            if (!isAllowed)
            {
                return false;
            }

            var result = action switch
            {
                MediaAction.TogglePlayPause =>
                    await selected.Session.TryTogglePlayPauseAsync(),
                MediaAction.SkipPrevious =>
                    await selected.Session.TrySkipPreviousAsync(),
                MediaAction.SkipNext =>
                    await selected.Session.TrySkipNextAsync(),
                _ => false
            };

            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogFailure("Media session control", exception);
            return false;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async ValueTask<SelectedSession?> GetSelectedSessionAsync(
        CancellationToken cancellationToken)
    {
        _manager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        cancellationToken.ThrowIfCancellationRequested();

        var sessions = _manager.GetSessions();
        var observedAt = DateTimeOffset.UtcNow;
        var candidates = new List<MediaSessionCandidate>(sessions.Count);
        var sessionLookup = new List<(string Id, GlobalSystemMediaTransportControlsSession Session)>();

        foreach (var session in sessions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sessionId = session.SourceAppUserModelId;
            var playbackInfo = session.GetPlaybackInfo();
            var controls = playbackInfo.Controls;
            var properties = await session.TryGetMediaPropertiesAsync();
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = new MediaSessionCandidate(
                sessionId,
                EmptyToNull(properties.Title),
                EmptyToNull(properties.Artist),
                MapPlaybackState(playbackInfo.PlaybackStatus),
                controls.IsPlayEnabled || controls.IsPauseEnabled,
                controls.IsPreviousEnabled,
                controls.IsNextEnabled,
                observedAt);
            candidates.Add(candidate);
            sessionLookup.Add((sessionId, session));
        }

        var currentSessionId = _manager.GetCurrentSession()?.SourceAppUserModelId;
        var selectedCandidate = MediaSessionRules.Select(candidates, currentSessionId);
        if (selectedCandidate is null)
        {
            return null;
        }

        var selectedSession = sessionLookup.First(item => string.Equals(
            item.Id,
            selectedCandidate.SessionId,
            StringComparison.Ordinal)).Session;
        var snapshot = new SpeakerMediaSnapshot(
            selectedCandidate.SessionId,
            selectedCandidate.TrackTitle,
            selectedCandidate.TrackArtist,
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
