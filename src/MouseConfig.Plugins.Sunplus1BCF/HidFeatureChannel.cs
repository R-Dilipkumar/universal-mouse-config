using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace MouseConfig.Plugins.Sunplus1BCF;

/// <summary>Feature reports on the mouse's settings channel (COL04), through Windows' HID driver.</summary>
internal sealed class HidFeatureChannel : IFeatureChannel, IDisposable
{
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareReadWrite = 0x1 | 0x2;
    private const uint OpenExisting = 3;

    private readonly SafeFileHandle _handle;

    public HidFeatureChannel(string devicePath)
    {
        _handle = CreateFile(devicePath, GenericRead | GenericWrite, FileShareReadWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
        if (_handle.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not open the mouse's settings channel");
        }
    }

    public void SetFeature(byte[] report)
    {
        if (!HidD_SetFeature(_handle, report, report.Length))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Sending command 0x{report[1]:X2} to the mouse failed");
        }
    }

    public byte[] GetFeature()
    {
        var buffer = new byte[SunplusSession.FrameBytes];
        buffer[0] = SunplusSession.ReportId;
        if (!HidD_GetFeature(_handle, buffer, buffer.Length))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Reading the mouse's answer failed");
        }
        return buffer;
    }

    public void Dispose() => _handle.Dispose();

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CreateFileW")]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_SetFeature(SafeFileHandle device, byte[] buffer, int length);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_GetFeature(SafeFileHandle device, byte[] buffer, int length);
}
