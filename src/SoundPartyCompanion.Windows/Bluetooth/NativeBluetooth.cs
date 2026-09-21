using System.Runtime.InteropServices;
using System.Text;

namespace SoundPartyCompanion.Windows.Bluetooth;

internal static class NativeBluetooth
{
    internal const int BluetoothMaxNameSize = 248;
    internal const uint DeviceInfoAllClasses = 0x00000004;
    internal const uint DeviceInfoPresent = 0x00000002;
    internal static readonly nint InvalidHandleValue = new(-1);

    [DllImport("BluetoothApis.dll", SetLastError = true)]
    internal static extern nint BluetoothFindFirstRadio(
        ref BluetoothFindRadioParams searchParams,
        out nint radioHandle);

    [DllImport("BluetoothApis.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool BluetoothFindNextRadio(
        nint findHandle,
        out nint radioHandle);

    [DllImport("BluetoothApis.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool BluetoothFindRadioClose(nint findHandle);

    [DllImport("BluetoothApis.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern nint BluetoothFindFirstDevice(
        ref BluetoothDeviceSearchParams searchParams,
        ref BluetoothDeviceInfo deviceInfo);

    [DllImport("BluetoothApis.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool BluetoothFindNextDevice(
        nint findHandle,
        ref BluetoothDeviceInfo deviceInfo);

    [DllImport("BluetoothApis.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool BluetoothFindDeviceClose(nint findHandle);

    [DllImport("BluetoothApis.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern uint BluetoothGetDeviceInfo(
        nint radioHandle,
        ref BluetoothDeviceInfo deviceInfo);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern nint SetupDiGetClassDevs(
        nint classGuid,
        string enumerator,
        nint parentWindow,
        uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetupDiEnumDeviceInfo(
        nint deviceInfoSet,
        uint memberIndex,
        ref SetupDeviceInfoData deviceInfoData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetupDiGetDeviceInstanceId(
        nint deviceInfoSet,
        ref SetupDeviceInfoData deviceInfoData,
        StringBuilder deviceInstanceId,
        int deviceInstanceIdSize,
        out int requiredSize);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetupDiDestroyDeviceInfoList(nint deviceInfoSet);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseHandle(nint handle);
}

[StructLayout(LayoutKind.Sequential)]
internal struct BluetoothFindRadioParams
{
    internal int Size;
}

[StructLayout(LayoutKind.Sequential)]
internal struct BluetoothDeviceSearchParams
{
    internal int Size;

    [MarshalAs(UnmanagedType.Bool)]
    internal bool ReturnAuthenticated;

    [MarshalAs(UnmanagedType.Bool)]
    internal bool ReturnRemembered;

    [MarshalAs(UnmanagedType.Bool)]
    internal bool ReturnUnknown;

    [MarshalAs(UnmanagedType.Bool)]
    internal bool ReturnConnected;

    [MarshalAs(UnmanagedType.Bool)]
    internal bool IssueInquiry;

    internal byte TimeoutMultiplier;
    internal nint RadioHandle;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct BluetoothDeviceInfo
{
    internal int Size;
    internal ulong Address;
    internal uint ClassOfDevice;

    [MarshalAs(UnmanagedType.Bool)]
    internal bool Connected;

    [MarshalAs(UnmanagedType.Bool)]
    internal bool Remembered;

    [MarshalAs(UnmanagedType.Bool)]
    internal bool Authenticated;

    internal SystemTime LastSeen;
    internal SystemTime LastUsed;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = NativeBluetooth.BluetoothMaxNameSize)]
    internal string Name;
}

[StructLayout(LayoutKind.Sequential)]
internal struct SystemTime
{
    internal ushort Year;
    internal ushort Month;
    internal ushort DayOfWeek;
    internal ushort Day;
    internal ushort Hour;
    internal ushort Minute;
    internal ushort Second;
    internal ushort Milliseconds;
}

[StructLayout(LayoutKind.Sequential)]
internal struct SetupDeviceInfoData
{
    internal int Size;
    internal Guid ClassGuid;
    internal uint DeviceInstance;
    internal nint Reserved;
}
