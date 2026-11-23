using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using static MouseConfig.Core.Native.NativeMethods;

namespace MouseConfig.Core.Devices;

/// <summary>What one HID channel says about itself. Read with zero access rights: nothing is sent to the device.</summary>
internal sealed record HidInterfaceInfo(
    string Path,
    string InstanceId,
    HidCollection? Collection,
    ushort? VendorId,
    ushort? ProductId,
    string? Product,
    string? Manufacturer,
    bool HasWheel,
    bool HasHorizontalWheel,
    string? Problem);

internal static class HidEnumerator
{
    private const int StringBufferBytes = 254; // HID strings are at most 126 UTF-16 characters.

    public static List<HidInterfaceInfo> ListAll(Log log)
    {
        var result = new List<HidInterfaceInfo>();
        HidD_GetHidGuid(out Guid hidGuid);
        IntPtr set = SetupDiGetClassDevs(ref hidGuid, IntPtr.Zero, IntPtr.Zero, DigcfPresent | DigcfDeviceInterface);
        if (set == new IntPtr(-1))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not list HID devices");
        }
        try
        {
            var data = new DeviceInterfaceData { Size = Marshal.SizeOf<DeviceInterfaceData>() };
            for (int index = 0; SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref hidGuid, index, ref data); index++)
            {
                (string? path, uint devInst) = GetInterfacePath(set, ref data);
                if (path is null)
                {
                    continue;
                }
                string instanceId = GetDeviceId(devInst) ?? DeviceIds.InstanceIdFromInterfacePath(path);
                result.Add(ReadInterface(path, instanceId, log));
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(set);
        }
        return result;
    }

    /// <summary>The device's own instance ID followed by each parent's, nearest first.</summary>
    public static List<string> GetSelfAndAncestors(string instanceId)
    {
        var ids = new List<string>();
        if (CM_Locate_DevNode(out uint devInst, instanceId, 0) != CrSuccess)
        {
            return ids;
        }
        while (ids.Count < 32)
        {
            string? id = GetDeviceId(devInst);
            if (id is null)
            {
                break;
            }
            ids.Add(id);
            if (CM_Get_Parent(out uint parent, devInst, 0) != CrSuccess)
            {
                break;
            }
            devInst = parent;
        }
        return ids;
    }

    private static (string? Path, uint DevInst) GetInterfacePath(IntPtr set, ref DeviceInterfaceData data)
    {
        var devInfo = new DeviceInfoData { Size = Marshal.SizeOf<DeviceInfoData>() };
        SetupDiGetDeviceInterfaceDetail(set, ref data, IntPtr.Zero, 0, out int requiredSize, ref devInfo);
        if (requiredSize <= 0)
        {
            return (null, 0);
        }
        IntPtr buffer = Marshal.AllocHGlobal(requiredSize);
        try
        {
            Marshal.WriteInt32(buffer, IntPtr.Size == 8 ? 8 : 6); // cbSize of SP_DEVICE_INTERFACE_DETAIL_DATA_W
            if (!SetupDiGetDeviceInterfaceDetail(set, ref data, buffer, requiredSize, out _, ref devInfo))
            {
                return (null, 0);
            }
            return (Marshal.PtrToStringUni(buffer + 4), devInfo.DevInst);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static string? GetDeviceId(uint devInst)
    {
        var buffer = new char[MaxDeviceIdLength + 1];
        return CM_Get_Device_ID(devInst, buffer, buffer.Length, 0) == CrSuccess
            ? new string(buffer).TrimEnd('\0')
            : null;
    }

    private static HidInterfaceInfo ReadInterface(string path, string instanceId, Log log)
    {
        using SafeFileHandle handle = CreateFile(path, 0, FileShareReadWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            string problem = $"Could not open {DeviceIds.CollectionLabel(instanceId)} (Windows error {Marshal.GetLastWin32Error()})";
            log.Info($"{problem}: {path}");
            return new HidInterfaceInfo(path, instanceId, null, null, null, null, null, false, false, problem);
        }

        ushort? vendorId = null, productId = null;
        var attributes = new HiddAttributes { Size = Marshal.SizeOf<HiddAttributes>() };
        if (HidD_GetAttributes(handle, ref attributes))
        {
            vendorId = attributes.VendorId;
            productId = attributes.ProductId;
        }

        string? product = ReadString(handle, HidD_GetProductString);
        string? manufacturer = ReadString(handle, HidD_GetManufacturerString);

        if (!HidD_GetPreparsedData(handle, out IntPtr preparsed))
        {
            string problem = $"Could not read the HID description (Windows error {Marshal.GetLastWin32Error()})";
            log.Info($"{problem}: {path}");
            return new HidInterfaceInfo(path, instanceId, null, vendorId, productId, product, manufacturer, false, false, problem);
        }
        try
        {
            if (HidP_GetCaps(preparsed, out HidpCaps caps) != HidpStatusSuccess)
            {
                return new HidInterfaceInfo(path, instanceId, null, vendorId, productId, product, manufacturer,
                    false, false, "Could not read the HID channel's capabilities");
            }
            var collection = new HidCollection(instanceId, caps.UsagePage, caps.Usage,
                caps.InputReportByteLength, caps.OutputReportByteLength, caps.FeatureReportByteLength, path);
            List<HidpValueCaps> inputValues = ReadInputValueCaps(preparsed, caps.NumberInputValueCaps);
            bool hasWheel = inputValues.Any(v => Covers(v, DeviceIds.UsagePageGenericDesktop, DeviceIds.UsageWheel));
            bool hasPan = inputValues.Any(v => Covers(v, DeviceIds.UsagePageConsumer, DeviceIds.UsageAcPan));
            return new HidInterfaceInfo(path, instanceId, collection, vendorId, productId, product, manufacturer,
                hasWheel, hasPan, null);
        }
        finally
        {
            HidD_FreePreparsedData(preparsed);
        }
    }

    private static List<HidpValueCaps> ReadInputValueCaps(IntPtr preparsed, ushort count)
    {
        if (count == 0)
        {
            return [];
        }
        var caps = new HidpValueCaps[count];
        return HidP_GetValueCaps(HidpInput, caps, ref count, preparsed) == HidpStatusSuccess
            ? caps.Take(count).ToList()
            : [];
    }

    private static bool Covers(HidpValueCaps caps, ushort usagePage, ushort usage) =>
        caps.UsagePage == usagePage
        && (caps.IsRange != 0 ? usage >= caps.UsageMin && usage <= caps.UsageMax : caps.UsageMin == usage);

    private static string? ReadString(SafeFileHandle handle, Func<SafeFileHandle, byte[], int, bool> read)
    {
        var buffer = new byte[StringBufferBytes];
        if (!read(handle, buffer, buffer.Length))
        {
            return null;
        }
        string text = Encoding.Unicode.GetString(buffer);
        int end = text.IndexOf('\0');
        text = (end >= 0 ? text[..end] : text).Trim();
        return text.Length == 0 ? null : text;
    }
}
