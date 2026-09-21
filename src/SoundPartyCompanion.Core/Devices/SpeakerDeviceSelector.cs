namespace SoundPartyCompanion.Core.Devices;

public static class SpeakerDeviceSelector
{
    public static SpeakerSelection? Select(
        SpeakerDiscoverySnapshot discovery,
        SpeakerSelection? savedSelection,
        IEnumerable<string> preferredNames)
    {
        ArgumentNullException.ThrowIfNull(discovery);
        ArgumentNullException.ThrowIfNull(preferredNames);

        var pairedDevices = discovery.Devices
            .Where(device => device.IsPaired)
            .ToArray();

        if (savedSelection is not null)
        {
            var savedDevice = pairedDevices.FirstOrDefault(device =>
                string.Equals(
                    device.DeviceKey,
                    savedSelection.DeviceKey,
                    StringComparison.Ordinal));

            if (savedDevice is not null)
            {
                return ToSelection(savedDevice);
            }
        }

        foreach (var preferredName in preferredNames.Where(name => !string.IsNullOrWhiteSpace(name)))
        {
            var matches = pairedDevices
                .Where(device => string.Equals(
                    device.DisplayName.Trim(),
                    preferredName.Trim(),
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (matches.Length == 1)
            {
                return ToSelection(matches[0]);
            }

            var connectedMatches = matches.Where(device => device.IsConnected).ToArray();
            if (connectedMatches.Length == 1)
            {
                return ToSelection(connectedMatches[0]);
            }
        }

        return null;
    }

    private static SpeakerSelection ToSelection(SpeakerDevice device) =>
        new(device.DeviceKey, device.DisplayName);
}
