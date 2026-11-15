using SoundPartyCompanion.Core.Devices;

namespace SoundPartyCompanion.Core.Audio;

public interface IAudioEndpointController
{
    ValueTask<SpeakerAudioSnapshot> GetSnapshotAsync(
        SpeakerDevice device,
        CancellationToken cancellationToken = default);

    IDisposable? WatchVolume(string endpointId, Action<double, bool> volumeChanged) => null;

    ValueTask<AudioControlResult> SetVolumeAsync(
        SpeakerDevice device,
        double volumePercent,
        CancellationToken cancellationToken = default);

    ValueTask<AudioControlResult> SetMutedAsync(
        SpeakerDevice device,
        bool isMuted,
        CancellationToken cancellationToken = default);

    ValueTask<AudioControlResult> SetAsDefaultOutputAsync(
        SpeakerDevice device,
        CancellationToken cancellationToken = default);
}

public static class AudioEndpointSelection
{
    public static IReadOnlyList<AudioEndpointDescriptor> MatchSpeakerEndpoints(
        string deviceName, IReadOnlyList<AudioEndpointDescriptor> endpoints)
    {
        var directMatches = endpoints.Where(endpoint =>
            AudioEndpointClassifier.NamesMatch(deviceName, endpoint.DisplayName)).ToArray();
        if (directMatches.Any(endpoint => endpoint.Kind == SpeakerAudioEndpointKind.A2dpRender))
        {
            return directMatches;
        }

        var aliases = endpoints.Where(endpoint =>
            endpoint.Kind == SpeakerAudioEndpointKind.A2dpRender &&
            AudioEndpointClassifier.MatchesSupportedSpeakerAlias(deviceName, endpoint.DisplayName)).Take(2).ToArray();

        return aliases is [{ IsActive: true }] ? [.. directMatches, aliases[0]] : directMatches;
    }

    public static AudioEndpointDescriptor? SelectA2dpEndpoint(IEnumerable<AudioEndpointDescriptor> endpoints) =>
        endpoints.FirstOrDefault(endpoint =>
            endpoint.Kind == SpeakerAudioEndpointKind.A2dpRender && endpoint.IsActive && endpoint.IsDefault)
        ?? endpoints.FirstOrDefault(endpoint =>
            endpoint.Kind == SpeakerAudioEndpointKind.A2dpRender && endpoint.IsActive);
}
