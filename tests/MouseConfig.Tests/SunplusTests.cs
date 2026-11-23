using MouseConfig.Core;
using MouseConfig.Core.Devices;
using MouseConfig.Plugins.Sunplus1BCF;

namespace MouseConfig.Tests;

public sealed class SunplusTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "MouseConfigTests", Guid.NewGuid().ToString("N"));

    /// <summary>Acts like the mouse's settings channel. Records every command it gets.</summary>
    private sealed class FakeMouse(byte[] config) : IFeatureChannel
    {
        private byte _lastCommand;
        private int _cursor;
        public List<byte> Commands { get; } = [];
        public byte[] Identity { get; set; } = [0xAA, 0x0C, 0xFD];
        public byte HandshakeAnswer { get; set; } = 0xA6;
        public int NotReadyBeforeEachAnswer { get; set; }
        public byte? FailStatusOn { get; set; }
        public int Gets { get; private set; }
        public bool BadEcho { get; set; }
        public bool IgnoreWrites { get; set; }
        public byte[] Stored => config;
        private int _notReadyLeft;
        private int _writeCursor;
        private int _writeEnd;
        private byte[] _lastChunk = [];

        public void SetFeature(byte[] report)
        {
            Assert.Equal(8, report.Length);
            Assert.Equal(0x04, report[0]);
            _lastCommand = report[1];
            Commands.Add(report[1]);
            _notReadyLeft = NotReadyBeforeEachAnswer;
            if (report[1] == 0x04)
            {
                _cursor = report[2] | (report[3] << 8);
                Assert.Equal(255, (report[4] << 8) | report[5]);
            }
            if (report[1] == 0x06)
            {
                _writeCursor = report[2] | (report[3] << 8);
                _writeEnd = _writeCursor + ((report[4] << 8) | report[5]);
            }
            if (report[1] == 0x07)
            {
                _lastChunk = report[2..8];
                for (int i = 0; i < 6 && _writeCursor < _writeEnd; i++, _writeCursor++)
                {
                    if (!IgnoreWrites)
                    {
                        config[_writeCursor] = _lastChunk[i];
                    }
                }
            }
        }

        public byte[] GetFeature()
        {
            Gets++;
            var reply = new byte[8];
            reply[0] = 0x04;
            if (_notReadyLeft-- > 0)
            {
                return reply; // status 0x00 = not ready
            }
            reply[7] = FailStatusOn == _lastCommand ? (byte)0xEE : (byte)0xCC;
            switch (_lastCommand)
            {
                case 0x01:
                    reply[1] = HandshakeAnswer;
                    break;
                case 0x03:
                    Identity.CopyTo(reply, 1);
                    break;
                case 0x05:
                    for (int i = 0; i < 6; i++)
                    {
                        reply[1 + i] = _cursor + i < config.Length ? config[_cursor + i] : (byte)0xFF;
                    }
                    _cursor += 6;
                    break;
                case 0x07:
                    _lastChunk.CopyTo(reply, 1);
                    if (BadEcho)
                    {
                        reply[1] ^= 0xFF;
                    }
                    break;
            }
            return reply;
        }
    }

    /// <summary>A believable settings block: 4 DPI slots, slot 2 active, rainbow lighting speed 5, side buttons as arrow-key "key mode".</summary>
    private static byte[] GoodConfig()
    {
        var data = new byte[255];
        data[2] = 4;
        data[6] = 2; data[7] = 3; data[8] = 5; data[9] = 8; // 800, 1000, 1600, 3200
        data[47] = SunplusConfig.Checksum(data.AsSpan(0, 47));
        data[48] = 0x01; data[49] = 0xFE;
        data[50] = (byte)(3 | ((8 - 5) << 5)); data[51] = (byte)(data[50] ^ 0xFF);
        data[52] = 0x00; data[53] = 0xFF;
        byte[][] buttons = [[1, 1, 0], [1, 2, 0], [1, 4, 0], [0x0F, 0, 0], [0x0F, 0, 0], [5, 0, 0], [0, 0, 0]];
        for (int i = 0; i < buttons.Length; i++)
        {
            buttons[i].CopyTo(data, 64 + 3 * i);
        }
        data[118] = SunplusConfig.Checksum(data.AsSpan(64, 54));
        return data;
    }

    private static readonly byte[] ReadSessionOrder =
        [0x01, 0x03, 0x04, .. Enumerable.Repeat((byte)0x05, 43), 0x08, 0x02];

    [Fact]
    public void Read_session_sends_the_reference_order_and_returns_the_block()
    {
        byte[] data = GoodConfig();
        var mouse = new FakeMouse(data);

        SunplusConfig config = new SunplusSession(mouse, _ => { }).ReadConfig();

        Assert.Equal(ReadSessionOrder, mouse.Commands);
        Assert.Equal(data, config.Data);
        Assert.DoesNotContain(mouse.Commands, c => c is 0x06 or 0x07); // read-only: never a write command
    }

    [Fact]
    public void Not_ready_answers_retry_the_get_but_never_resend_the_command()
    {
        var mouse = new FakeMouse(GoodConfig()) { NotReadyBeforeEachAnswer = 3 };

        new SunplusSession(mouse, _ => { }).ReadConfig();

        Assert.Equal(ReadSessionOrder, mouse.Commands);
        Assert.Equal(ReadSessionOrder.Length * 4, mouse.Gets);
    }

    [Fact]
    public void Mouse_that_never_gets_ready_is_an_error()
    {
        var mouse = new FakeMouse(GoodConfig()) { NotReadyBeforeEachAnswer = 8 };

        var error = Assert.Throws<SunplusProtocolException>(() => new SunplusSession(mouse, _ => { }).ReadConfig());

        Assert.Contains("didn't answer", error.Message);
        Assert.Equal([0x01], mouse.Commands); // handshake never answered: config mode not entered, nothing to close
    }

    [Fact]
    public void Wrong_identity_stops_before_reading_but_still_leaves_config_mode()
    {
        var mouse = new FakeMouse(GoodConfig()) { Identity = [0xAA, 0x0C, 0x00] };

        var error = Assert.Throws<SunplusProtocolException>(() => new SunplusSession(mouse, _ => { }).ReadConfig());

        Assert.Contains("Unsupported", error.Message);
        Assert.Equal([0x01, 0x03, 0x08, 0x02], mouse.Commands);
    }

    [Fact]
    public void Untested_but_known_identity_can_be_read_and_is_marked()
    {
        var mouse = new FakeMouse(GoodConfig()) { Identity = [0xAA, 0x0E, 0xFD] };
        var session = new SunplusSession(mouse, _ => { });

        session.ReadConfig();

        Assert.False(session.IsTestedIdentity);
        Assert.Equal(ReadSessionOrder, mouse.Commands);
    }

    [Fact]
    public void Wrong_handshake_answer_still_leaves_config_mode()
    {
        var mouse = new FakeMouse(GoodConfig()) { HandshakeAnswer = 0x00 };

        Assert.Throws<SunplusProtocolException>(() => new SunplusSession(mouse, _ => { }).ReadConfig());

        Assert.Equal([0x01, 0x08, 0x02], mouse.Commands);
    }

    [Fact]
    public void Error_status_in_the_middle_still_sends_exit()
    {
        var mouse = new FakeMouse(GoodConfig()) { FailStatusOn = 0x04 };

        var error = Assert.Throws<SunplusProtocolException>(() => new SunplusSession(mouse, _ => { }).ReadConfig());

        Assert.Contains("0xEE", error.Message);
        Assert.Equal([0x01, 0x03, 0x04, 0x08, 0x02], mouse.Commands);
    }

    [Fact]
    public void Exit_is_sent_even_when_0x08_fails()
    {
        var mouse = new FakeMouse(GoodConfig()) { FailStatusOn = 0x08 };

        var error = Assert.Throws<SunplusProtocolException>(() => new SunplusSession(mouse, _ => { }).ReadConfig());

        Assert.Contains("Closing", error.Message);
        Assert.Equal(0x02, mouse.Commands[^1]);
    }

    [Fact]
    public void Config_decodes_dpi_lighting_and_buttons()
    {
        var config = new SunplusConfig(GoodConfig());

        Assert.Equal([800, 1000, 1600, 3200], config.DpiValues);
        Assert.Equal(2, config.ActiveDpiSlot);
        Assert.Equal(("Rainbow", 5), (config.LightingModeName, config.LightingSpeed));
        Assert.Equal(["Left click", "Right click", "Middle click", "Key mode", "Key mode", "DPI cycle", "Disabled"],
            config.Buttons.Select(b => b.ActionName));
        Assert.Equal(73, config.Buttons[3].Offset);
        Assert.True(config.CanBeChanged);
        Assert.All(config.Checks(), c => Assert.True(c.Ok, c.Name));
    }

    [Fact]
    public void Stale_main_checksum_is_info_only()
    {
        byte[] data = GoodConfig();
        data[47] ^= 0x01;
        var config = new SunplusConfig(data);

        ConfigCheck main = config.Checks().Single(c => c.Name == "Main checksum");
        Assert.False(main.Ok);
        Assert.False(main.BlocksWrites);
        Assert.True(config.CanBeChanged);
    }

    [Theory]
    [InlineData(118)] // button checksum
    [InlineData(49)]  // active-slot pair
    [InlineData(51)]  // lighting pair
    [InlineData(53)]
    [InlineData(2)]   // DPI count
    public void Broken_blocks_cannot_be_changed(int offset)
    {
        byte[] data = GoodConfig();
        data[offset] = offset == 2 ? (byte)9 : (byte)(data[offset] ^ 0x01);

        Assert.False(new SunplusConfig(data).CanBeChanged);
    }

    [Fact]
    public void No_left_click_anywhere_blocks_changes()
    {
        byte[] data = GoodConfig();
        data[64] = 0; data[65] = 0;
        data[118] = SunplusConfig.Checksum(data.AsSpan(64, 54));

        Assert.False(new SunplusConfig(data).CanBeChanged);
    }

    [Fact]
    public void Unknown_action_codes_show_their_bytes()
    {
        Assert.Equal("Unknown (0F0025)", SunplusActions.NameOf([0x0F, 0x00, 0x25]));
        Assert.Equal("Unknown (0200FF)", SunplusActions.NameOf([0x02, 0x00, 0xFF]));
        Assert.Equal("Browser back", SunplusActions.NameOf([0x03, 0x02, 0x24]));
    }

    [Fact]
    public async Task Plugin_saves_one_backup_and_never_overwrites_it()
    {
        byte[] data = GoodConfig();
        var paths = new AppPaths(_folder);
        var plugin = new SunplusPlugin(paths, new Log(paths.LogsFolder), _ => new FakeMouse(data));
        MouseDevice mouse = Redgear();

        HardwareReadResult first = await plugin.ReadAsync(mouse);
        HardwareReadResult second = await plugin.ReadAsync(mouse);

        Assert.NotNull(first.NewBackupFile);
        Assert.Equal(data, await File.ReadAllBytesAsync(first.NewBackupFile!, TestContext.Current.CancellationToken));
        Assert.Null(second.NewBackupFile);
        Assert.Equal(first.NewBackupFile, second.ExistingBackupFile);
        Assert.Single(Directory.GetFiles(paths.BackupsFolder));
        Assert.Equal("AA0CFD", first.IdentityHex);
    }

    private static readonly byte[] BrowserBack = [0x03, 0x02, 0x24];

    [Fact]
    public void Changing_a_side_button_only_touches_its_slot_and_the_button_checksum()
    {
        var before = new SunplusConfig(GoodConfig());

        SunplusConfig after = before.WithButton(4, BrowserBack);

        Assert.Equal([73, 74, 75, 118], after.DifferencesFrom(before));
        Assert.Equal("Browser back", after.Buttons[3].ActionName);
        Assert.True(after.CanBeChanged);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    public void Only_slots_1_to_7_exist(int slot)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SunplusConfig(GoodConfig()).WithButton(slot, BrowserBack));
    }

    [Fact]
    public void Any_key_with_modifiers_can_go_on_any_slot()
    {
        byte[] ctrlShiftG = SunplusActions.KeyCode(HidKeys.ModCtrl | HidKeys.ModShift, HidKeys.UsageFromVirtualKey(0x47)!.Value);

        SunplusConfig after = new SunplusConfig(GoodConfig())
            .WithButton(4, SunplusActions.KeyCode(0, HidKeys.UsageFromVirtualKey(0x41)!.Value))
            .WithButton(6, ctrlShiftG);

        Assert.Equal("Key: A", after.Buttons[3].ActionName);
        Assert.Equal("Key: Ctrl + Shift + G", after.Buttons[5].ActionName);
        Assert.Equal([0x02, 0x03, 0x0A], after.Buttons[5].Code);
        Assert.True(after.CanBeChanged);
    }

    [Fact]
    public void Left_click_can_move_but_never_disappear()
    {
        var config = new SunplusConfig(GoodConfig());
        byte[] keyH = SunplusActions.KeyCode(0, HidKeys.UsageFromVirtualKey(0x48)!.Value);

        // Left button types H, but slot 4 becomes left click: allowed.
        SunplusConfig moved = config.WithButton(4, SunplusActions.Left).WithButton(1, keyH);
        Assert.Equal(("Key: H", "Left click"), (moved.Buttons[0].ActionName, moved.Buttons[3].ActionName));

        // No left click anywhere: refused.
        var error = Assert.Throws<InvalidOperationException>(() => config.WithButton(1, keyH));
        Assert.Contains("left click", error.Message);
    }

    [Theory]
    [InlineData(0x41, 0x04, "A")]
    [InlineData(0x5A, 0x1D, "Z")]
    [InlineData(0x31, 0x1E, "1")]
    [InlineData(0x30, 0x27, "0")]
    [InlineData(0x70, 0x3A, "F1")]
    [InlineData(0x7B, 0x45, "F12")]
    [InlineData(0x87, 0x73, "F24")]
    [InlineData(0x25, 0x50, "Left arrow")]
    [InlineData(0x20, 0x2C, "Space")]
    [InlineData(0xBF, 0x38, "/")]
    [InlineData(0x60, 0x62, "Num 0")]
    [InlineData(0x2E, 0x4C, "Delete")]
    public void Windows_keys_map_to_mouse_key_codes(int virtualKey, int usage, string name)
    {
        Assert.Equal((byte)usage, HidKeys.UsageFromVirtualKey(virtualKey));
        Assert.Equal(name, HidKeys.Name((byte)usage));
    }

    [Fact]
    public void Keys_the_mouse_cant_store_are_refused()
    {
        Assert.Null(HidKeys.UsageFromVirtualKey(0x10)); // Shift on its own
        Assert.Throws<ArgumentException>(() => SunplusActions.KeyCode(0x10, 0x04)); // right-hand modifier bits
        Assert.Throws<ArgumentException>(() => SunplusActions.KeyCode(0, 0xFF));
    }

    [Fact]
    public void A_broken_block_is_never_changed()
    {
        byte[] data = GoodConfig();
        data[118] ^= 0x01;

        Assert.Throws<InvalidOperationException>(() => new SunplusConfig(data).WithButton(4, BrowserBack));
    }

    [Fact]
    public void Write_session_writes_only_the_button_area_in_the_reference_order()
    {
        byte[] data = GoodConfig();
        var mouse = new FakeMouse(data);
        var before = new SunplusConfig((byte[])data.Clone());
        SunplusConfig wanted = before.WithButton(5, BrowserBack);

        new SunplusSession(mouse, _ => { }).WriteConfig(before, wanted, allowUntestedIdentity: false);

        byte[] expected = [0x01, 0x03, 0x04, .. Enumerable.Repeat((byte)0x05, 43), 0x06, .. Enumerable.Repeat((byte)0x07, 10), 0x08, 0x02];
        Assert.Equal(expected, mouse.Commands);
        Assert.Equal(wanted.Data, mouse.Stored);
    }

    [Fact]
    public void Untested_identity_is_not_written_without_ok()
    {
        byte[] data = GoodConfig();
        var mouse = new FakeMouse(data) { Identity = [0xAA, 0x0E, 0xFD] };
        var before = new SunplusConfig((byte[])data.Clone());

        var error = Assert.Throws<SunplusProtocolException>(() =>
            new SunplusSession(mouse, _ => { }).WriteConfig(before, before.WithButton(4, BrowserBack), allowUntestedIdentity: false));

        Assert.Contains("needs your OK", error.Message);
        Assert.DoesNotContain(mouse.Commands, c => c is 0x06 or 0x07);
        Assert.Equal(before.Data, mouse.Stored);
    }

    [Fact]
    public void Untested_identity_is_written_with_ok()
    {
        byte[] data = GoodConfig();
        var mouse = new FakeMouse(data) { Identity = [0xAA, 0x0E, 0xFD] };
        var before = new SunplusConfig((byte[])data.Clone());
        SunplusConfig wanted = before.WithButton(4, BrowserBack);

        new SunplusSession(mouse, _ => { }).WriteConfig(before, wanted, allowUntestedIdentity: true);

        Assert.Equal(wanted.Data, mouse.Stored);
    }

    [Fact]
    public void Settings_changed_since_reading_are_not_overwritten()
    {
        byte[] data = GoodConfig();
        var mouse = new FakeMouse(data);
        var staleRead = new SunplusConfig((byte[])data.Clone());
        data[50] ^= 0x01; data[51] ^= 0x01; // someone changed lighting on the mouse meanwhile

        Assert.Throws<SunplusProtocolException>(() =>
            new SunplusSession(mouse, _ => { }).WriteConfig(staleRead, staleRead.WithButton(4, BrowserBack), false));

        Assert.DoesNotContain(mouse.Commands, c => c is 0x06 or 0x07);
        Assert.Equal(0x02, mouse.Commands[^1]);
    }

    [Fact]
    public void Bad_echo_is_an_error_and_still_exits()
    {
        byte[] data = GoodConfig();
        var mouse = new FakeMouse(data) { BadEcho = true };
        var before = new SunplusConfig((byte[])data.Clone());

        var error = Assert.Throws<SunplusProtocolException>(() =>
            new SunplusSession(mouse, _ => { }).WriteConfig(before, before.WithButton(4, BrowserBack), false));

        Assert.Contains("echo", error.Message);
        Assert.Equal([0x08, 0x02], mouse.Commands[^2..]);
    }

    [Fact]
    public async Task Plugin_write_reads_back_and_fails_loudly_on_mismatch()
    {
        byte[] data = GoodConfig();
        var mouse = new FakeMouse(data) { IgnoreWrites = true };
        var paths = new AppPaths(_folder);
        var plugin = new SunplusPlugin(paths, new Log(paths.LogsFolder), _ => mouse);
        var before = new SunplusConfig((byte[])data.Clone());

        var error = await Assert.ThrowsAsync<SunplusProtocolException>(() =>
            plugin.WriteAsync(Redgear(), before, before.WithButton(4, BrowserBack), false));

        Assert.Contains("doesn't hold what was written", error.Message);
    }

    [Fact]
    public async Task Plugin_write_returns_what_the_mouse_holds()
    {
        byte[] data = GoodConfig();
        var mouse = new FakeMouse(data);
        var paths = new AppPaths(_folder);
        var plugin = new SunplusPlugin(paths, new Log(paths.LogsFolder), _ => mouse);
        var before = new SunplusConfig((byte[])data.Clone());

        SunplusConfig after = await plugin.WriteAsync(Redgear(), before, before.WithButton(4, BrowserBack), false);

        Assert.Equal("Browser back", after.Buttons[3].ActionName);
    }

    [Fact]
    public void Restoring_a_backup_takes_only_the_writable_areas()
    {
        var backup = new SunplusConfig(GoodConfig());
        byte[] nowData = GoodConfig();
        nowData[200] = 0x77; // outside the writable areas: must stay
        SunplusConfig now = new SunplusConfig(nowData).WithButton(4, BrowserBack);

        SunplusConfig restored = now.WithWritableRegionsFrom(backup);

        Assert.Equal("Key mode", restored.Buttons[3].ActionName);
        Assert.Equal(0x77, restored.Data[200]);
    }

    [Fact]
    public void Active_dpi_step_changes_only_bytes_48_and_49()
    {
        var before = new SunplusConfig(GoodConfig());
        byte[] data = GoodConfig();
        data[48] = 0x31; data[49] = 0xCE; // keep a high half to prove it's kept
        var withHighBits = new SunplusConfig(data);

        SunplusConfig after = before.WithActiveDpi(4);
        SunplusConfig keptHigh = withHighBits.WithActiveDpi(3);

        Assert.Equal([48, 49], after.DifferencesFrom(before));
        Assert.Equal(4, after.ActiveDpiSlot);
        Assert.Equal(0x32, keptHigh.Data[48]);
        Assert.Equal(0xCD, keptHigh.Data[49]);
        Assert.True(after.CanBeChanged);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)] // the test block has 4 steps
    public void Active_dpi_step_must_exist(int step)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SunplusConfig(GoodConfig()).WithActiveDpi(step));
    }

    [Fact]
    public void Lighting_changes_only_bytes_50_and_51_and_round_trips()
    {
        var before = new SunplusConfig(GoodConfig());

        SunplusConfig after = before.WithLighting(4, 8);

        Assert.Equal([50, 51], after.DifferencesFrom(before));
        Assert.Equal(("Flow", 8), (after.LightingModeName, after.LightingSpeed));
        Assert.Equal(0x04, after.Data[50]); // speed 8 → top bits 0
        Assert.Equal(0xFB, after.Data[51]);
        Assert.Equal(1, before.WithLighting(7, 1).LightingSpeed);
        Assert.Equal(0xE7, before.WithLighting(7, 1).Data[50]); // off, speed 1 → top bits 7, bit 4 clear
        Assert.True(after.CanBeChanged);
    }

    [Theory]
    [InlineData(0, 4)]
    [InlineData(8, 4)]
    [InlineData(3, 0)]
    [InlineData(3, 9)]
    public void Lighting_values_must_be_in_range(int mode, int speed)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SunplusConfig(GoodConfig()).WithLighting(mode, speed));
    }

    [Fact]
    public void Key_actions_are_named()
    {
        Assert.Equal("Key: Left arrow", SunplusActions.NameOf([0x02, 0x00, 0x50]));
        Assert.Equal("Key: Right arrow", SunplusActions.NameOf([0x02, 0x00, 0x4F]));
        Assert.Equal("Key: Ctrl + Shift + T", SunplusActions.NameOf([0x02, 0x03, 0x17]));
        Assert.Equal("Show desktop", SunplusActions.NameOf([0x02, 0x08, 0x07]));
    }

    [Fact]
    public void Plugin_finds_the_settings_channel()
    {
        Assert.True(SunplusPlugin.Matches(Redgear()));
        Assert.EndsWith("COL04", SunplusPlugin.SettingsChannel(Redgear())!.ShortName);
        Assert.Null(SunplusPlugin.SettingsChannel(Redgear() with { Collections = [] }));
    }

    private static MouseDevice Redgear() => new()
    {
        RawInputPath = "raw",
        InstanceId = @"HID\VID_1BCF&PID_08A0&COL01\6&1&0000",
        Name = "USB mouse 1BCF:08A0",
        VendorId = 0x1BCF,
        ProductId = 0x08A0,
        Collections = [new(@"HID\VID_1BCF&PID_08A0&COL04\6&1&0003", 0xFF00, 0x01, 0, 0, 8, @"\\?\hid#col04")],
    };

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }
}
