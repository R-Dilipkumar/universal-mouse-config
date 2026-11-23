using MouseConfig.Core.Settings;

namespace MouseConfig.Tests;

public sealed class MouseSettingsTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "MouseConfigTests", Guid.NewGuid().ToString("N"));
    private static readonly WindowsMouseSettings Start = WindowsMouseSettings.WindowsDefaults with { PointerSpeed = 7, WheelLines = 5 };

    /// <summary>Stands in for Windows. Can be told to ignore a setting, like Windows sometimes does.</summary>
    private sealed class FakeSystem(WindowsMouseSettings start) : IMouseSettingsSystem
    {
        public WindowsMouseSettings Stored { get; set; } = start;
        public int Writes { get; private set; }
        public bool IgnoreSpeed { get; set; }

        public WindowsMouseSettings Read() => Stored;

        public void Write(WindowsMouseSettings from, WindowsMouseSettings to)
        {
            Writes++;
            Stored = IgnoreSpeed ? to with { PointerSpeed = Stored.PointerSpeed } : to;
        }
    }

    private static MouseSettingsEditor Editor(FakeSystem system) =>
        new(system, new OriginalSettingsSnapshot(1, DateTime.UnixEpoch, Start));

    [Fact]
    public void Change_writes_and_undo_puts_it_back()
    {
        var system = new FakeSystem(Start);
        MouseSettingsEditor editor = Editor(system);

        editor.Change(SettingsGroup.Pointer, editor.Current with { PointerSpeed = 14 });
        Assert.Equal(14, system.Stored.PointerSpeed);
        Assert.True(editor.CanUndo(SettingsGroup.Pointer));

        editor.Undo(SettingsGroup.Pointer);
        Assert.Equal(7, system.Stored.PointerSpeed);
        Assert.False(editor.CanUndo(SettingsGroup.Pointer));
    }

    [Fact]
    public void Undo_on_one_tab_leaves_the_other_tab_alone()
    {
        var system = new FakeSystem(Start);
        MouseSettingsEditor editor = Editor(system);

        editor.Change(SettingsGroup.Pointer, editor.Current with { PointerSpeed = 14 });
        editor.Change(SettingsGroup.Scroll, editor.Current with { WheelLines = 9 });
        editor.Undo(SettingsGroup.Pointer);

        Assert.Equal((7, 9), (system.Stored.PointerSpeed, system.Stored.WheelLines));
    }

    [Fact]
    public void Change_only_touches_its_own_tab()
    {
        var system = new FakeSystem(Start);
        MouseSettingsEditor editor = Editor(system);

        editor.Change(SettingsGroup.Scroll, editor.Current with { PointerSpeed = 20, WheelLines = 1 });

        Assert.Equal((7, 1), (system.Stored.PointerSpeed, system.Stored.WheelLines));
    }

    [Fact]
    public void No_change_means_no_write_and_no_undo_step()
    {
        var system = new FakeSystem(Start);
        MouseSettingsEditor editor = Editor(system);

        editor.Change(SettingsGroup.Pointer, editor.Current);

        Assert.Equal(0, system.Writes);
        Assert.False(editor.CanUndo(SettingsGroup.Pointer));
    }

    [Fact]
    public void Windows_not_keeping_a_value_is_an_error()
    {
        var system = new FakeSystem(Start) { IgnoreSpeed = true };
        MouseSettingsEditor editor = Editor(system);

        var error = Assert.Throws<InvalidOperationException>(() =>
            editor.Change(SettingsGroup.Pointer, editor.Current with { PointerSpeed = 14 }));

        Assert.Contains("Pointer speed", error.Message);
        Assert.Equal(7, editor.Current.PointerSpeed);
        Assert.False(editor.CanUndo(SettingsGroup.Pointer));
    }

    [Fact]
    public void Defaults_and_restore_original()
    {
        var system = new FakeSystem(Start);
        MouseSettingsEditor editor = Editor(system);

        editor.ResetToWindowsDefaults(SettingsGroup.Pointer);
        Assert.Equal((10, 5), (system.Stored.PointerSpeed, system.Stored.WheelLines));

        editor.Change(SettingsGroup.Scroll, editor.Current with { WheelLines = 2 });
        editor.RestoreOriginal();
        Assert.Equal(Start, system.Stored);
    }

    [Fact]
    public void Reload_picks_up_outside_changes()
    {
        var system = new FakeSystem(Start);
        MouseSettingsEditor editor = Editor(system);

        system.Stored = Start with { PointerSpeed = 3 };

        Assert.True(editor.Reload());
        Assert.Equal(3, editor.Current.PointerSpeed);
        Assert.False(editor.Reload());
    }

    [Fact]
    public void Precision_switch_sets_windows_numbers()
    {
        WindowsMouseSettings off = Start.WithEnhancePrecision(false);

        Assert.False(off.EnhancePrecision);
        Assert.Equal((0, 0, 0), (off.AccelThreshold1, off.AccelThreshold2, off.Acceleration));
        Assert.Equal((6, 10, 1), (off.WithEnhancePrecision(true).AccelThreshold1, off.WithEnhancePrecision(true).AccelThreshold2,
            off.WithEnhancePrecision(true).Acceleration));
    }

    [Fact]
    public void Every_field_is_in_exactly_one_group_and_copies()
    {
        WindowsMouseSettings changed = new()
        {
            PointerSpeed = 1, AccelThreshold1 = 1, AccelThreshold2 = 1, Acceleration = 2, SwapButtons = true, DoubleClickMs = 1,
            ClickLock = true, ClickLockMs = 1, HideWhileTyping = false, PointerTrails = 7, ShowLocationWithCtrl = true,
            SnapToDefaultButton = true, WheelLines = 1, WheelChars = 1,
        };

        WindowsMouseSettings copied = WindowsMouseSettings.WindowsDefaults
            .WithGroupFrom(changed, SettingsGroup.Pointer)
            .WithGroupFrom(changed, SettingsGroup.Scroll);

        Assert.Equal(changed, copied); // a field missing from Fields would stay at its default and fail here
    }

    [Fact]
    public void Snapshot_is_written_once_and_never_overwritten()
    {
        string path = Path.Combine(_folder, "original-windows-settings.json");

        OriginalSettingsSnapshot first = OriginalSettingsSnapshot.LoadOrCreate(path, () => Start, new DateTime(2026, 11, 8));
        OriginalSettingsSnapshot second = OriginalSettingsSnapshot.LoadOrCreate(path,
            () => Start with { PointerSpeed = 20 }, new DateTime(2026, 12, 1));

        Assert.Equal(Start, first.Settings);
        Assert.Equal(first, second);
        Assert.Equal(OriginalSettingsSnapshot.CurrentSchemaVersion, second.SchemaVersion);
    }

    [Fact]
    public void Snapshot_with_unknown_version_fails_loudly()
    {
        string path = Path.Combine(_folder, "original-windows-settings.json");
        Directory.CreateDirectory(_folder);
        File.WriteAllText(path, """{ "SchemaVersion": 99, "TakenAt": "2026-11-08T00:00:00", "Settings": { "PointerSpeed": 10 } }""");

        var error = Assert.Throws<InvalidDataException>(() =>
            OriginalSettingsSnapshot.LoadOrCreate(path, () => Start, DateTime.Now));

        Assert.Contains("version 99", error.Message);
    }

    [Fact]
    public void Real_windows_settings_can_be_read()
    {
        WindowsMouseSettings now = new SystemMouseSettings().Read();

        Assert.InRange(now.PointerSpeed, 1, 20);
        Assert.InRange(now.DoubleClickMs, 1, 5000);
        Assert.True(now.WheelLines == WindowsMouseSettings.OneScreenAtATime || now.WheelLines >= 0);
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }
}
