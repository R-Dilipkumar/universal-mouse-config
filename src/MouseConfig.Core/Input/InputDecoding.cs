using MouseConfig.Core.Devices;

namespace MouseConfig.Core.Input;

/// <summary>Pure rules for turning Raw Input data into <see cref="InputEvent"/>s. No Windows calls.</summary>
public static class InputDecoding
{
    private const ushort RiKeyBreak = 0x01;
    private const ushort MouseWheel = 0x0400;
    private const ushort MouseHorizontalWheel = 0x0800;

    /// <summary>RAWMOUSE usButtonFlags bits for buttons 1–5: (down bit, up bit, button number).</summary>
    private static readonly (ushort Down, ushort Up, int Button)[] ButtonBits =
    [
        (0x0001, 0x0002, 1),
        (0x0004, 0x0008, 2),
        (0x0010, 0x0020, 3),
        (0x0040, 0x0080, 4),
        (0x0100, 0x0200, 5),
    ];

    public static List<InputEvent> FromMouse(string deviceId, ushort buttonFlags, ushort buttonData, int moveX = 0, int moveY = 0)
    {
        var events = new List<InputEvent>();
        if (moveX != 0 || moveY != 0)
        {
            events.Add(new InputEvent(InputKind.Move, deviceId, 0, true, ""));
        }
        foreach ((ushort down, ushort up, int button) in ButtonBits)
        {
            if ((buttonFlags & down) != 0)
            {
                events.Add(new InputEvent(InputKind.MouseButton, deviceId, button, true, ButtonName(button)));
            }
            if ((buttonFlags & up) != 0)
            {
                events.Add(new InputEvent(InputKind.MouseButton, deviceId, button, false, ButtonName(button)));
            }
        }
        short delta = unchecked((short)buttonData);
        if ((buttonFlags & MouseWheel) != 0)
        {
            events.Add(new InputEvent(InputKind.Wheel, deviceId, delta, true, delta > 0 ? "Wheel up" : "Wheel down"));
        }
        if ((buttonFlags & MouseHorizontalWheel) != 0)
        {
            events.Add(new InputEvent(InputKind.TiltWheel, deviceId, delta, true, delta > 0 ? "Tilt right" : "Tilt left"));
        }
        return events;
    }

    public static InputEvent FromKeyboard(string deviceId, ushort flags, ushort virtualKey) =>
        new(InputKind.Key, deviceId, virtualKey, (flags & RiKeyBreak) == 0, "");

    /// <summary>
    /// Compares the usages held down in this report with the last one and returns presses and releases.
    /// HID media and system keys only say "these are down now", so a release is a usage that disappeared.
    /// </summary>
    public static List<InputEvent> DiffUsages(string deviceId, ushort usagePage,
        IReadOnlySet<ushort> before, IReadOnlySet<ushort> now)
    {
        InputKind kind = usagePage == DeviceIds.UsagePageConsumer ? InputKind.MediaKey : InputKind.SystemKey;
        var events = new List<InputEvent>();
        foreach (ushort usage in now.Where(u => !before.Contains(u)).Order())
        {
            events.Add(new InputEvent(kind, deviceId, usage, true, UsageName(usagePage, usage)));
        }
        foreach (ushort usage in before.Where(u => !now.Contains(u)).Order())
        {
            events.Add(new InputEvent(kind, deviceId, usage, false, UsageName(usagePage, usage)));
        }
        return events;
    }

    public static string ButtonName(int button) => button switch
    {
        1 => "Left button",
        2 => "Right button",
        3 => "Middle button (wheel click)",
        4 => "Back button (4)",
        5 => "Forward button (5)",
        _ => $"Button {button}",
    };

    public static string UsageName(ushort usagePage, ushort usage) => (usagePage, usage) switch
    {
        (DeviceIds.UsagePageConsumer, 0xB5) => "Next track",
        (DeviceIds.UsagePageConsumer, 0xB6) => "Previous track",
        (DeviceIds.UsagePageConsumer, 0xB7) => "Stop",
        (DeviceIds.UsagePageConsumer, 0xCD) => "Play / pause",
        (DeviceIds.UsagePageConsumer, 0xE2) => "Mute",
        (DeviceIds.UsagePageConsumer, 0xE9) => "Volume up",
        (DeviceIds.UsagePageConsumer, 0xEA) => "Volume down",
        (DeviceIds.UsagePageConsumer, 0x183) => "Media player",
        (DeviceIds.UsagePageConsumer, 0x18A) => "Mail",
        (DeviceIds.UsagePageConsumer, 0x192) => "Calculator",
        (DeviceIds.UsagePageConsumer, 0x194) => "This PC",
        (DeviceIds.UsagePageConsumer, 0x221) => "Search",
        (DeviceIds.UsagePageConsumer, 0x223) => "Browser home",
        (DeviceIds.UsagePageConsumer, 0x224) => "Browser back",
        (DeviceIds.UsagePageConsumer, 0x225) => "Browser forward",
        (DeviceIds.UsagePageConsumer, 0x227) => "Browser refresh",
        (DeviceIds.UsagePageGenericDesktop, 0x81) => "Power",
        (DeviceIds.UsagePageGenericDesktop, 0x82) => "Sleep",
        (DeviceIds.UsagePageGenericDesktop, 0x83) => "Wake",
        (DeviceIds.UsagePageConsumer, _) => $"Media key 0x{usage:X}",
        _ => $"System key 0x{usage:X}",
    };

    public static string Hex(IEnumerable<byte> bytes) => string.Join(" ", bytes.Select(b => b.ToString("X2")));

    /// <summary>True when the input came from one of this mouse's channels (not the laptop keyboard or touchpad).</summary>
    public static bool BelongsTo(string deviceInstanceId, MouseDevice mouse) =>
        string.Equals(deviceInstanceId, mouse.InstanceId, StringComparison.OrdinalIgnoreCase)
        || mouse.Collections.Any(c => string.Equals(c.InstanceId, deviceInstanceId, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The (usage page, usage) pairs to listen to for this mouse: always mouse, keyboard and media keys, plus
    /// every other channel of the mouse that sends input, such as system keys and the maker's own channels.
    /// Touchpad channels are left out.
    /// </summary>
    public static List<(ushort UsagePage, ushort Usage)> UsagesToListen(MouseDevice mouse)
    {
        var usages = new List<(ushort, ushort)>
        {
            (DeviceIds.UsagePageGenericDesktop, DeviceIds.UsageMouse),
            (DeviceIds.UsagePageGenericDesktop, DeviceIds.UsageKeyboard),
            (DeviceIds.UsagePageConsumer, 0x01),
        };
        foreach (HidCollection c in mouse.Collections)
        {
            if (c.InputReportBytes > 0 && c.UsagePage != DeviceIds.UsagePageDigitizer && !usages.Contains((c.UsagePage, c.Usage)))
            {
                usages.Add((c.UsagePage, c.Usage));
            }
        }
        return usages;
    }
}
