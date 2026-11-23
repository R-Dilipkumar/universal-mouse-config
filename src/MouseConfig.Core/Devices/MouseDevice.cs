namespace MouseConfig.Core.Devices;

public enum ConnectionType
{
    Unknown,
    Usb,
    Bluetooth,
    BuiltIn,
}

/// <summary>One HID channel ("top-level collection") of a device, e.g. mouse, keyboard, media keys, vendor.</summary>
public sealed record HidCollection(
    string InstanceId,
    ushort UsagePage,
    ushort Usage,
    int InputReportBytes,
    int OutputReportBytes,
    int FeatureReportBytes,
    string DevicePath = "")
{
    /// <summary>Short label from the instance ID, e.g. "COL04", or "" for single-channel devices.</summary>
    public string ShortName => DeviceIds.CollectionLabel(InstanceId);

    public string Meaning => DeviceIds.DescribeUsage(UsagePage, Usage, FeatureReportBytes);
}

/// <summary>A pointing device found through Raw Input, with what its HID description says.</summary>
public sealed record MouseDevice
{
    /// <summary>Raw Input device path. Stable while plugged in; used to match devices between scans.</summary>
    public required string RawInputPath { get; init; }
    public required string InstanceId { get; init; }
    public required string Name { get; init; }
    public string? Manufacturer { get; init; }
    public ushort? VendorId { get; init; }
    public ushort? ProductId { get; init; }
    public ConnectionType Connection { get; init; }
    public int ButtonCount { get; init; }
    /// <summary>Reports per second as Windows states it. 0 means the driver didn't say.</summary>
    public int SampleRateHz { get; init; }
    public bool? HasWheel { get; init; }
    public bool HasHorizontalWheel { get; init; }
    public bool IsTouchpad { get; init; }
    /// <summary>All HID channels of the same physical device, including the mouse one.</summary>
    public IReadOnlyList<HidCollection> Collections { get; init; } = [];
    /// <summary>Things that could not be read, shown in the details panel.</summary>
    public IReadOnlyList<string> Problems { get; init; } = [];

    public string? UsbId => VendorId is { } vid && ProductId is { } pid ? $"{vid:X4}:{pid:X4}" : null;
}
