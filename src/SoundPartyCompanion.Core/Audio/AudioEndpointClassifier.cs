using System.Text;

namespace SoundPartyCompanion.Core.Audio;

public static class AudioEndpointClassifier
{
    private static readonly string[] SupportedSpeakerNames = ["Xiaomi蓝牙音箱Pro", "Xiaomi Sound Party"];

    private static readonly string[] HandsFreeMarkers =
    [
        "handsfree",
        "agaudio",
        "免提",
        "通话"
    ];

    public static bool NamesMatch(string deviceName, string endpointName)
    {
        var normalizedDevice = Normalize(deviceName);
        var normalizedEndpoint = Normalize(endpointName);

        return normalizedDevice.Length >= 4 &&
            normalizedEndpoint.Contains(normalizedDevice, StringComparison.Ordinal);
    }

    public static bool MatchesSupportedSpeakerAlias(string deviceName, string endpointName)
    {
        var normalizedDevice = Normalize(deviceName);
        return SupportedSpeakerNames.Any(name => Normalize(name) == normalizedDevice) &&
            IsSupportedSpeakerEndpointName(endpointName);
    }

    public static bool IsSupportedSpeakerEndpointName(string endpointName) =>
        SupportedSpeakerNames.Any(name => NamesMatch(name, endpointName));

    public static SpeakerAudioEndpointKind Classify(
        string endpointName,
        AudioEndpointDirection direction,
        bool isSameDeviceContainer = false)
    {
        var normalizedName = Normalize(endpointName);
        var isHandsFree = HandsFreeMarkers.Any(marker =>
            normalizedName.Contains(marker, StringComparison.Ordinal)) ||
            (!isSameDeviceContainer &&
             (normalizedName.Contains("headset", StringComparison.Ordinal) ||
              normalizedName.Contains("耳麦", StringComparison.Ordinal)));

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
