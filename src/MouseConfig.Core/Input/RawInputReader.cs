using System.ComponentModel;
using System.Runtime.InteropServices;
using MouseConfig.Core.Devices;
using static MouseConfig.Core.Native.NativeMethods;

namespace MouseConfig.Core.Input;

/// <summary>
/// Listens to Raw Input for the button test. Only while started, and only while the app's window is in front
/// (no background listening). Nothing is sent to the mouse.
/// </summary>
public sealed class RawInputReader : IDisposable
{
    public const int WmInput = 0x00FF;
    private const int MaxUsagesPerReport = 32;

    private readonly Dictionary<IntPtr, string?> _instanceIdByHandle = [];
    private readonly Dictionary<IntPtr, IntPtr> _preparsedByHandle = [];
    private readonly Dictionary<(IntPtr, ushort), HashSet<ushort>> _heldUsages = [];
    private readonly Dictionary<IntPtr, string> _lastMakerReport = [];
    private readonly Dictionary<IntPtr, ushort> _heldKeys = [];
    private List<RawInputDevice> _registered = [];

    public bool IsListening => _registered.Count > 0;

    /// <summary>Starts listening. Returns the usages Windows refused, so the screen can say what is missing.</summary>
    public List<(ushort UsagePage, ushort Usage)> Start(IntPtr window, IEnumerable<(ushort UsagePage, ushort Usage)> usages)
    {
        Stop();
        var refused = new List<(ushort, ushort)>();
        uint itemSize = (uint)Marshal.SizeOf<RawInputDevice>();
        foreach ((ushort page, ushort usage) in usages)
        {
            var device = new RawInputDevice { UsagePage = page, Usage = usage, Flags = 0, Target = window };
            if (RegisterRawInputDevices([device], 1, itemSize))
            {
                _registered.Add(device);
            }
            else
            {
                refused.Add((page, usage));
            }
        }
        return refused;
    }

    public void Stop()
    {
        if (_registered.Count > 0)
        {
            RawInputDevice[] remove = _registered
                .Select(d => new RawInputDevice { UsagePage = d.UsagePage, Usage = d.Usage, Flags = RidevRemove, Target = IntPtr.Zero })
                .ToArray();
            if (!RegisterRawInputDevices(remove, (uint)remove.Length, (uint)Marshal.SizeOf<RawInputDevice>()))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not stop listening to the mouse");
            }
            _registered = [];
        }
        foreach (IntPtr preparsed in _preparsedByHandle.Values)
        {
            Marshal.FreeHGlobal(preparsed);
        }
        _preparsedByHandle.Clear();
        _instanceIdByHandle.Clear();
        _heldUsages.Clear();
        _lastMakerReport.Clear();
        _heldKeys.Clear();
    }

    public void Dispose() => Stop();

    /// <summary>Reads one WM_INPUT message. Returns nothing for devices that can't be identified.</summary>
    public List<InputEvent> Read(IntPtr lParam)
    {
        uint headerSize = (uint)Marshal.SizeOf<RawInputHeader>();
        uint size = 0;
        GetRawInputData(lParam, RidInput, IntPtr.Zero, ref size, headerSize);
        if (size == 0)
        {
            return [];
        }
        IntPtr buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (GetRawInputData(lParam, RidInput, buffer, ref size, headerSize) == uint.MaxValue)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not read input from the mouse");
            }
            var header = Marshal.PtrToStructure<RawInputHeader>(buffer);
            string? deviceId = InstanceIdOf(header.Device);
            if (deviceId is null)
            {
                return [];
            }
            IntPtr body = buffer + (int)headerSize;
            return header.Type switch
            {
                RimTypeMouse => ReadMouse(deviceId, body),
                RimTypeKeyboard => ReadKeyboard(header.Device, deviceId, body),
                RimTypeHid => ReadHid(header.Device, deviceId, body),
                _ => [],
            };
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    // RAWMOUSE: usFlags (2 bytes) + 2 padding, usButtonFlags, usButtonData, ulRawButtons, lLastX, lLastY.
    private static List<InputEvent> ReadMouse(string deviceId, IntPtr body) =>
        InputDecoding.FromMouse(deviceId, (ushort)Marshal.ReadInt16(body, 4), (ushort)Marshal.ReadInt16(body, 6),
            Marshal.ReadInt32(body, 12), Marshal.ReadInt32(body, 16));

    // RAWKEYBOARD: MakeCode, Flags, Reserved, VKey. Repeats while a key is held are dropped.
    private List<InputEvent> ReadKeyboard(IntPtr handle, string deviceId, IntPtr body)
    {
        InputEvent key = InputDecoding.FromKeyboard(deviceId, (ushort)Marshal.ReadInt16(body, 2), (ushort)Marshal.ReadInt16(body, 6));
        bool isRepeat = key.IsDown && _heldKeys.TryGetValue(handle, out ushort held) && held == key.Code;
        if (key.IsDown)
        {
            _heldKeys[handle] = (ushort)key.Code;
        }
        else
        {
            _heldKeys.Remove(handle);
        }
        return isRepeat ? [] : [key];
    }

    // RAWHID: dwSizeHid, dwCount, then dwCount reports of dwSizeHid bytes each.
    private List<InputEvent> ReadHid(IntPtr handle, string deviceId, IntPtr body)
    {
        int reportSize = Marshal.ReadInt32(body, 0);
        int reportCount = Marshal.ReadInt32(body, 4);
        var events = new List<InputEvent>();
        IntPtr preparsed = PreparsedDataOf(handle);
        for (int i = 0; i < reportCount; i++)
        {
            var report = new byte[reportSize];
            Marshal.Copy(body + 8 + i * reportSize, report, 0, reportSize);
            if (preparsed == IntPtr.Zero || HidP_GetCaps(preparsed, out HidpCaps caps) != HidpStatusSuccess)
            {
                continue;
            }
            if (caps.UsagePage == DeviceIds.UsagePageConsumer || caps.UsagePage == DeviceIds.UsagePageGenericDesktop)
            {
                events.AddRange(ReadUsages(handle, deviceId, caps.UsagePage, preparsed, report));
            }
            else
            {
                string hex = InputDecoding.Hex(report);
                // Maker channels may repeat the same report; only changes are worth showing.
                if (!_lastMakerReport.TryGetValue(handle, out string? last) || last != hex)
                {
                    _lastMakerReport[handle] = hex;
                    events.Add(new InputEvent(InputKind.MakerData, deviceId, caps.UsagePage, true, hex));
                }
            }
        }
        return events;
    }

    private List<InputEvent> ReadUsages(IntPtr handle, string deviceId, ushort usagePage, IntPtr preparsed, byte[] report)
    {
        var usages = new ushort[MaxUsagesPerReport];
        uint count = (uint)usages.Length;
        var now = new HashSet<ushort>();
        if (HidP_GetUsages(HidpInput, usagePage, 0, usages, ref count, preparsed, report, (uint)report.Length) == HidpStatusSuccess)
        {
            now.UnionWith(usages.Take((int)count).Where(u => u != 0));
        }
        HashSet<ushort> before = _heldUsages.GetValueOrDefault((handle, usagePage)) ?? [];
        _heldUsages[(handle, usagePage)] = now;
        return InputDecoding.DiffUsages(deviceId, usagePage, before, now);
    }

    private string? InstanceIdOf(IntPtr handle)
    {
        if (!_instanceIdByHandle.TryGetValue(handle, out string? id))
        {
            string? path = ReadDeviceName(handle);
            id = path is null ? null : DeviceIds.InstanceIdFromInterfacePath(path);
            _instanceIdByHandle[handle] = id;
        }
        return id;
    }

    private IntPtr PreparsedDataOf(IntPtr handle)
    {
        if (_preparsedByHandle.TryGetValue(handle, out IntPtr cached))
        {
            return cached;
        }
        uint size = 0;
        GetRawInputDeviceInfo(handle, RidiPreparsedData, IntPtr.Zero, ref size);
        IntPtr data = IntPtr.Zero;
        if (size > 0)
        {
            data = Marshal.AllocHGlobal((int)size);
            if (GetRawInputDeviceInfo(handle, RidiPreparsedData, data, ref size) == uint.MaxValue)
            {
                Marshal.FreeHGlobal(data);
                data = IntPtr.Zero;
            }
        }
        _preparsedByHandle[handle] = data;
        return data;
    }

    private static string? ReadDeviceName(IntPtr device)
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
            return GetRawInputDeviceInfo(device, RidiDeviceName, buffer, ref chars) == uint.MaxValue
                ? null
                : Marshal.PtrToStringUni(buffer);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
