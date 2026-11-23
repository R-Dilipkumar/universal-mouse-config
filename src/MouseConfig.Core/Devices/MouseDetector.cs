using System.ComponentModel;
using System.Runtime.InteropServices;
using static MouseConfig.Core.Native.NativeMethods;

namespace MouseConfig.Core.Devices;

public sealed record DetectionResult(
    IReadOnlyList<MouseDevice> Mice,
    IReadOnlyList<MouseDevice> HiddenTouchpads,
    int HiddenVirtualCount);

/// <summary>Finds pointing devices with Raw Input and fills in details from their HID description. Read-only.</summary>
public sealed class MouseDetector
{
    private readonly Log _log;

    public MouseDetector(Log log)
    {
        _log = log;
    }

    public DetectionResult Detect()
    {
        List<HidInterfaceInfo> hidInterfaces = HidEnumerator.ListAll(_log);
        var hidByInstance = new Dictionary<string, HidInterfaceInfo>(StringComparer.OrdinalIgnoreCase);
        var keyByInstance = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (HidInterfaceInfo hid in hidInterfaces)
        {
            hidByInstance[hid.InstanceId] = hid;
            keyByInstance[hid.InstanceId] = DeviceIds.PhysicalDeviceKey(
                HidEnumerator.GetSelfAndAncestors(hid.InstanceId).Skip(1).ToList());
        }

        var mice = new List<MouseDevice>();
        var touchpads = new List<MouseDevice>();
        int virtualCount = 0;
        foreach ((IntPtr handle, string path) in ListRawInputMice())
        {
            string instanceId = DeviceIds.InstanceIdFromInterfacePath(path);
            if (DeviceIds.IsVirtualMouse(instanceId))
            {
                virtualCount++;
                continue;
            }
            MouseDevice mouse = Describe(handle, path, instanceId, hidByInstance, keyByInstance);
            (mouse.IsTouchpad ? touchpads : mice).Add(mouse);
        }
        return new DetectionResult(mice, touchpads, virtualCount);
    }

    private MouseDevice Describe(IntPtr handle, string path, string instanceId,
        Dictionary<string, HidInterfaceInfo> hidByInstance, Dictionary<string, string?> keyByInstance)
    {
        var problems = new List<string>();
        RawInputDeviceInfo? info = ReadRawInfo(handle);
        if (info is null)
        {
            problems.Add("Windows didn't give button details");
        }

        List<string> selfAndAncestors = HidEnumerator.GetSelfAndAncestors(instanceId);
        hidByInstance.TryGetValue(instanceId, out HidInterfaceInfo? mouseHid);
        if (mouseHid?.Problem is { } hidProblem)
        {
            problems.Add(hidProblem);
        }

        List<HidCollection> collections = [];
        string? key = DeviceIds.PhysicalDeviceKey(selfAndAncestors.Skip(1).ToList());
        if (key is not null)
        {
            collections = hidByInstance.Values
                .Where(h => h.Collection is not null
                            && string.Equals(keyByInstance[h.InstanceId], key, StringComparison.OrdinalIgnoreCase))
                .Select(h => h.Collection!)
                .OrderBy(c => c.InstanceId, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        if (mouseHid is null)
        {
            problems.Add("Not a HID device, so only basic details are known");
        }

        ConnectionType connection = DeviceIds.ConnectionFromAncestors(selfAndAncestors);
        return new MouseDevice
        {
            RawInputPath = path,
            InstanceId = instanceId,
            Name = DeviceIds.DisplayName(mouseHid?.Product, mouseHid?.Manufacturer, mouseHid?.VendorId,
                mouseHid?.ProductId, connection),
            Manufacturer = mouseHid?.Manufacturer,
            VendorId = mouseHid?.VendorId,
            ProductId = mouseHid?.ProductId,
            Connection = connection,
            ButtonCount = (int)(info?.MouseButtonCount ?? 0),
            SampleRateHz = (int)(info?.MouseSampleRate ?? 0),
            HasWheel = mouseHid is null ? null : mouseHid.HasWheel,
            HasHorizontalWheel = (info?.MouseHasHorizontalWheel ?? 0) != 0 || mouseHid?.HasHorizontalWheel == true,
            IsTouchpad = DeviceIds.IsTouchpad(collections),
            Collections = collections,
            Problems = problems,
        };
    }

    private static List<(IntPtr Handle, string Path)> ListRawInputMice()
    {
        uint itemSize = (uint)Marshal.SizeOf<RawInputDeviceListItem>();
        uint count = 0;
        if (GetRawInputDeviceList(null, ref count, itemSize) == uint.MaxValue)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not count input devices");
        }
        var items = new RawInputDeviceListItem[count + 8]; // room for a device plugged in between the two calls
        count = (uint)items.Length;
        uint found = GetRawInputDeviceList(items, ref count, itemSize);
        if (found == uint.MaxValue)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not list input devices");
        }

        var mice = new List<(IntPtr, string)>();
        foreach (RawInputDeviceListItem item in items.Take((int)found))
        {
            if (item.Type == RimTypeMouse && ReadRawName(item.Device) is { } name)
            {
                mice.Add((item.Device, name));
            }
        }
        return mice;
    }

    private static string? ReadRawName(IntPtr device)
    {
        uint chars = 0;
        GetRawInputDeviceInfo(device, RidiDeviceName, IntPtr.Zero, ref chars);
        if (chars == 0)
        {
            return null;
        }
        IntPtr buffer = Marshal.AllocHGlobal((int)chars * 2);
        try
        {
            uint copied = GetRawInputDeviceInfo(device, RidiDeviceName, buffer, ref chars);
            return copied == uint.MaxValue ? null : Marshal.PtrToStringUni(buffer);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static RawInputDeviceInfo? ReadRawInfo(IntPtr device)
    {
        int size = Marshal.SizeOf<RawInputDeviceInfo>();
        IntPtr buffer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(new RawInputDeviceInfo { Size = (uint)size }, buffer, false);
            uint byteCount = (uint)size;
            uint copied = GetRawInputDeviceInfo(device, RidiDeviceInfo, buffer, ref byteCount);
            return copied == uint.MaxValue ? null : Marshal.PtrToStructure<RawInputDeviceInfo>(buffer);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
