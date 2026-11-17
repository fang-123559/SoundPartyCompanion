namespace SoundPartyCompanion.Core.Media;

public interface IMediaSessionController
{
    ValueTask<SpeakerMediaSnapshot> GetSnapshotAsync(
        CancellationToken cancellationToken = default);

    ValueTask<bool> TogglePlayPauseAsync(string sessionId, CancellationToken cancellationToken = default);

    ValueTask<bool> SkipPreviousAsync(string sessionId, CancellationToken cancellationToken = default);

    ValueTask<bool> SkipNextAsync(string sessionId, CancellationToken cancellationToken = default);
}
