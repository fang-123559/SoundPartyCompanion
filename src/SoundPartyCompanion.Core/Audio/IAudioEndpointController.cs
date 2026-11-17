using SoundPartyCompanion.Core.Devices;

namespace SoundPartyCompanion.Core.Audio;

public interface IAudioEndpointController
{
    // This query must not activate or read endpoint volume controls.
    ValueTask<bool> IsCurrentOutputAsync(
        SpeakerDevice device,
        CancellationToken cancellationToken = default);

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
        string deviceName, IReadOnlyList<AudioEndpointDescriptor> endpoints, Guid? speakerContainerId = null)
    {
        var candidates = endpoints;
        if (speakerContainerId.HasValue && speakerContainerId != Guid.Empty)
        {
            var sameContainer = endpoints.Where(endpoint => endpoint.ContainerId == speakerContainerId)
                .Select(endpoint => endpoint with
                {
                    Kind = AudioEndpointClassifier.Classify(endpoint.DisplayName, endpoint.Direction, isSameDeviceContainer: true)
                })
                .ToArray();
            if (sameContainer.Length > 0) return sameContainer;

            // A known mismatch must never fall back to a name match.
            candidates = endpoints.Where(endpoint => endpoint.ContainerId is null || endpoint.ContainerId == Guid.Empty).ToArray();
        }

        var directMatches = candidates.Where(endpoint =>
            AudioEndpointClassifier.NamesMatch(deviceName, endpoint.DisplayName)).ToArray();
        if (SelectA2dpEndpoint(directMatches) is not null)
        {
            return directMatches;
        }

        var aliases = candidates.Where(endpoint =>
            endpoint.Kind == SpeakerAudioEndpointKind.A2dpRender && endpoint.IsActive &&
            AudioEndpointClassifier.MatchesSupportedSpeakerAlias(deviceName, endpoint.DisplayName)).Take(2).ToArray();

        return aliases is [var alias] ? [.. directMatches, alias] : directMatches;
    }

    public static AudioEndpointDescriptor? SelectA2dpEndpoint(IEnumerable<AudioEndpointDescriptor> endpoints) =>
        endpoints.FirstOrDefault(endpoint =>
            endpoint.Kind == SpeakerAudioEndpointKind.A2dpRender && endpoint.IsActive && endpoint.IsDefault)
        ?? endpoints.FirstOrDefault(endpoint =>
            endpoint.Kind == SpeakerAudioEndpointKind.A2dpRender && endpoint.IsActive);
}
