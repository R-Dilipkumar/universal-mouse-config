using System.Text.RegularExpressions;

namespace MouseConfig.Core.Devices;

/// <summary>Pure text and number rules about device IDs and HID usages. No Windows calls, so they can be tested.</summary>
public static partial class DeviceIds
{
    public const ushort UsagePageGenericDesktop = 0x01;
    public const ushort UsagePageConsumer = 0x0C;
    public const ushort UsagePageDigitizer = 0x0D;
    public const ushort UsageMouse = 0x02;
    public const ushort UsageKeyboard = 0x06;
    public const ushort UsageWheel = 0x38;
    public const ushort UsageAcPan = 0x238;
    public const ushort UsageTouchpad = 0x05;

    /// <summary>
    /// Turns a device interface path into its device instance ID.
    /// <c>\\?\HID#VID_1BCF&amp;PID_08A0&amp;Col01#6&amp;12f5541c&amp;1&amp;0000#{378de44c-...}</c>
    /// becomes <c>HID\VID_1BCF&amp;PID_08A0&amp;Col01\6&amp;12f5541c&amp;1&amp;0000</c>.
    /// </summary>
    public static string InstanceIdFromInterfacePath(string path)
    {
        string id = path;
        if (id.StartsWith(@"\\?\", StringComparison.Ordinal) || id.StartsWith(@"\??\", StringComparison.Ordinal))
        {
            id = id[4..];
        }
        int guidStart = id.LastIndexOf("#{", StringComparison.Ordinal);
        if (guidStart >= 0)
        {
            id = id[..guidStart];
        }
        return id.Replace('#', '\\');
    }

    /// <summary>Finds the connection from the device's own ID and its parents' IDs (nearest first).</summary>
    public static ConnectionType ConnectionFromAncestors(IEnumerable<string> ancestorIds)
    {
        var ids = ancestorIds.ToList();
        if (ids.Any(id => id.StartsWith("BTHENUM", StringComparison.OrdinalIgnoreCase)
                       || id.StartsWith("BTHLE", StringComparison.OrdinalIgnoreCase)
                       || id.Contains("00001124-0000-1000-8000-00805F9B34FB", StringComparison.OrdinalIgnoreCase)
                       || id.Contains("00001812-0000-1000-8000-00805F9B34FB", StringComparison.OrdinalIgnoreCase)))
        {
            return ConnectionType.Bluetooth;
        }
        if (ids.Any(id => id.StartsWith(@"USB\", StringComparison.OrdinalIgnoreCase)))
        {
            return ConnectionType.Usb;
        }
        if (ids.Any(id => id.StartsWith(@"ACPI\", StringComparison.OrdinalIgnoreCase)))
        {
            return ConnectionType.BuiltIn;
        }
        return ConnectionType.Unknown;
    }

    /// <summary>
    /// ID of the physical device a HID channel belongs to, from its parents' IDs (nearest first, self excluded).
    /// For USB devices with several interfaces (MI_00, MI_01…) that is the device above the interfaces,
    /// so a mouse's keyboard channel on another interface still counts as the same mouse.
    /// </summary>
    public static string? PhysicalDeviceKey(IReadOnlyList<string> parentIds)
    {
        if (parentIds.Count == 0)
        {
            return null;
        }
        string parent = parentIds[0];
        bool isUsbInterface = parent.StartsWith(@"USB\", StringComparison.OrdinalIgnoreCase)
                              && parent.Contains("&MI_", StringComparison.OrdinalIgnoreCase);
        return isUsbInterface && parentIds.Count > 1 ? parentIds[1] : parent;
    }

    /// <summary>A touchpad has a digitizer "touch pad" channel next to its mouse channel.</summary>
    public static bool IsTouchpad(IEnumerable<HidCollection> collections) =>
        collections.Any(c => c.UsagePage == UsagePageDigitizer && c.Usage == UsageTouchpad);

    /// <summary>Remote Desktop and similar software-only mice.</summary>
    public static bool IsVirtualMouse(string instanceId) =>
        instanceId.StartsWith(@"ROOT\RDP_MOU", StringComparison.OrdinalIgnoreCase)
        || instanceId.StartsWith(@"TS_INPT", StringComparison.OrdinalIgnoreCase);

    public static string DisplayName(string? productString, string? manufacturer, ushort? vendorId, ushort? productId,
        ConnectionType connection)
    {
        string? product = Clean(productString);
        if (product is not null)
        {
            string? maker = Clean(manufacturer);
            bool makerAlreadyInName = maker is null || product.Contains(maker, StringComparison.OrdinalIgnoreCase);
            return makerAlreadyInName ? product : $"{maker} {product}";
        }
        string kind = connection switch
        {
            ConnectionType.Usb => "USB mouse",
            ConnectionType.Bluetooth => "Bluetooth mouse",
            ConnectionType.BuiltIn => "Built-in mouse",
            _ => "Mouse",
        };
        return vendorId is { } vid && productId is { } pid ? $"{kind} {vid:X4}:{pid:X4}" : kind;
    }

    public static string DescribeUsage(ushort usagePage, ushort usage, int featureReportBytes)
    {
        string meaning = (usagePage, usage) switch
        {
            (UsagePageGenericDesktop, UsageMouse) => "Mouse",
            (UsagePageGenericDesktop, 0x01) => "Pointer",
            (UsagePageGenericDesktop, UsageKeyboard) => "Keyboard keys",
            (UsagePageGenericDesktop, 0x80) => "System keys (sleep, power)",
            (UsagePageConsumer, 0x01) => "Media keys",
            (UsagePageDigitizer, UsageTouchpad) => "Touchpad",
            (UsagePageDigitizer, 0x0E) => "Touchpad settings",
            _ when usagePage >= 0xFF00 => "Maker's own channel",
            _ => "Other",
        };
        return featureReportBytes > 0 && usagePage >= 0xFF00 ? $"{meaning} (has settings reports)" : meaning;
    }

    public static string CollectionLabel(string instanceId)
    {
        Match match = CollectionNumber().Match(instanceId);
        return match.Success ? $"COL{match.Groups[1].Value}" : "";
    }

    private static string? Clean(string? text)
    {
        string? trimmed = text?.Trim('\0', ' ');
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    [GeneratedRegex(@"&COL(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex CollectionNumber();
}
