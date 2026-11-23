using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;

namespace MouseConfig.App;

/// <summary>
/// Tells the app when a HID device (mouse, keyboard, touchpad) is plugged in or removed.
/// One plug-in fires several Windows messages (one per HID channel), so they are grouped into one event.
/// </summary>
public sealed class DeviceChangeWatcher : IDisposable
{
    private const int WmDeviceChange = 0x0219;
    private const int DbtDeviceArrival = 0x8000;
    private const int DbtDeviceRemoveComplete = 0x8004;
    private const int DbtDevtypDeviceInterface = 5;
    private const int DeviceNotifyWindowHandle = 0;
    private static readonly TimeSpan QuietTime = TimeSpan.FromMilliseconds(500);

    private readonly HwndSource _source;
    private readonly DispatcherTimer _timer;
    private IntPtr _registration;

    public DeviceChangeWatcher(HwndSource source)
    {
        _source = source;
        _timer = new DispatcherTimer { Interval = QuietTime };
        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            DevicesChanged?.Invoke(this, EventArgs.Empty);
        };

        HidD_GetHidGuid(out Guid hidGuid);
        var filter = new DevBroadcastDeviceInterface
        {
            Size = Marshal.SizeOf<DevBroadcastDeviceInterface>(),
            DeviceType = DbtDevtypDeviceInterface,
            ClassGuid = hidGuid,
        };
        _registration = RegisterDeviceNotification(source.Handle, ref filter, DeviceNotifyWindowHandle);
        if (_registration == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not watch for mouse plug-in and removal");
        }
        source.AddHook(OnMessage);
    }

    public event EventHandler? DevicesChanged;

    public void Dispose()
    {
        _timer.Stop();
        _source.RemoveHook(OnMessage);
        if (_registration != IntPtr.Zero)
        {
            UnregisterDeviceNotification(_registration);
            _registration = IntPtr.Zero;
        }
    }

    private IntPtr OnMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // This hook sees every window message; many carry 64-bit pointers in wParam, so read it only for
        // WM_DEVICECHANGE and without a checked 32-bit conversion.
        if (message != WmDeviceChange)
        {
            return IntPtr.Zero;
        }
        long change = wParam.ToInt64();
        if (change == DbtDeviceArrival || change == DbtDeviceRemoveComplete)
        {
            _timer.Stop();
            _timer.Start();
        }
        return IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DevBroadcastDeviceInterface
    {
        public int Size;
        public int DeviceType;
        public int Reserved;
        public Guid ClassGuid;
        public char Name;
    }

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "RegisterDeviceNotificationW")]
    private static extern IntPtr RegisterDeviceNotification(IntPtr recipient, ref DevBroadcastDeviceInterface filter, int flags);

    [DllImport("user32.dll")]
    private static extern bool UnregisterDeviceNotification(IntPtr handle);

    [DllImport("hid.dll")]
    private static extern void HidD_GetHidGuid(out Guid hidGuid);
}
