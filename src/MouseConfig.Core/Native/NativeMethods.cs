using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace MouseConfig.Core.Native;

/// <summary>Windows calls for Raw Input, HID and device tree lookups. Read-only use only.</summary>
internal static class NativeMethods
{
    // ---------- Raw Input ----------
    public const uint RimTypeMouse = 0;
    public const uint RidiDeviceName = 0x20000007;
    public const uint RidiDeviceInfo = 0x2000000b;

    [StructLayout(LayoutKind.Sequential)]
    public struct RawInputDeviceListItem
    {
        public IntPtr Device;
        public uint Type;
    }

    /// <summary>RID_DEVICE_INFO, mouse part only. 32 bytes, the size of its largest (keyboard) form.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 32)]
    public struct RawInputDeviceInfo
    {
        [FieldOffset(0)] public uint Size;
        [FieldOffset(4)] public uint Type;
        [FieldOffset(8)] public uint MouseId;
        [FieldOffset(12)] public uint MouseButtonCount;
        [FieldOffset(16)] public uint MouseSampleRate;
        [FieldOffset(20)] public int MouseHasHorizontalWheel;
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetRawInputDeviceList(
        [Out] RawInputDeviceListItem[]? list, ref uint count, uint itemSize);

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "GetRawInputDeviceInfoW")]
    public static extern uint GetRawInputDeviceInfo(IntPtr device, uint command, IntPtr data, ref uint size);

    public const uint RimTypeKeyboard = 1;
    public const uint RimTypeHid = 2;
    public const uint RidInput = 0x10000003;
    public const uint RidiPreparsedData = 0x20000005;
    public const uint RidevRemove = 0x00000001;

    [StructLayout(LayoutKind.Sequential)]
    public struct RawInputDevice
    {
        public ushort UsagePage;
        public ushort Usage;
        public uint Flags;
        public IntPtr Target;
    }

    /// <summary>RAWINPUTHEADER: type, size, device handle, wParam. 24 bytes on 64-bit.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct RawInputHeader
    {
        public uint Type;
        public uint Size;
        public IntPtr Device;
        public IntPtr WParam;
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterRawInputDevices(RawInputDevice[] devices, uint count, uint itemSize);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetRawInputData(IntPtr rawInput, uint command, IntPtr data, ref uint size, uint headerSize);

    // ---------- SetupAPI + config manager ----------
    public const int DigcfPresent = 0x02;
    public const int DigcfDeviceInterface = 0x10;
    public const int CrSuccess = 0;
    public const int MaxDeviceIdLength = 200;

    [StructLayout(LayoutKind.Sequential)]
    public struct DeviceInterfaceData
    {
        public int Size;
        public Guid InterfaceClass;
        public int Flags;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DeviceInfoData
    {
        public int Size;
        public Guid ClassGuid;
        public uint DevInst;
        public IntPtr Reserved;
    }

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, IntPtr enumerator, IntPtr parent, int flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    public static extern bool SetupDiEnumDeviceInterfaces(
        IntPtr set, IntPtr deviceInfo, ref Guid interfaceClass, int index, ref DeviceInterfaceData data);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool SetupDiGetDeviceInterfaceDetail(
        IntPtr set, ref DeviceInterfaceData data, IntPtr detail, int detailSize, out int requiredSize,
        ref DeviceInfoData deviceInfo);

    [DllImport("setupapi.dll", SetLastError = true)]
    public static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, EntryPoint = "CM_Get_Device_IDW")]
    public static extern int CM_Get_Device_ID(uint devInst, char[] buffer, int bufferLength, int flags);

    [DllImport("cfgmgr32.dll")]
    public static extern int CM_Get_Parent(out uint parentDevInst, uint devInst, int flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, EntryPoint = "CM_Locate_DevNodeW")]
    public static extern int CM_Locate_DevNode(out uint devInst, string deviceId, int flags);

    // ---------- HID ----------
    [StructLayout(LayoutKind.Sequential)]
    public struct HiddAttributes
    {
        public int Size;
        public ushort VendorId;
        public ushort ProductId;
        public ushort VersionNumber;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct HidpCaps
    {
        public ushort Usage;
        public ushort UsagePage;
        public ushort InputReportByteLength;
        public ushort OutputReportByteLength;
        public ushort FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes;
        public ushort NumberInputButtonCaps;
        public ushort NumberInputValueCaps;
        public ushort NumberInputDataIndices;
        public ushort NumberOutputButtonCaps;
        public ushort NumberOutputValueCaps;
        public ushort NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps;
        public ushort NumberFeatureValueCaps;
        public ushort NumberFeatureDataIndices;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct HidpValueCaps
    {
        public ushort UsagePage;
        public byte ReportId;
        public byte IsAlias;
        public ushort BitField;
        public ushort LinkCollection;
        public ushort LinkUsage;
        public ushort LinkUsagePage;
        public byte IsRange;
        public byte IsStringRange;
        public byte IsDesignatorRange;
        public byte IsAbsolute;
        public byte HasNull;
        public byte Reserved;
        public ushort BitSize;
        public ushort ReportCount;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 5)] public ushort[] Reserved2;
        public uint UnitsExp;
        public uint Units;
        public int LogicalMin;
        public int LogicalMax;
        public int PhysicalMin;
        public int PhysicalMax;
        /// <summary>The usage itself when IsRange is 0.</summary>
        public ushort UsageMin;
        public ushort UsageMax;
        public ushort StringMin;
        public ushort StringMax;
        public ushort DesignatorMin;
        public ushort DesignatorMax;
        public ushort DataIndexMin;
        public ushort DataIndexMax;
    }

    public const int HidpStatusSuccess = 0x00110000;
    public const int HidpInput = 0;

    [DllImport("hid.dll")]
    public static extern void HidD_GetHidGuid(out Guid hidGuid);

    [DllImport("hid.dll", SetLastError = true)]
    public static extern bool HidD_GetAttributes(SafeFileHandle device, ref HiddAttributes attributes);

    [DllImport("hid.dll", SetLastError = true)]
    public static extern bool HidD_GetProductString(SafeFileHandle device, byte[] buffer, int bufferLength);

    [DllImport("hid.dll", SetLastError = true)]
    public static extern bool HidD_GetManufacturerString(SafeFileHandle device, byte[] buffer, int bufferLength);

    [DllImport("hid.dll", SetLastError = true)]
    public static extern bool HidD_GetPreparsedData(SafeFileHandle device, out IntPtr preparsedData);

    [DllImport("hid.dll")]
    public static extern bool HidD_FreePreparsedData(IntPtr preparsedData);

    [DllImport("hid.dll")]
    public static extern int HidP_GetCaps(IntPtr preparsedData, out HidpCaps caps);

    [DllImport("hid.dll")]
    public static extern int HidP_GetUsages(
        int reportType, ushort usagePage, ushort linkCollection, [Out] ushort[] usages, ref uint usageCount,
        IntPtr preparsedData, byte[] report, uint reportLength);

    [DllImport("hid.dll")]
    public static extern int HidP_GetValueCaps(
        int reportType, [Out] HidpValueCaps[] caps, ref ushort count, IntPtr preparsedData);

    // ---------- Files ----------
    public const uint FileShareReadWrite = 0x1 | 0x2;
    public const uint OpenExisting = 3;

    /// <summary>Opened with zero access: enough to read HID descriptions, never to talk to the device.</summary>
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CreateFileW")]
    public static extern SafeFileHandle CreateFile(
        string fileName, uint access, uint shareMode, IntPtr security, uint creation, uint flags, IntPtr template);
}
