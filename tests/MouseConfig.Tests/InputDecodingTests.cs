using MouseConfig.Core.Devices;
using MouseConfig.Core.Input;

namespace MouseConfig.Tests;

public sealed class InputDecodingTests
{
    private const string Dev = @"HID\VID_1BCF&PID_08A0&COL01\6&1&0000";

    [Theory]
    [InlineData(0x0001, 1, true)]
    [InlineData(0x0002, 1, false)]
    [InlineData(0x0004, 2, true)]
    [InlineData(0x0010, 3, true)]
    [InlineData(0x0040, 4, true)]
    [InlineData(0x0080, 4, false)]
    [InlineData(0x0100, 5, true)]
    [InlineData(0x0200, 5, false)]
    public void Button_flags_give_button_and_direction(int flags, int button, bool isDown)
    {
        InputEvent e = Assert.Single(InputDecoding.FromMouse(Dev, (ushort)flags, 0));

        Assert.Equal(InputKind.MouseButton, e.Kind);
        Assert.Equal(button, e.Code);
        Assert.Equal(isDown, e.IsDown);
    }

    [Fact]
    public void Several_flags_in_one_report_give_several_events()
    {
        List<InputEvent> events = InputDecoding.FromMouse(Dev, 0x0001 | 0x0008, 0);

        Assert.Equal([(1, true), (2, false)], events.Select(e => (e.Code, e.IsDown)));
    }

    [Fact]
    public void Wheel_delta_is_signed()
    {
        InputEvent up = Assert.Single(InputDecoding.FromMouse(Dev, 0x0400, 120));
        InputEvent down = Assert.Single(InputDecoding.FromMouse(Dev, 0x0400, unchecked((ushort)-120)));
        InputEvent tilt = Assert.Single(InputDecoding.FromMouse(Dev, 0x0800, unchecked((ushort)-120)));

        Assert.Equal((InputKind.Wheel, 120, "Wheel up"), (up.Kind, up.Code, up.Detail));
        Assert.Equal((InputKind.Wheel, -120, "Wheel down"), (down.Kind, down.Code, down.Detail));
        Assert.Equal((InputKind.TiltWheel, -120, "Tilt left"), (tilt.Kind, tilt.Code, tilt.Detail));
    }

    [Fact]
    public void No_movement_and_no_buttons_gives_no_events()
    {
        Assert.Empty(InputDecoding.FromMouse(Dev, 0, 0));
    }

    [Fact]
    public void Movement_gives_a_move_event()
    {
        InputEvent move = Assert.Single(InputDecoding.FromMouse(Dev, 0, 0, moveX: 3, moveY: 0));

        Assert.Equal(InputKind.Move, move.Kind);
    }

    [Fact]
    public void Report_rate_counts_reports_in_the_last_second()
    {
        var meter = new ReportRateMeter();
        for (int i = 0; i <= 1500; i++)
        {
            meter.AddMove(TimeSpan.FromMilliseconds(i)); // 1000 reports per second
        }

        Assert.InRange(meter.Current, 999, 1001);
        Assert.InRange(meter.Peak, 999, 1001);

        meter.Reset();
        meter.AddMove(TimeSpan.FromSeconds(10));
        Assert.Equal(0, meter.Current); // not a full second yet
    }

    [Theory]
    [InlineData(124, 125)]
    [InlineData(980, 1000)]
    [InlineData(470, 500)]
    [InlineData(700, null)]
    [InlineData(0, null)]
    public void Nearest_standard_rate(int measured, int? expected)
    {
        Assert.Equal(expected, ReportRateMeter.NearestStandard(measured));
    }

    [Fact]
    public void Key_break_flag_means_release()
    {
        Assert.True(InputDecoding.FromKeyboard(Dev, 0x00, 0x74).IsDown);
        Assert.False(InputDecoding.FromKeyboard(Dev, 0x01, 0x74).IsDown);
        Assert.False(InputDecoding.FromKeyboard(Dev, 0x03, 0x74).IsDown); // break + E0 prefix
    }

    [Fact]
    public void Media_key_press_and_release_come_from_report_changes()
    {
        var none = new HashSet<ushort>();
        var volumeUp = new HashSet<ushort> { 0xE9 };

        InputEvent press = Assert.Single(InputDecoding.DiffUsages(Dev, 0x0C, none, volumeUp));
        Assert.Empty(InputDecoding.DiffUsages(Dev, 0x0C, volumeUp, volumeUp));
        InputEvent release = Assert.Single(InputDecoding.DiffUsages(Dev, 0x0C, volumeUp, none));

        Assert.Equal((InputKind.MediaKey, 0xE9, true, "Volume up"), (press.Kind, press.Code, press.IsDown, press.Detail));
        Assert.False(release.IsDown);
    }

    [Fact]
    public void System_keys_use_generic_desktop_names()
    {
        InputEvent sleep = Assert.Single(InputDecoding.DiffUsages(Dev, 0x01, new HashSet<ushort>(), new HashSet<ushort> { 0x82 }));

        Assert.Equal((InputKind.SystemKey, "Sleep"), (sleep.Kind, sleep.Detail));
        Assert.Equal("Media key 0x999", InputDecoding.UsageName(0x0C, 0x999));
    }

    [Fact]
    public void Hex_has_spaces_between_bytes()
    {
        Assert.Equal("05 01 FF", InputDecoding.Hex([0x05, 0x01, 0xFF]));
    }

    [Fact]
    public void Only_the_mouse_own_channels_belong_to_it()
    {
        MouseDevice mouse = Redgear();

        Assert.True(InputDecoding.BelongsTo(@"hid\vid_1bcf&pid_08a0&col01\6&1&0000", mouse));
        Assert.True(InputDecoding.BelongsTo(@"HID\VID_1BCF&PID_08A0&COL02\6&1&0001", mouse));
        Assert.False(InputDecoding.BelongsTo(@"ACPI\HPQ8001\4&1&0", mouse)); // laptop keyboard
    }

    [Fact]
    public void Listens_to_all_input_channels_except_touchpad_and_settings_only_ones()
    {
        MouseDevice mouse = Redgear() with
        {
            Collections = [.. Redgear().Collections, new HidCollection(@"HID\X&COL09\1", 0x0D, 0x05, 12, 0, 0)],
        };

        List<(ushort, ushort)> usages = InputDecoding.UsagesToListen(mouse);

        Assert.Equal([(0x01, 0x02), (0x01, 0x06), (0x0C, 0x01), (0xFF01, 0x01)], usages);
    }

    private static MouseDevice Redgear() => new()
    {
        RawInputPath = @"\\?\HID#VID_1BCF&PID_08A0&Col01#6&1&0000#{378de44c-56ef-11d1-bc8c-00a0c91405dd}",
        InstanceId = @"HID\VID_1BCF&PID_08A0&Col01\6&1&0000",
        Name = "USB mouse 1BCF:08A0",
        Collections =
        [
            new(@"HID\VID_1BCF&PID_08A0&COL01\6&1&0000", 0x01, 0x02, 7, 0, 0),
            new(@"HID\VID_1BCF&PID_08A0&COL02\6&1&0001", 0x01, 0x06, 8, 2, 0),
            new(@"HID\VID_1BCF&PID_08A0&COL03\6&1&0002", 0x0C, 0x01, 3, 0, 0),
            new(@"HID\VID_1BCF&PID_08A0&COL04\6&1&0003", 0xFF00, 0x01, 0, 0, 8),
            new(@"HID\VID_1BCF&PID_08A0&COL05\6&1&0004", 0xFF01, 0x01, 6, 0, 0),
        ],
    };
}
