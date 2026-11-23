using System.ComponentModel;
using System.Runtime.InteropServices;

namespace MouseConfig.Core.Settings;

/// <summary>Reads and writes Windows' mouse settings.</summary>
public interface IMouseSettingsSystem
{
    WindowsMouseSettings Read();

    /// <summary>Writes only the settings that differ between <paramref name="from"/> and <paramref name="to"/>.</summary>
    void Write(WindowsMouseSettings from, WindowsMouseSettings to);
}

/// <summary>The real Windows settings, through SystemParametersInfo. Saved for the user and announced to other apps.</summary>
public sealed class SystemMouseSettings : IMouseSettingsSystem
{
    private const uint SpiGetMouse = 0x0003;
    private const uint SpiSetMouse = 0x0004;
    private const uint SpiSetDoubleClickTime = 0x0020;
    private const uint SpiSetMouseButtonSwap = 0x0021;
    private const uint SpiSetMouseTrails = 0x005D;
    private const uint SpiGetMouseTrails = 0x005E;
    private const uint SpiGetSnapToDefButton = 0x005F;
    private const uint SpiSetSnapToDefButton = 0x0060;
    private const uint SpiGetWheelScrollLines = 0x0068;
    private const uint SpiSetWheelScrollLines = 0x0069;
    private const uint SpiGetWheelScrollChars = 0x006C;
    private const uint SpiSetWheelScrollChars = 0x006D;
    private const uint SpiGetMouseSpeed = 0x0070;
    private const uint SpiSetMouseSpeed = 0x0071;
    private const uint SpiGetMouseSonar = 0x101C;
    private const uint SpiSetMouseSonar = 0x101D;
    private const uint SpiGetMouseClickLock = 0x101E;
    private const uint SpiSetMouseClickLock = 0x101F;
    private const uint SpiGetMouseVanish = 0x1020;
    private const uint SpiSetMouseVanish = 0x1021;
    private const uint SpiGetMouseClickLockTime = 0x2008;
    private const uint SpiSetMouseClickLockTime = 0x2009;
    private const uint SpifUpdateIniFile = 0x01;
    private const uint SpifSendChange = 0x02;
    private const int SmSwapButton = 23;
    private const uint WheelPageScroll = uint.MaxValue;

    public WindowsMouseSettings Read()
    {
        var accel = new int[3];
        Check(SystemParametersInfo(SpiGetMouse, 0, accel, 0), "read pointer precision");
        int trails = GetInt(SpiGetMouseTrails, "read pointer trails");
        uint wheelLines = (uint)GetInt(SpiGetWheelScrollLines, "read scroll lines");
        return new WindowsMouseSettings
        {
            PointerSpeed = GetInt(SpiGetMouseSpeed, "read pointer speed"),
            AccelThreshold1 = accel[0],
            AccelThreshold2 = accel[1],
            Acceleration = accel[2],
            SwapButtons = GetSystemMetrics(SmSwapButton) != 0,
            DoubleClickMs = (int)GetDoubleClickTime(),
            ClickLock = GetInt(SpiGetMouseClickLock, "read ClickLock") != 0,
            ClickLockMs = GetInt(SpiGetMouseClickLockTime, "read ClickLock time"),
            HideWhileTyping = GetInt(SpiGetMouseVanish, "read hide pointer while typing") != 0,
            PointerTrails = trails <= 1 ? 0 : trails, // Windows reports 0 or 1 for "off"
            ShowLocationWithCtrl = GetInt(SpiGetMouseSonar, "read show pointer location") != 0,
            SnapToDefaultButton = GetInt(SpiGetSnapToDefButton, "read snap to default button") != 0,
            WheelLines = wheelLines == WheelPageScroll ? WindowsMouseSettings.OneScreenAtATime : (int)wheelLines,
            WheelChars = GetInt(SpiGetWheelScrollChars, "read scroll characters"),
        };
    }

    public void Write(WindowsMouseSettings from, WindowsMouseSettings to)
    {
        const uint save = SpifUpdateIniFile | SpifSendChange;
        if (from.PointerSpeed != to.PointerSpeed)
            Check(SystemParametersInfo(SpiSetMouseSpeed, 0, to.PointerSpeed, save), "set pointer speed");
        if ((from.AccelThreshold1, from.AccelThreshold2, from.Acceleration) != (to.AccelThreshold1, to.AccelThreshold2, to.Acceleration))
            Check(SystemParametersInfo(SpiSetMouse, 0, [to.AccelThreshold1, to.AccelThreshold2, to.Acceleration], save), "set pointer precision");
        if (from.SwapButtons != to.SwapButtons)
            Check(SystemParametersInfo(SpiSetMouseButtonSwap, to.SwapButtons ? 1u : 0u, IntPtr.Zero, save), "swap buttons");
        if (from.DoubleClickMs != to.DoubleClickMs)
            Check(SystemParametersInfo(SpiSetDoubleClickTime, (uint)to.DoubleClickMs, IntPtr.Zero, save), "set double-click speed");
        if (from.ClickLock != to.ClickLock)
            Check(SystemParametersInfo(SpiSetMouseClickLock, 0, to.ClickLock ? 1 : 0, save), "set ClickLock");
        if (from.ClickLockMs != to.ClickLockMs)
            Check(SystemParametersInfo(SpiSetMouseClickLockTime, 0, to.ClickLockMs, save), "set ClickLock time");
        if (from.HideWhileTyping != to.HideWhileTyping)
            Check(SystemParametersInfo(SpiSetMouseVanish, 0, to.HideWhileTyping ? 1 : 0, save), "set hide pointer while typing");
        if (from.PointerTrails != to.PointerTrails)
            Check(SystemParametersInfo(SpiSetMouseTrails, (uint)to.PointerTrails, IntPtr.Zero, save), "set pointer trails");
        if (from.ShowLocationWithCtrl != to.ShowLocationWithCtrl)
            Check(SystemParametersInfo(SpiSetMouseSonar, 0, to.ShowLocationWithCtrl ? 1 : 0, save), "set show pointer location");
        if (from.SnapToDefaultButton != to.SnapToDefaultButton)
            Check(SystemParametersInfo(SpiSetSnapToDefButton, to.SnapToDefaultButton ? 1u : 0u, IntPtr.Zero, save), "set snap to default button");
        if (from.WheelLines != to.WheelLines)
        {
            uint lines = to.WheelLines == WindowsMouseSettings.OneScreenAtATime ? WheelPageScroll : (uint)to.WheelLines;
            Check(SystemParametersInfo(SpiSetWheelScrollLines, lines, IntPtr.Zero, save), "set scroll lines");
        }
        if (from.WheelChars != to.WheelChars)
            Check(SystemParametersInfo(SpiSetWheelScrollChars, (uint)to.WheelChars, IntPtr.Zero, save), "set scroll characters");
    }

    private static int GetInt(uint action, string what)
    {
        int value = 0;
        Check(SystemParametersInfo(action, 0, ref value, 0), what);
        return value;
    }

    private static void Check(bool ok, string what)
    {
        if (!ok)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Windows refused to {what}");
        }
    }

    // pvParam passed as a plain number: used by the SET actions that take their value in pvParam.
    private static bool SystemParametersInfo(uint action, uint uiParam, int pvValue, uint winIni) =>
        SystemParametersInfo(action, uiParam, new IntPtr(pvValue), winIni);

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "SystemParametersInfoW")]
    private static extern bool SystemParametersInfo(uint action, uint uiParam, IntPtr pvParam, uint winIni);

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "SystemParametersInfoW")]
    private static extern bool SystemParametersInfo(uint action, uint uiParam, ref int pvParam, uint winIni);

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "SystemParametersInfoW")]
    private static extern bool SystemParametersInfo(uint action, uint uiParam, [In, Out] int[] pvParam, uint winIni);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    private static extern uint GetDoubleClickTime();
}
