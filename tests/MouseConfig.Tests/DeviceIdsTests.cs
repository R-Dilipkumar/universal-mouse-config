using MouseConfig.Core.Devices;

namespace MouseConfig.Tests;

public sealed class DeviceIdsTests
{
    [Fact]
    public void Raw_input_path_becomes_instance_id()
    {
        string path = @"\\?\HID#VID_1BCF&PID_08A0&Col01#6&12f5541c&1&0000#{378de44c-56ef-11d1-bc8c-00a0c91405dd}";

        Assert.Equal(@"HID\VID_1BCF&PID_08A0&Col01\6&12f5541c&1&0000", DeviceIds.InstanceIdFromInterfacePath(path));
    }

    [Theory]
    [InlineData(@"HID\VID_1BCF&PID_08A0&COL01\6&1", @"USB\VID_1BCF&PID_08A0\5&4", ConnectionType.Usb)]
    [InlineData(@"HID\ELAN0788&COL01\5&7", @"ACPI\ELAN0788\4&7", ConnectionType.BuiltIn)]
    [InlineData(@"HID\{00001124-0000-1000-8000-00805f9b34fb}_VID&0002046d_PID&b01a\8&1", @"BTHENUM\{00001124}\7&1", ConnectionType.Bluetooth)]
    [InlineData(@"HID\{00001812-0000-1000-8000-00805f9b34fb}&Dev&VID_046d&PID_b023&Col01\9&1", @"BTHLEDEVICE\{00001812}\8&1", ConnectionType.Bluetooth)]
    [InlineData(@"ROOT\SOMETHING\0000", @"HTREE\ROOT\0", ConnectionType.Unknown)]
    public void Connection_comes_from_the_device_tree(string self, string parent, ConnectionType expected)
    {
        Assert.Equal(expected, DeviceIds.ConnectionFromAncestors([self, parent]));
    }

    [Fact]
    public void Bluetooth_wins_over_usb_for_mice_on_a_usb_bluetooth_adapter()
    {
        string[] chain = [@"HID\{00001124-0000-1000-8000-00805f9b34fb}_VID&1\8&1", @"BTHENUM\{00001124}\7&1",
            @"BTH\MS_BTHBRB\6&1", @"USB\VID_8087&PID_0026\5&1"];

        Assert.Equal(ConnectionType.Bluetooth, DeviceIds.ConnectionFromAncestors(chain));
    }

    [Fact]
    public void Usb_interface_parent_is_skipped_so_all_interfaces_group_together()
    {
        string? key = DeviceIds.PhysicalDeviceKey([@"USB\VID_046D&PID_C52B&MI_01\7&1", @"USB\VID_046D&PID_C52B\6&1"]);

        Assert.Equal(@"USB\VID_046D&PID_C52B\6&1", key);
    }

    [Fact]
    public void Single_interface_parent_is_the_physical_device()
    {
        Assert.Equal(@"USB\VID_1BCF&PID_08A0\5&4", DeviceIds.PhysicalDeviceKey([@"USB\VID_1BCF&PID_08A0\5&4", @"USB\ROOT_HUB30\4&3"]));
        Assert.Null(DeviceIds.PhysicalDeviceKey([]));
    }

    [Fact]
    public void Touchpad_is_found_by_its_digitizer_channel()
    {
        HidCollection mouse = new(@"HID\ELAN0788&COL01\5&0", 0x01, 0x02, 9, 0, 5);
        HidCollection touchpad = new(@"HID\ELAN0788&COL03\5&2", 0x0D, 0x05, 12, 0, 257);
        HidCollection vendor = new(@"HID\VID_1BCF&PID_08A0&COL04\6&3", 0xFF00, 0x01, 0, 0, 8);

        Assert.True(DeviceIds.IsTouchpad([mouse, touchpad]));
        Assert.False(DeviceIds.IsTouchpad([mouse, vendor]));
    }

    [Fact]
    public void Remote_desktop_mouse_is_virtual()
    {
        Assert.True(DeviceIds.IsVirtualMouse(@"Root\RDP_MOU\0000"));
        Assert.False(DeviceIds.IsVirtualMouse(@"HID\VID_1BCF&PID_08A0&Col01\6&1"));
    }

    [Theory]
    [InlineData("G502 HERO", "Logitech", "Logitech G502 HERO")]
    [InlineData("Logitech USB Receiver", "Logitech", "Logitech USB Receiver")]
    [InlineData("M720", null, "M720")]
    [InlineData("  \0", "Sunplus", "USB mouse 1BCF:08A0")]
    [InlineData(null, null, "USB mouse 1BCF:08A0")]
    public void Display_name_uses_product_string_then_usb_id(string? product, string? maker, string expected)
    {
        Assert.Equal(expected, DeviceIds.DisplayName(product, maker, 0x1BCF, 0x08A0, ConnectionType.Usb));
    }

    [Fact]
    public void Display_name_without_ids_says_the_kind()
    {
        Assert.Equal("Mouse", DeviceIds.DisplayName(null, null, null, null, ConnectionType.Unknown));
    }

    [Theory]
    [InlineData(0x01, 0x02, 0, "Mouse")]
    [InlineData(0x01, 0x06, 0, "Keyboard keys")]
    [InlineData(0x0C, 0x01, 0, "Media keys")]
    [InlineData(0xFF00, 0x01, 8, "Maker's own channel (has settings reports)")]
    [InlineData(0xFF01, 0x01, 0, "Maker's own channel")]
    [InlineData(0x0D, 0x05, 257, "Touchpad")]
    [InlineData(0x09, 0x01, 0, "Other")]
    public void Channels_get_plain_names(int page, int usage, int featureBytes, string expected)
    {
        Assert.Equal(expected, DeviceIds.DescribeUsage((ushort)page, (ushort)usage, featureBytes));
    }

    [Fact]
    public void Collection_label_comes_from_the_instance_id()
    {
        Assert.Equal("COL04", DeviceIds.CollectionLabel(@"HID\VID_1BCF&PID_08A0&COL04\6&12F5541C&1&0003"));
        Assert.Equal("", DeviceIds.CollectionLabel(@"HID\VID_046D&PID_C077\6&1"));
    }
}
