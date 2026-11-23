namespace MouseConfig.Plugins.Sunplus1BCF;

public sealed record ButtonSlot(int Number, int Offset, byte[] Code)
{
    public string CodeHex => Convert.ToHexString(Code);
    public string ActionName => SunplusActions.NameOf(Code);
}

public sealed record ConfigCheck(string Name, bool Ok, bool BlocksWrites, string Detail);

/// <summary>
/// The 255-byte settings block of a Sunplus 1BCF:08A0 mouse, decoded. Pure byte logic, no device access.
/// Layout from the protocol notes in plan.md section 5.
/// </summary>
public sealed class SunplusConfig
{
    public const int Size = 255;
    public const int ButtonTableStart = 64;
    public const int ButtonSlotCount = 7;
    public const int ButtonChecksumOffset = 118;
    public const int MainChecksumOffset = 47;

    private static readonly int[] DpiByCode = [500, 800, 1000, 1200, 1600, 2000, 2400, 3200, 4000, 4800, 6400, 8000];
    private static readonly string[] LightingModes = ["", "DPI colour", "Multicolour", "Rainbow", "Flow", "Waltz", "Seasons", "Off"];

    public SunplusConfig(byte[] data)
    {
        if (data.Length != Size)
        {
            throw new ArgumentException($"Settings block must be {Size} bytes, got {data.Length}.", nameof(data));
        }
        Data = (byte[])data.Clone();
    }

    public byte[] Data { get; }

    public int DpiSlotCount => Data[2];
    public IReadOnlyList<int> DpiCodes => Enumerable.Range(6, Math.Clamp(DpiSlotCount, 0, 7)).Select(i => (int)Data[i]).ToList();
    public IReadOnlyList<int?> DpiValues => DpiCodes.Select(DpiOf).ToList();
    public int ActiveDpiSlot => (Data[48] & 0x0F) + 1;
    public int LightingMode => Data[50] & 0x0F;
    public string LightingModeName => LightingMode is >= 1 and <= 7 ? LightingModes[LightingMode] : $"Unknown ({LightingMode})";
    public int LightingSpeed => 8 - ((Data[50] >> 5) & 0x07);

    public IReadOnlyList<ButtonSlot> Buttons => Enumerable.Range(0, ButtonSlotCount)
        .Select(i => new ButtonSlot(i + 1, ButtonTableStart + 3 * i, Data[(ButtonTableStart + 3 * i)..(ButtonTableStart + 3 * i + 3)]))
        .ToList();

    public static int? DpiOf(int code) => code is >= 1 and <= 12 ? DpiByCode[code - 1] : null;

    public static byte Checksum(ReadOnlySpan<byte> bytes)
    {
        int sum = 0;
        foreach (byte b in bytes)
        {
            sum += b;
        }
        return (byte)((sum & 0xFF) ^ 0x5A);
    }

    /// <summary>Every check from the protocol notes. A failed check that blocks writes means: don't touch this mouse.</summary>
    public IReadOnlyList<ConfigCheck> Checks()
    {
        var checks = new List<ConfigCheck>();
        bool countOk = DpiSlotCount is >= 1 and <= 7;
        checks.Add(new("DPI slot count", countOk, true, $"{DpiSlotCount} (must be 1–7)"));
        if (countOk)
        {
            bool codesOk = DpiCodes.All(c => DpiOf(c) is not null);
            checks.Add(new("DPI values", codesOk, true, string.Join(", ", DpiCodes.Select(c => $"0x{c:X2}"))));
        }

        byte mainExpected = Checksum(Data.AsSpan(0, MainChecksumOffset));
        // The reference tool keeps this one as found (its author's mouse had a stale one), so it's info only.
        checks.Add(new("Main checksum", Data[MainChecksumOffset] == mainExpected, false,
            $"stored 0x{Data[MainChecksumOffset]:X2}, computed 0x{mainExpected:X2} (kept as found)"));

        foreach (int offset in new[] { 48, 50, 52 })
        {
            bool pairOk = (Data[offset] ^ Data[offset + 1]) == 0xFF;
            checks.Add(new($"Pair at {offset}/{offset + 1}", pairOk, true, $"0x{Data[offset]:X2} / 0x{Data[offset + 1]:X2} (must be opposites)"));
        }
        checks.Add(new("Lighting byte", (Data[50] & 0x10) == 0, true, $"0x{Data[50]:X2} (bit 4 must be 0)"));

        byte buttonExpected = Checksum(Data.AsSpan(ButtonTableStart, ButtonChecksumOffset - ButtonTableStart));
        checks.Add(new("Button checksum", Data[ButtonChecksumOffset] == buttonExpected, true,
            $"stored 0x{Data[ButtonChecksumOffset]:X2}, computed 0x{buttonExpected:X2}"));

        bool hasLeft = Buttons.Any(b => b.Code.AsSpan().SequenceEqual(SunplusActions.Left));
        checks.Add(new("A button is left click", hasLeft, true, hasLeft ? "yes" : "no slot holds left click"));
        return checks;
    }

    public bool CanBeChanged => Checks().All(c => c.Ok || !c.BlocksWrites);

    /// <summary>
    /// Every slot can be changed (the reference tool only offers 4 and 5; the user asked for all, 2026-11-09).
    /// The "a button is left click" check still applies, so the mouse can always click.
    /// </summary>
    public static IReadOnlyList<int> ChangeableSlots { get; } = [1, 2, 3, 4, 5, 6, 7];

    /// <summary>The only areas ever written: active DPI + lighting (48–53) and the button table with its checksum (64–123).</summary>
    public static IReadOnlyList<(int Address, int Length)> WritableRegions { get; } = [(48, 6), (64, 60)];

    /// <summary>A copy with one side button's action changed and the button checksum recomputed.</summary>
    public SunplusConfig WithButton(int slot, byte[] code)
    {
        if (!ChangeableSlots.Contains(slot))
        {
            throw new ArgumentOutOfRangeException(nameof(slot), $"Only slots {string.Join(" and ", ChangeableSlots)} can be changed.");
        }
        if (code.Length != 3)
        {
            throw new ArgumentException("An action is 3 bytes.", nameof(code));
        }
        RequireChangeable("the current settings");
        byte[] data = (byte[])Data.Clone();
        code.CopyTo(data, ButtonTableStart + 3 * (slot - 1));
        data[ButtonChecksumOffset] = Checksum(data.AsSpan(ButtonTableStart, ButtonChecksumOffset - ButtonTableStart));
        var changed = new SunplusConfig(data);
        changed.RequireChangeable("the new settings");
        return changed;
    }

    /// <summary>Lighting modes 1–7 with plain names, for pickers.</summary>
    public static IReadOnlyList<(int Mode, string Name)> LightingModeChoices { get; } =
        Enumerable.Range(1, 7).Select(mode => (mode, LightingModes[mode])).ToList();

    public const int LightingModeOff = 7;
    public const int MinLightingSpeed = 1;
    public const int MaxLightingSpeed = 8;

    /// <summary>A copy with another DPI step active (1 = first). Only bytes 48/49 change; the high half of 48 is kept.</summary>
    public SunplusConfig WithActiveDpi(int step)
    {
        if (step < 1 || step > DpiSlotCount)
        {
            throw new ArgumentOutOfRangeException(nameof(step), $"DPI step must be 1–{DpiSlotCount}.");
        }
        RequireChangeable("the current settings");
        byte[] data = (byte[])Data.Clone();
        data[48] = (byte)((data[48] & 0xF0) | (step - 1));
        data[49] = (byte)(data[48] ^ 0xFF);
        var changed = new SunplusConfig(data);
        changed.RequireChangeable("the new settings");
        return changed;
    }

    /// <summary>
    /// A copy with another lighting mode (1–7) and speed (1 slow – 8 fast). Byte 50: low half = mode,
    /// bit 4 = 0, top 3 bits = 8 − speed; byte 51 = its opposite. Only bytes 50/51 change.
    /// </summary>
    public SunplusConfig WithLighting(int mode, int speed)
    {
        if (mode is < 1 or > LightingModeOff)
        {
            throw new ArgumentOutOfRangeException(nameof(mode), "Lighting mode must be 1–7.");
        }
        if (speed is < MinLightingSpeed or > MaxLightingSpeed)
        {
            throw new ArgumentOutOfRangeException(nameof(speed), "Lighting speed must be 1–8.");
        }
        RequireChangeable("the current settings");
        byte[] data = (byte[])Data.Clone();
        data[50] = (byte)(mode | ((MaxLightingSpeed - speed) << 5));
        data[51] = (byte)(data[50] ^ 0xFF);
        var changed = new SunplusConfig(data);
        changed.RequireChangeable("the new settings");
        return changed;
    }

    /// <summary>A copy with the writable areas taken from <paramref name="source"/> (e.g. a backup). Everything else stays.</summary>
    public SunplusConfig WithWritableRegionsFrom(SunplusConfig source)
    {
        source.RequireChangeable("the backup");
        byte[] data = (byte[])Data.Clone();
        foreach ((int address, int length) in WritableRegions)
        {
            Array.Copy(source.Data, address, data, address, length);
        }
        var changed = new SunplusConfig(data);
        changed.RequireChangeable("the restored settings");
        return changed;
    }

    /// <summary>Offsets where two blocks differ.</summary>
    public IReadOnlyList<int> DifferencesFrom(SunplusConfig other) =>
        Enumerable.Range(0, Size).Where(i => Data[i] != other.Data[i]).ToList();

    private void RequireChangeable(string what)
    {
        List<string> failed = Checks().Where(c => !c.Ok && c.BlocksWrites).Select(c => c.Name).ToList();
        if (failed.Count > 0)
        {
            throw new InvalidOperationException($"Not changing the mouse: {what} fail these checks: {string.Join(", ", failed)}.");
        }
    }

    public string HexDump()
    {
        var lines = new List<string>();
        for (int row = 0; row < Size; row += 16)
        {
            int count = Math.Min(16, Size - row);
            lines.Add($"{row,3}  {string.Join(" ", Data.Skip(row).Take(count).Select(b => b.ToString("X2")))}");
        }
        return string.Join(Environment.NewLine, lines);
    }
}

/// <summary>On-mouse button actions (3-byte codes) from the protocol notes.</summary>
public static class SunplusActions
{
    public static readonly byte[] Left = [0x01, 0x01, 0x00];

    public static IReadOnlyList<(string Name, byte[] Code)> All { get; } =
    [
        ("Left click", [0x01, 0x01, 0x00]),
        ("Right click", [0x01, 0x02, 0x00]),
        ("Middle click", [0x01, 0x04, 0x00]),
        ("Back (mouse button 4)", [0x01, 0x08, 0x00]),
        ("Forward (mouse button 5)", [0x01, 0x10, 0x00]),
        ("Disabled", [0x00, 0x00, 0x00]),
        ("DPI cycle", [0x05, 0x00, 0x00]),
        ("DPI up", [0x07, 0x00, 0x00]),
        ("DPI down", [0x08, 0x00, 0x00]),
        ("Lighting on/off", [0x09, 0x00, 0x00]),
        ("Lighting mode", [0x10, 0x00, 0x00]),
        ("Key mode", [0x0F, 0x00, 0x00]),
        ("Play / pause", [0x03, 0x00, 0xCD]),
        ("Stop", [0x03, 0x00, 0xB7]),
        ("Previous track", [0x03, 0x00, 0xB6]),
        ("Next track", [0x03, 0x00, 0xB5]),
        ("Volume up", [0x03, 0x00, 0xE9]),
        ("Volume down", [0x03, 0x00, 0xEA]),
        ("Mute", [0x03, 0x00, 0xE2]),
        ("Browser back", [0x03, 0x02, 0x24]),
        ("Browser forward", [0x03, 0x02, 0x25]),
        ("Show desktop", [0x02, 0x08, 0x07]),
    ];

    public static string NameOf(byte[] code)
    {
        string? known = All.FirstOrDefault(a => a.Code.AsSpan().SequenceEqual(code)).Name;
        if (known is not null)
        {
            return known;
        }
        // 02 [modifiers] [HID key]: a keyboard key, e.g. 02 08 07 = Win + D. Seen on a real mouse: 02 00 50 = Left arrow.
        if (IsKey(code) && HidKeys.Name(code[2]) is { } key)
        {
            return $"Key: {HidKeys.ModifierText(code[1])}{key}";
        }
        return $"Unknown ({Convert.ToHexString(code)})";
    }

    /// <summary>A keyboard key action. Only the four modifier bits (Ctrl, Shift, Alt, Win) are allowed.</summary>
    public static byte[] KeyCode(byte modifiers, byte hidKey)
    {
        if ((modifiers & ~0x0F) != 0 || HidKeys.Name(hidKey) is null)
        {
            throw new ArgumentException($"Not a known key: modifiers 0x{modifiers:X2}, key 0x{hidKey:X2}.");
        }
        return [0x02, modifiers, hidKey];
    }

    public static bool IsKey(byte[] code) => code.Length == 3 && code[0] == 0x02 && (code[1] & ~0x0F) == 0;
}
