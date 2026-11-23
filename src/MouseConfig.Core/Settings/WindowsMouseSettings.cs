namespace MouseConfig.Core.Settings;

public enum SettingsGroup
{
    Pointer,
    Scroll,
}

/// <summary>Windows' own mouse settings. Windows applies them to every mouse at once.</summary>
public sealed record WindowsMouseSettings
{
    public const int OneScreenAtATime = -1;

    /// <summary>1–20. Windows' default is 10.</summary>
    public int PointerSpeed { get; init; }
    /// <summary>The three SPI_GETMOUSE numbers, kept as found so they can be put back exactly.</summary>
    public int AccelThreshold1 { get; init; }
    public int AccelThreshold2 { get; init; }
    public int Acceleration { get; init; }
    public bool SwapButtons { get; init; }
    public int DoubleClickMs { get; init; }
    public bool ClickLock { get; init; }
    public int ClickLockMs { get; init; }
    public bool HideWhileTyping { get; init; }
    /// <summary>0 = off, 2–7 = trail length.</summary>
    public int PointerTrails { get; init; }
    public bool ShowLocationWithCtrl { get; init; }
    public bool SnapToDefaultButton { get; init; }
    /// <summary>Lines per wheel notch, or <see cref="OneScreenAtATime"/>.</summary>
    public int WheelLines { get; init; }
    public int WheelChars { get; init; }

    /// <summary>"Enhance pointer precision" in Windows Settings: on when acceleration is not 0.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool EnhancePrecision => Acceleration != 0;

    public WindowsMouseSettings WithEnhancePrecision(bool on) => on
        ? this with { AccelThreshold1 = 6, AccelThreshold2 = 10, Acceleration = 1 }
        : this with { AccelThreshold1 = 0, AccelThreshold2 = 0, Acceleration = 0 };

    /// <summary>What a fresh Windows 10/11 install uses.</summary>
    public static WindowsMouseSettings WindowsDefaults { get; } = new()
    {
        PointerSpeed = 10,
        AccelThreshold1 = 6,
        AccelThreshold2 = 10,
        Acceleration = 1,
        SwapButtons = false,
        DoubleClickMs = 500,
        ClickLock = false,
        ClickLockMs = 1200,
        HideWhileTyping = true,
        PointerTrails = 0,
        ShowLocationWithCtrl = false,
        SnapToDefaultButton = false,
        WheelLines = 3,
        WheelChars = 3,
    };

    /// <summary>Each setting with its group and a plain name, so changes can be listed and copied per tab.</summary>
    public static IReadOnlyList<SettingField> Fields { get; } =
    [
        new("Pointer speed", SettingsGroup.Pointer, s => s.PointerSpeed, (s, o) => s with { PointerSpeed = o.PointerSpeed }),
        new("Enhance pointer precision", SettingsGroup.Pointer, s => (s.AccelThreshold1, s.AccelThreshold2, s.Acceleration),
            (s, o) => s with { AccelThreshold1 = o.AccelThreshold1, AccelThreshold2 = o.AccelThreshold2, Acceleration = o.Acceleration }),
        new("Swap left and right buttons", SettingsGroup.Pointer, s => s.SwapButtons, (s, o) => s with { SwapButtons = o.SwapButtons }),
        new("Double-click speed", SettingsGroup.Pointer, s => s.DoubleClickMs, (s, o) => s with { DoubleClickMs = o.DoubleClickMs }),
        new("ClickLock", SettingsGroup.Pointer, s => s.ClickLock, (s, o) => s with { ClickLock = o.ClickLock }),
        new("ClickLock time", SettingsGroup.Pointer, s => s.ClickLockMs, (s, o) => s with { ClickLockMs = o.ClickLockMs }),
        new("Hide pointer while typing", SettingsGroup.Pointer, s => s.HideWhileTyping, (s, o) => s with { HideWhileTyping = o.HideWhileTyping }),
        new("Pointer trails", SettingsGroup.Pointer, s => s.PointerTrails, (s, o) => s with { PointerTrails = o.PointerTrails }),
        new("Show pointer location with Ctrl", SettingsGroup.Pointer, s => s.ShowLocationWithCtrl, (s, o) => s with { ShowLocationWithCtrl = o.ShowLocationWithCtrl }),
        new("Snap to default button", SettingsGroup.Pointer, s => s.SnapToDefaultButton, (s, o) => s with { SnapToDefaultButton = o.SnapToDefaultButton }),
        new("Lines per wheel notch", SettingsGroup.Scroll, s => s.WheelLines, (s, o) => s with { WheelLines = o.WheelLines }),
        new("Characters per tilt", SettingsGroup.Scroll, s => s.WheelChars, (s, o) => s with { WheelChars = o.WheelChars }),
    ];

    /// <summary>This settings object with one group's values taken from <paramref name="source"/>.</summary>
    public WindowsMouseSettings WithGroupFrom(WindowsMouseSettings source, SettingsGroup group) =>
        Fields.Where(f => f.Group == group).Aggregate(this, (result, field) => field.CopyFrom(result, source));

    public IEnumerable<SettingField> DifferencesFrom(WindowsMouseSettings other) =>
        Fields.Where(f => !Equals(f.Read(this), f.Read(other)));
}

public sealed record SettingField(
    string Name,
    SettingsGroup Group,
    Func<WindowsMouseSettings, object> Read,
    Func<WindowsMouseSettings, WindowsMouseSettings, WindowsMouseSettings> CopyFrom);
