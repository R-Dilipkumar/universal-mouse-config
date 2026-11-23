namespace MouseConfig.Plugins.Sunplus1BCF;

/// <summary>
/// Keyboard keys as the mouse stores them: HID keyboard usage numbers (USB HID usage tables, page 0x07),
/// plus the conversion from Windows virtual-key numbers, which is what a key press in the app gives us.
/// </summary>
public static class HidKeys
{
    public const byte ModCtrl = 0x01;
    public const byte ModShift = 0x02;
    public const byte ModAlt = 0x04;
    public const byte ModWin = 0x08;

    private static readonly (int VirtualKey, byte Usage, string Name)[] Keys = BuildTable();

    public static string? Name(byte usage) => Keys.FirstOrDefault(k => k.Usage == usage).Name;

    public static byte? UsageFromVirtualKey(int virtualKey)
    {
        foreach ((int vk, byte usage, _) in Keys)
        {
            if (vk == virtualKey)
            {
                return usage;
            }
        }
        return null;
    }

    public static string ModifierText(byte modifiers)
    {
        var parts = new List<string>();
        if ((modifiers & ModCtrl) != 0) parts.Add("Ctrl");
        if ((modifiers & ModShift) != 0) parts.Add("Shift");
        if ((modifiers & ModAlt) != 0) parts.Add("Alt");
        if ((modifiers & ModWin) != 0) parts.Add("Win");
        return parts.Count == 0 ? "" : string.Join(" + ", parts) + " + ";
    }

    private static (int, byte, string)[] BuildTable()
    {
        var keys = new List<(int, byte, string)>();
        for (int i = 0; i < 26; i++)
        {
            keys.Add((0x41 + i, (byte)(0x04 + i), ((char)('A' + i)).ToString()));
        }
        for (int i = 0; i < 9; i++)
        {
            keys.Add((0x31 + i, (byte)(0x1E + i), ((char)('1' + i)).ToString()));
        }
        keys.Add((0x30, 0x27, "0"));
        keys.AddRange([
            (0x0D, 0x28, "Enter"), (0x1B, 0x29, "Esc"), (0x08, 0x2A, "Backspace"), (0x09, 0x2B, "Tab"), (0x20, 0x2C, "Space"),
            (0xBD, 0x2D, "-"), (0xBB, 0x2E, "="), (0xDB, 0x2F, "["), (0xDD, 0x30, "]"), (0xDC, 0x31, "\\"),
            (0xBA, 0x33, ";"), (0xDE, 0x34, "'"), (0xC0, 0x35, "`"), (0xBC, 0x36, ","), (0xBE, 0x37, "."), (0xBF, 0x38, "/"),
            (0x14, 0x39, "Caps Lock"),
        ]);
        for (int i = 0; i < 12; i++)
        {
            keys.Add((0x70 + i, (byte)(0x3A + i), $"F{i + 1}"));
        }
        keys.AddRange([
            (0x2C, 0x46, "Print Screen"), (0x91, 0x47, "Scroll Lock"), (0x13, 0x48, "Pause"),
            (0x2D, 0x49, "Insert"), (0x24, 0x4A, "Home"), (0x21, 0x4B, "Page up"), (0x2E, 0x4C, "Delete"),
            (0x23, 0x4D, "End"), (0x22, 0x4E, "Page down"),
            (0x27, 0x4F, "Right arrow"), (0x25, 0x50, "Left arrow"), (0x28, 0x51, "Down arrow"), (0x26, 0x52, "Up arrow"),
            (0x90, 0x53, "Num Lock"), (0x6F, 0x54, "Num /"), (0x6A, 0x55, "Num *"), (0x6D, 0x56, "Num -"), (0x6B, 0x57, "Num +"),
        ]);
        for (int i = 0; i < 9; i++)
        {
            keys.Add((0x61 + i, (byte)(0x59 + i), $"Num {i + 1}"));
        }
        keys.AddRange([(0x60, 0x62, "Num 0"), (0x6E, 0x63, "Num ."), (0x5D, 0x65, "Menu")]);
        for (int i = 0; i < 12; i++)
        {
            keys.Add((0x7C + i, (byte)(0x68 + i), $"F{i + 13}"));
        }
        return keys.ToArray();
    }
}
