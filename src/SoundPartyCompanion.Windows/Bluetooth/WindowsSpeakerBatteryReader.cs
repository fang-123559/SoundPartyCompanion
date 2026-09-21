using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using SoundPartyCompanion.Core.Devices;

namespace SoundPartyCompanion.Windows.Bluetooth;

public sealed class WindowsSpeakerBatteryReader : ISpeakerBatteryReader
{
    private const string HandsFreeSuffix = "Hands-Free AG";

    private static DevicePropertyKey FriendlyNameProperty => new()
    {
        FormatId = Guid.Parse("a45c254e-df1c-4efd-8020-67d146a850e0"),
        PropertyId = 14
    };

    private static DevicePropertyKey HfpBatteryProperty => new()
    {
        FormatId = Guid.Parse("104ea319-6ee2-4701-bd47-8ddbf425bbe5"),
        PropertyId = 2
    };

    public ValueTask<SpeakerBatteryReading?> ReadAsync(
        SpeakerDevice device,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        cancellationToken.ThrowIfCancellationRequested();

        var deviceInfoSet = NativeBluetooth.SetupDiGetClassDevs(
            nint.Zero,
            null,
            nint.Zero,
            NativeBluetooth.DeviceInfoAllClasses | NativeBluetooth.DeviceInfoPresent);
        if (deviceInfoSet == NativeBluetooth.InvalidHandleValue)
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "Unable to enumerate present Windows devices for battery state.");
        }

        try
        {
            for (uint index = 0; ; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var deviceInfo = new SetupDeviceInfoData
                {
                    Size = Marshal.SizeOf<SetupDeviceInfoData>()
                };

                if (!NativeBluetooth.SetupDiEnumDeviceInfo(deviceInfoSet, index, ref deviceInfo))
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error != NativeBluetooth.NoMoreItems)
                    {
                        throw new Win32Exception(error, "Windows device enumeration failed.");
                    }

                    return ValueTask.FromResult<SpeakerBatteryReading?>(null);
                }

                var friendlyName = ReadStringProperty(
                    deviceInfoSet,
                    ref deviceInfo,
                    FriendlyNameProperty);
                if (!MatchesSpeaker(device.DisplayName, friendlyName))
                {
                    continue;
                }

                var percent = ReadByteProperty(
                    deviceInfoSet,
                    ref deviceInfo,
                    HfpBatteryProperty);
                if (percent is > 100)
                {
                    continue;
                }

                return ValueTask.FromResult<SpeakerBatteryReading?>(
                    percent.HasValue
                        ? new SpeakerBatteryReading(percent.Value, DateTimeOffset.UtcNow)
                        : null);
            }
        }
        finally
        {
            NativeBluetooth.SetupDiDestroyDeviceInfoList(deviceInfoSet);
        }
    }

    private static bool MatchesSpeaker(string speakerName, string? candidateName)
    {
        if (string.IsNullOrWhiteSpace(candidateName) ||
            !candidateName.EndsWith(HandsFreeSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var candidateBaseName = candidateName[..^HandsFreeSuffix.Length];
        return string.Equals(
            NormalizeName(speakerName),
            NormalizeName(candidateBaseName),
            StringComparison.Ordinal);
    }

    private static string NormalizeName(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToUpperInvariant(character));
            }
        }

        return builder.ToString();
    }

    private static string? ReadStringProperty(
        nint deviceInfoSet,
        ref SetupDeviceInfoData deviceInfo,
        DevicePropertyKey propertyKey)
    {
        var buffer = new byte[1024];
        if (!NativeBluetooth.SetupDiGetDeviceProperty(
                deviceInfoSet,
                ref deviceInfo,
                ref propertyKey,
                out _,
                buffer,
                (uint)buffer.Length,
                out var requiredSize,
                0))
        {
            return null;
        }

        var byteCount = Math.Min((int)requiredSize, buffer.Length);
        return Encoding.Unicode.GetString(buffer, 0, byteCount).TrimEnd('\0');
    }

    private static byte? ReadByteProperty(
        nint deviceInfoSet,
        ref SetupDeviceInfoData deviceInfo,
        DevicePropertyKey propertyKey)
    {
        var buffer = new byte[1];
        return NativeBluetooth.SetupDiGetDeviceProperty(
            deviceInfoSet,
            ref deviceInfo,
            ref propertyKey,
            out _,
            buffer,
            1,
            out var requiredSize,
            0) && requiredSize == 1
            ? buffer[0]
            : null;
    }
}
