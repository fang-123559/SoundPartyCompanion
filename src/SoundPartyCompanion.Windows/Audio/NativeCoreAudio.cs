using System.Runtime.InteropServices;

namespace SoundPartyCompanion.Windows.Audio;

internal enum AudioDataFlow
{
    Render,
    Capture,
    All
}

internal enum AudioDeviceRole
{
    Console,
    Multimedia,
    Communications
}

[Flags]
internal enum AudioDeviceState : uint
{
    Active = 0x00000001,
    Disabled = 0x00000002,
    NotPresent = 0x00000004,
    Unplugged = 0x00000008,
    All = Active | Disabled | NotPresent | Unplugged
}

[Flags]
internal enum ClassContext : uint
{
    InProcessServer = 0x1,
    InProcessHandler = 0x2,
    LocalServer = 0x4,
    RemoteServer = 0x10,
    All = InProcessServer | InProcessHandler | LocalServer | RemoteServer
}

[StructLayout(LayoutKind.Sequential)]
public struct PropertyKey(Guid formatId, uint propertyId)
{
    public Guid FormatId = formatId;
    public uint PropertyId = propertyId;
}

// On x64 the native union contains counted pointers and occupies 16 bytes at offset 8.
[StructLayout(LayoutKind.Explicit, Size = 24)]
internal struct PropVariant
{
    [FieldOffset(0)]
    public ushort VariantType;

    [FieldOffset(8)]
    public nint PointerValue;
}

[ComImport]
[Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
internal sealed class MultimediaDeviceEnumeratorComObject;

[ComImport]
[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMultimediaDeviceEnumerator
{
    [PreserveSig]
    int EnumAudioEndpoints(
        AudioDataFlow dataFlow,
        AudioDeviceState stateMask,
        out IMultimediaDeviceCollection devices);

    [PreserveSig]
    int GetDefaultAudioEndpoint(
        AudioDataFlow dataFlow,
        AudioDeviceRole role,
        out IMultimediaDevice device);

    [PreserveSig]
    int GetDevice(
        [MarshalAs(UnmanagedType.LPWStr)] string endpointId,
        out IMultimediaDevice device);

    [PreserveSig]
    int RegisterEndpointNotificationCallback(nint callback);

    [PreserveSig]
    int UnregisterEndpointNotificationCallback(nint callback);
}

[ComImport]
[Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMultimediaDeviceCollection
{
    [PreserveSig]
    int GetCount(out uint count);

    [PreserveSig]
    int Item(uint index, out IMultimediaDevice device);
}

[ComVisible(true)]
[Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IMMNotificationClient
{
    [PreserveSig]
    int OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string endpointId, uint state);

    [PreserveSig]
    int OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string endpointId);

    [PreserveSig]
    int OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string endpointId);

    [PreserveSig]
    int OnDefaultDeviceChanged(int flow, int role, [MarshalAs(UnmanagedType.LPWStr)] string? endpointId);

    [PreserveSig]
    int OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string endpointId, PropertyKey key);
}

[ComImport]
[Guid("1BE09788-6894-4089-8586-9A2A6C265AC5")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMEndpoint
{
    [PreserveSig]
    int GetDataFlow(out AudioDataFlow flow);
}

[ComImport]
[Guid("D666063F-1587-4E43-81F1-B948E807363F")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMultimediaDevice
{
    [PreserveSig]
    int Activate(
        ref Guid interfaceId,
        ClassContext classContext,
        nint activationParameters,
        [MarshalAs(UnmanagedType.Interface)] out object activatedInterface);

    [PreserveSig]
    int OpenPropertyStore(uint storageMode, out IAudioPropertyStore properties);

    [PreserveSig]
    int GetId([MarshalAs(UnmanagedType.LPWStr)] out string endpointId);

    [PreserveSig]
    int GetState(out AudioDeviceState state);
}

[ComImport]
[Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioPropertyStore
{
    [PreserveSig]
    int GetCount(out uint propertyCount);

    [PreserveSig]
    int GetAt(uint propertyIndex, out PropertyKey key);

    [PreserveSig]
    int GetValue(ref PropertyKey key, out PropVariant value);

    [PreserveSig]
    int SetValue(ref PropertyKey key, ref PropVariant value);

    [PreserveSig]
    int Commit();
}

[ComImport]
[Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioEndpointVolume
{
    [PreserveSig]
    int RegisterControlChangeNotify([MarshalAs(UnmanagedType.Interface)] IAudioEndpointVolumeCallback callback);

    [PreserveSig]
    int UnregisterControlChangeNotify([MarshalAs(UnmanagedType.Interface)] IAudioEndpointVolumeCallback callback);

    [PreserveSig]
    int GetChannelCount(out uint channelCount);

    [PreserveSig]
    int SetMasterVolumeLevel(float levelInDecibels, ref Guid eventContext);

    [PreserveSig]
    int SetMasterVolumeLevelScalar(float level, ref Guid eventContext);

    [PreserveSig]
    int GetMasterVolumeLevel(out float levelInDecibels);

    [PreserveSig]
    int GetMasterVolumeLevelScalar(out float level);

    [PreserveSig]
    int SetChannelVolumeLevel(uint channelNumber, float levelInDecibels, ref Guid eventContext);

    [PreserveSig]
    int SetChannelVolumeLevelScalar(uint channelNumber, float level, ref Guid eventContext);

    [PreserveSig]
    int GetChannelVolumeLevel(uint channelNumber, out float levelInDecibels);

    [PreserveSig]
    int GetChannelVolumeLevelScalar(uint channelNumber, out float level);

    [PreserveSig]
    int SetMute([MarshalAs(UnmanagedType.Bool)] bool isMuted, ref Guid eventContext);

    [PreserveSig]
    int GetMute([MarshalAs(UnmanagedType.Bool)] out bool isMuted);

    [PreserveSig]
    int GetVolumeStepInfo(out uint currentStep, out uint stepCount);

    [PreserveSig]
    int VolumeStepUp(ref Guid eventContext);

    [PreserveSig]
    int VolumeStepDown(ref Guid eventContext);

    [PreserveSig]
    int QueryHardwareSupport(out uint hardwareSupportMask);

    [PreserveSig]
    int GetVolumeRange(out float minimumDecibels, out float maximumDecibels, out float incrementDecibels);
}

[ComVisible(true)]
[Guid("657804FA-D6AD-4496-8A60-352752AF4F89")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IAudioEndpointVolumeCallback
{
    [PreserveSig]
    int OnNotify(nint notification);
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct AudioVolumeNotification
{
    public readonly Guid EventContext;
    public readonly int IsMuted;
    public readonly float MasterVolume;
}

[ComImport]
[Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
internal sealed class PolicyConfigClientComObject;

[ComImport]
[Guid("F8679F50-850A-41CF-9C72-430F290290C8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPolicyConfig
{
    [PreserveSig]
    int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string endpointId, out nint format);

    [PreserveSig]
    int GetDeviceFormat(
        [MarshalAs(UnmanagedType.LPWStr)] string endpointId,
        [MarshalAs(UnmanagedType.Bool)] bool defaultFormat,
        out nint format);

    [PreserveSig]
    int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string endpointId);

    [PreserveSig]
    int SetDeviceFormat(
        [MarshalAs(UnmanagedType.LPWStr)] string endpointId,
        nint endpointFormat,
        nint mixFormat);

    [PreserveSig]
    int GetProcessingPeriod(
        [MarshalAs(UnmanagedType.LPWStr)] string endpointId,
        [MarshalAs(UnmanagedType.Bool)] bool defaultPeriod,
        out long defaultPeriodValue,
        out long minimumPeriodValue);

    [PreserveSig]
    int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string endpointId, ref long period);

    [PreserveSig]
    int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string endpointId, out nint mode);

    [PreserveSig]
    int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string endpointId, nint mode);

    [PreserveSig]
    int GetPropertyValue(
        [MarshalAs(UnmanagedType.LPWStr)] string endpointId,
        ref PropertyKey key,
        out PropVariant value);

    [PreserveSig]
    int SetPropertyValue(
        [MarshalAs(UnmanagedType.LPWStr)] string endpointId,
        ref PropertyKey key,
        ref PropVariant value);

    [PreserveSig]
    int SetDefaultEndpoint(
        [MarshalAs(UnmanagedType.LPWStr)] string endpointId,
        AudioDeviceRole role);

    [PreserveSig]
    int SetEndpointVisibility(
        [MarshalAs(UnmanagedType.LPWStr)] string endpointId,
        [MarshalAs(UnmanagedType.Bool)] bool isVisible);
}

internal static class CoreAudioInterop
{
    private const ushort VariantString = 31;
    private const ushort VariantGuid = 72;
    private const uint ReadOnlyStorage = 0;
    private static readonly PropertyKey FriendlyNameKey = new(
        new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"),
        14);
    private static readonly PropertyKey ContainerIdKey = new(
        new Guid("8C7ED206-3F8A-4827-B3AB-AE9E1FAEFC6C"),
        2);

    public static IMultimediaDeviceEnumerator CreateEnumerator() =>
        (IMultimediaDeviceEnumerator)Activator.CreateInstance(
            Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"))!)!;

    public static string GetEndpointId(IMultimediaDevice device)
    {
        Marshal.ThrowExceptionForHR(device.GetId(out var endpointId));
        return endpointId;
    }

    public static (string? FriendlyName, Guid? ContainerId) GetEndpointIdentity(
        IMultimediaDevice device, bool readContainerId, Guid? requiredContainerId = null)
    {
        IAudioPropertyStore? store = null;
        try
        {
            Marshal.ThrowExceptionForHR(device.OpenPropertyStore(ReadOnlyStorage, out store));
            var containerId = readContainerId ? ReadProperty<Guid?>(store, ContainerIdKey, ParseContainerId) : null;
            if (requiredContainerId.GetValueOrDefault() != Guid.Empty && containerId.HasValue &&
                containerId != requiredContainerId)
            {
                return (null, containerId);
            }
            return (ReadProperty(store, FriendlyNameKey, static value => value.VariantType == VariantString
                        ? Marshal.PtrToStringUni(value.PointerValue)
                        : null),
                containerId);
        }
        finally
        {
            Release(store);
        }
    }

    private static T? ReadProperty<T>(IAudioPropertyStore store, PropertyKey key, Func<PropVariant, T?> parse)
    {
        var value = default(PropVariant);
        try
        {
            return store.GetValue(ref key, out value) >= 0 && value.PointerValue != nint.Zero
                ? parse(value)
                : default;
        }
        finally
        {
            if (value.VariantType != 0)
            {
                PropVariantClear(ref value);
            }
        }
    }

    private static Guid? ParseContainerId(PropVariant value)
    {
        var containerId = value.VariantType switch
        {
            VariantGuid => Marshal.PtrToStructure<Guid>(value.PointerValue),
            VariantString when Guid.TryParse(Marshal.PtrToStringUni(value.PointerValue), out var parsed) => parsed,
            _ => Guid.Empty
        };
        return containerId == Guid.Empty ? null : containerId;
    }

    public static IAudioEndpointVolume ActivateEndpointVolume(IMultimediaDevice device)
    {
        var interfaceId = typeof(IAudioEndpointVolume).GUID;
        Marshal.ThrowExceptionForHR(device.Activate(
            ref interfaceId,
            ClassContext.All,
            nint.Zero,
            out var activatedInterface));
        return (IAudioEndpointVolume)activatedInterface;
    }

    public static void Release(object? comObject)
    {
        if (comObject is not null && Marshal.IsComObject(comObject))
        {
            // Separate Core Audio calls can return the same managed wrapper.
            Marshal.ReleaseComObject(comObject);
        }
    }

    [DllImport("Ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant value);
}
