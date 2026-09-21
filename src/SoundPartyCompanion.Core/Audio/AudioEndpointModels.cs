namespace SoundPartyCompanion.Core.Audio;

public enum AudioEndpointDirection
{
    Render,
    Capture
}

public enum SpeakerAudioEndpointKind
{
    A2dpRender,
    HandsFreeRender,
    HandsFreeCapture,
    Unknown
}

public enum AudioControlResult
{
    Success,
    SpeakerOffline,
    EndpointNotFound,
    UserActionRequired,
    Failed
}

public sealed record AudioEndpointDescriptor(
    string EndpointId,
    string DisplayName,
    AudioEndpointDirection Direction,
    SpeakerAudioEndpointKind Kind,
    bool IsActive,
    bool IsDefault);

public sealed record SpeakerAudioSnapshot(
    IReadOnlyList<AudioEndpointDescriptor> Endpoints,
    double? VolumePercent,
    bool IsMuted,
    bool IsCurrentOutput,
    DateTimeOffset ObservedAt)
{
    public bool HasA2dpEndpoint => Endpoints.Any(endpoint =>
        endpoint.Kind == SpeakerAudioEndpointKind.A2dpRender && endpoint.IsActive);

    public static SpeakerAudioSnapshot Unavailable(DateTimeOffset observedAt) =>
        new([], null, false, false, observedAt);
}

public static class AudioValueRules
{
    public static double ClampVolumePercent(double value)
    {
        if (double.IsNaN(value) || double.IsNegativeInfinity(value))
        {
            return 0;
        }

        if (double.IsPositiveInfinity(value))
        {
            return 100;
        }

        return Math.Clamp(value, 0, 100);
    }
}
