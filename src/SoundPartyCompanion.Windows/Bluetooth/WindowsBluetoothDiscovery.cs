using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using SoundPartyCompanion.Core.Devices;
using SoundPartyCompanion.Core.Diagnostics;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using Windows.Devices.Radios;

namespace SoundPartyCompanion.Windows.Bluetooth;

public sealed class WindowsBluetoothDiscovery(IAppLogger? logger = null) : ISpeakerDiscovery
{
    private readonly IAppLogger? _logger = logger;

    public ValueTask<SpeakerDiscoverySnapshot> DiscoverAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var observedAt = DateTimeOffset.UtcNow;

        try
        {
            var primaryPnpDevices = EnumeratePnpBluetoothDevices(observedAt, cancellationToken);
            var nativeDiscovery = new NativeDiscoveryResult(false, []);

            try
            {
                nativeDiscovery = EnumeratePairedDevices(observedAt, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger?.Write(
                    AppLogLevel.Warning,
                    $"Classic Bluetooth radio enumeration was skipped ({exception.GetType().Name}).");
            }

            var devices = MergeDevices(
                nativeDiscovery.Devices,
                primaryPnpDevices);
            var bluetoothAvailable = nativeDiscovery.HasRadio ||
                devices.Any(device => device.IsConnected);

            return ValueTask.FromResult(new SpeakerDiscoverySnapshot(
                bluetoothAvailable,
                devices,
                observedAt));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger?.Write(
                AppLogLevel.Warning,
                $"Bluetooth discovery failed ({exception.GetType().Name}).");

            return ValueTask.FromResult(new SpeakerDiscoverySnapshot(false, [], observedAt));
        }
    }

    private static async ValueTask<BluetoothPowerState> GetBluetoothPowerStateAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var radios = await Radio.GetRadiosAsync();
            cancellationToken.ThrowIfCancellationRequested();
            var bluetoothRadio = radios.FirstOrDefault(radio => radio.Kind == RadioKind.Bluetooth);

            if (bluetoothRadio is null)
            {
                return BluetoothPowerState.Unknown;
            }

            return bluetoothRadio.State == RadioState.On
                ? BluetoothPowerState.On
                : BluetoothPowerState.Off;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return BluetoothPowerState.Unknown;
        }
    }

    private static NativeDiscoveryResult EnumeratePairedDevices(
        DateTimeOffset observedAt,
        CancellationToken cancellationToken)
    {
        var devices = new Dictionary<string, SpeakerDevice>(StringComparer.Ordinal);
        var radioParams = new BluetoothFindRadioParams
        {
            Size = Marshal.SizeOf<BluetoothFindRadioParams>()
        };

        var findRadio = NativeBluetooth.BluetoothFindFirstRadio(ref radioParams, out var radio);
        if (findRadio == nint.Zero)
        {
            return new NativeDiscoveryResult(false, []);
        }

        try
        {
            do
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    EnumerateRadioDevices(radio, observedAt, devices, cancellationToken);
                }
                finally
                {
                    if (radio != nint.Zero)
                    {
                        NativeBluetooth.CloseHandle(radio);
                    }
                }
            }
            while (NativeBluetooth.BluetoothFindNextRadio(findRadio, out radio));
        }
        finally
        {
            NativeBluetooth.BluetoothFindRadioClose(findRadio);
        }

        var orderedDevices = devices.Values
            .OrderByDescending(device => device.IsConnected)
            .ThenBy(device => device.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        return new NativeDiscoveryResult(true, orderedDevices);
    }

    private static async ValueTask<IReadOnlyList<SpeakerDevice>> EnumeratePairedDevicesAsync(
        DateTimeOffset observedAt,
        CancellationToken cancellationToken)
    {
        var selector = BluetoothDevice.GetDeviceSelectorFromPairingState(true);
        var deviceInformation = await DeviceInformation.FindAllAsync(selector);
        cancellationToken.ThrowIfCancellationRequested();
        var devices = new List<SpeakerDevice>(deviceInformation.Count);

        foreach (var information in deviceInformation)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bluetoothDevice = await BluetoothDevice.FromIdAsync(information.Id);
            if (bluetoothDevice is null)
            {
                continue;
            }

            using (bluetoothDevice)
            {
                var address = bluetoothDevice.BluetoothAddress;
                var connected = bluetoothDevice.ConnectionStatus == BluetoothConnectionStatus.Connected;
                var nativeDevice = new BluetoothDeviceInfo
                {
                    Size = Marshal.SizeOf<BluetoothDeviceInfo>(),
                    Address = address,
                    Name = string.Empty
                };

                if (NativeBluetooth.BluetoothGetDeviceInfo(nint.Zero, ref nativeDevice) == 0)
                {
                    connected = nativeDevice.Connected;
                }

                var name = FirstNonEmpty(
                    information.Name,
                    bluetoothDevice.Name,
                    nativeDevice.Name,
                    "Bluetooth audio device");
                var key = address == 0
                    ? CreateDeviceKey(information.Id)
                    : CreateDeviceKey(address);

                devices.Add(new SpeakerDevice(
                    key,
                    name,
                    information.Pairing.IsPaired,
                    connected,
                    observedAt));
            }
        }

        return devices;
    }

    private static async ValueTask<IReadOnlyList<SpeakerDevice>> EnumerateAssociationEndpointsAsync(
        DateTimeOffset observedAt,
        CancellationToken cancellationToken)
    {
        const string selector =
            "System.Devices.Aep.ProtocolId:=\"{E0CBF06C-CD8B-4647-BB8A-263B43F0F974}\"";
        string[] requestedProperties =
        [
            "System.Devices.Aep.IsConnected",
            "System.Devices.Aep.IsPaired",
            "System.Devices.Aep.DeviceAddress"
        ];

        var endpoints = await DeviceInformation.FindAllAsync(
            selector,
            requestedProperties,
            DeviceInformationKind.AssociationEndpoint);
        cancellationToken.ThrowIfCancellationRequested();
        var devices = new List<SpeakerDevice>(endpoints.Count);

        foreach (var endpoint in endpoints)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var paired = ReadBooleanProperty(endpoint, "System.Devices.Aep.IsPaired") ||
                endpoint.Pairing.IsPaired;

            if (!paired)
            {
                continue;
            }

            var connected = ReadBooleanProperty(endpoint, "System.Devices.Aep.IsConnected");
            var address = ReadBluetoothAddress(endpoint);

            if (address is not null)
            {
                var nativeDevice = new BluetoothDeviceInfo
                {
                    Size = Marshal.SizeOf<BluetoothDeviceInfo>(),
                    Address = address.Value,
                    Name = string.Empty
                };

                if (NativeBluetooth.BluetoothGetDeviceInfo(nint.Zero, ref nativeDevice) == 0)
                {
                    connected = nativeDevice.Connected;
                }
            }

            var key = address is null
                ? CreateDeviceKey(endpoint.Id)
                : CreateDeviceKey(address.Value);
            var name = FirstNonEmpty(endpoint.Name, "Bluetooth audio device");

            devices.Add(new SpeakerDevice(
                key,
                name,
                paired,
                connected,
                observedAt));
        }

        return devices;
    }

    private static IReadOnlyList<SpeakerDevice> EnumeratePnpBluetoothDevices(
        DateTimeOffset observedAt,
        CancellationToken cancellationToken)
    {
        var deviceInfoSet = NativeBluetooth.SetupDiGetClassDevs(
            nint.Zero,
            "BTHENUM",
            nint.Zero,
            NativeBluetooth.DeviceInfoAllClasses | NativeBluetooth.DeviceInfoPresent);

        if (deviceInfoSet == NativeBluetooth.InvalidHandleValue)
        {
            return [];
        }

        var devices = new Dictionary<string, SpeakerDevice>(StringComparer.Ordinal);

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
                    break;
                }

                var instanceId = new StringBuilder(capacity: 512);
                if (!NativeBluetooth.SetupDiGetDeviceInstanceId(
                        deviceInfoSet,
                        ref deviceInfo,
                        instanceId,
                        instanceId.Capacity,
                        out _))
                {
                    continue;
                }

                var address = TryReadAddressFromInstanceId(instanceId.ToString());
                if (address is null)
                {
                    continue;
                }

                var nativeDevice = new BluetoothDeviceInfo
                {
                    Size = Marshal.SizeOf<BluetoothDeviceInfo>(),
                    Address = address.Value,
                    Name = string.Empty
                };

                if (NativeBluetooth.BluetoothGetDeviceInfo(nint.Zero, ref nativeDevice) != 0 ||
                    (!nativeDevice.Authenticated && !nativeDevice.Remembered))
                {
                    continue;
                }

                var key = CreateDeviceKey(address.Value);
                var name = FirstNonEmpty(nativeDevice.Name, "Bluetooth audio device");
                devices[key] = new SpeakerDevice(
                    key,
                    name,
                    IsPaired: true,
                    nativeDevice.Connected,
                    observedAt);
            }
        }
        finally
        {
            NativeBluetooth.SetupDiDestroyDeviceInfoList(deviceInfoSet);
        }

        return devices.Values.ToArray();
    }

    private static IReadOnlyList<SpeakerDevice> MergeDevices(
        params IReadOnlyList<SpeakerDevice>[] sources)
    {
        var merged = new Dictionary<string, SpeakerDevice>(StringComparer.Ordinal);

        foreach (var source in sources)
        {
            foreach (var device in source)
            {
                merged[device.DeviceKey] = device;
            }
        }

        return merged.Values
            .OrderByDescending(device => device.IsConnected)
            .ThenBy(device => device.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private static void EnumerateRadioDevices(
        nint radio,
        DateTimeOffset observedAt,
        IDictionary<string, SpeakerDevice> devices,
        CancellationToken cancellationToken)
    {
        var search = new BluetoothDeviceSearchParams
        {
            Size = Marshal.SizeOf<BluetoothDeviceSearchParams>(),
            ReturnAuthenticated = true,
            ReturnRemembered = true,
            ReturnUnknown = false,
            ReturnConnected = true,
            IssueInquiry = false,
            TimeoutMultiplier = 0,
            RadioHandle = radio
        };

        var nativeDevice = new BluetoothDeviceInfo
        {
            Size = Marshal.SizeOf<BluetoothDeviceInfo>(),
            Name = string.Empty
        };

        var findDevice = NativeBluetooth.BluetoothFindFirstDevice(ref search, ref nativeDevice);
        if (findDevice == nint.Zero)
        {
            return;
        }

        try
        {
            do
            {
                cancellationToken.ThrowIfCancellationRequested();
                var key = CreateDeviceKey(nativeDevice.Address);
                var name = string.IsNullOrWhiteSpace(nativeDevice.Name)
                    ? "Bluetooth audio device"
                    : nativeDevice.Name.Trim();

                devices[key] = new SpeakerDevice(
                    key,
                    name,
                    nativeDevice.Authenticated || nativeDevice.Remembered,
                    nativeDevice.Connected,
                    observedAt);

                nativeDevice.Size = Marshal.SizeOf<BluetoothDeviceInfo>();
            }
            while (NativeBluetooth.BluetoothFindNextDevice(findDevice, ref nativeDevice));
        }
        finally
        {
            NativeBluetooth.BluetoothFindDeviceClose(findDevice);
        }
    }

    private static string CreateDeviceKey(ulong address)
    {
        Span<byte> addressBytes = stackalloc byte[sizeof(ulong)];
        BinaryPrimitives.WriteUInt64LittleEndian(addressBytes, address);

        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(addressBytes, hash);
        return $"bt-{Convert.ToHexString(hash[..16])}";
    }

    private static string CreateDeviceKey(string deviceId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(deviceId));
        return $"bt-{Convert.ToHexString(hash.AsSpan(0, 16))}";
    }

    private static string FirstNonEmpty(params string?[] candidates) =>
        candidates.First(candidate => !string.IsNullOrWhiteSpace(candidate))!.Trim();

    private static bool ReadBooleanProperty(DeviceInformation device, string propertyName) =>
        device.Properties.TryGetValue(propertyName, out var value) &&
        value is bool boolean &&
        boolean;

    private static ulong? ReadBluetoothAddress(DeviceInformation device)
    {
        if (!device.Properties.TryGetValue("System.Devices.Aep.DeviceAddress", out var value) ||
            value is not string address)
        {
            return null;
        }

        var normalized = new string(address.Where(char.IsAsciiHexDigit).ToArray());
        return ulong.TryParse(
            normalized,
            NumberStyles.HexNumber,
            CultureInfo.InvariantCulture,
            out var parsed)
            ? parsed
            : null;
    }

    private static ulong? TryReadAddressFromInstanceId(string instanceId)
    {
        const string marker = "DEV_";
        var markerIndex = instanceId.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
        {
            return null;
        }

        var addressStart = markerIndex + marker.Length;
        if (instanceId.Length < addressStart + 12)
        {
            return null;
        }

        var addressText = instanceId.AsSpan(addressStart, 12);
        return ulong.TryParse(
            addressText,
            NumberStyles.HexNumber,
            CultureInfo.InvariantCulture,
            out var address)
            ? address
            : null;
    }

    private enum BluetoothPowerState
    {
        Unknown,
        Off,
        On
    }

    private readonly record struct NativeDiscoveryResult(
        bool HasRadio,
        IReadOnlyList<SpeakerDevice> Devices);
}
