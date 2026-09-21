namespace SoundPartyCompanion.Core.Media;

public interface IMediaSessionController
{
    ValueTask<SpeakerMediaSnapshot> GetSnapshotAsync(
        CancellationToken cancellationToken = default);

    ValueTask<bool> TogglePlayPauseAsync(CancellationToken cancellationToken = default);

    ValueTask<bool> SkipPreviousAsync(CancellationToken cancellationToken = default);

    ValueTask<bool> SkipNextAsync(CancellationToken cancellationToken = default);
}
