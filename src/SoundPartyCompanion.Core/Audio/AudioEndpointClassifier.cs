using System.Text;

namespace SoundPartyCompanion.Core.Audio;

public static class AudioEndpointClassifier
{
    private static readonly string[] HandsFreeMarkers =
    [
        "handsfree",
        "headset",
        "agaudio",
        "免提",
        "通话",
        "耳麦"
    ];

    public static bool NamesMatch(string deviceName, string endpointName)
    {
        var normalizedDevice = Normalize(deviceName);
        var normalizedEndpoint = Normalize(endpointName);

        return normalizedDevice.Length >= 4 &&
            normalizedEndpoint.Contains(normalizedDevice, StringComparison.Ordinal);
    }

    public static SpeakerAudioEndpointKind Classify(
        string endpointName,
        AudioEndpointDirection direction)
    {
        var normalizedName = Normalize(endpointName);
        var isHandsFree = HandsFreeMarkers.Any(marker =>
            normalizedName.Contains(marker, StringComparison.Ordinal));

        if (direction == AudioEndpointDirection.Capture)
        {
            return SpeakerAudioEndpointKind.HandsFreeCapture;
        }

        return isHandsFree
            ? SpeakerAudioEndpointKind.HandsFreeRender
            : SpeakerAudioEndpointKind.A2dpRender;
    }

    private static string Normalize(string value)
    {
        var builder = new StringBuilder(value.Length);

        foreach (var character in value)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return builder.ToString();
    }
}
