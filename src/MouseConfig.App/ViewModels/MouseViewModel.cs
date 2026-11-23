using CommunityToolkit.Mvvm.ComponentModel;
using MouseConfig.Core.Devices;
using MouseConfig.Plugins.Sunplus1BCF;

namespace MouseConfig.App.ViewModels;

public sealed record InfoRow(string Label, string Value);

public sealed record ChannelRow(string Name, string Meaning, string Usage, string Reports);

/// <summary>A card on the Overview that opens another tab.</summary>
public sealed record QuickAction(string Icon, string Title, string Text, string Badge, int TabIndex, bool IsAvailable);

/// <summary>Tab order in the main window. Kept in one place so Overview cards and the window agree.</summary>
public static class Tabs
{
    public const int Overview = 0;
    public const int Test = 1;
    public const int Pointer = 2;
    public const int Scroll = 3;
    public const int MouseMemory = 4;
}

/// <summary>What the screen shows for one mouse. Built once per scan.</summary>
public sealed partial class MouseViewModel : ObservableObject
{
    public MouseViewModel(MouseDevice device)
    {
        Device = device;
        Family = MouseFamilies.Find(device.VendorId);
        bool hasPlugin = SunplusPlugin.Matches(device) && SunplusPlugin.SettingsChannel(device) is not null;

        Chips = new[]
        {
            ConnectionText(device.Connection),
            device.ButtonCount > 0 ? $"{device.ButtonCount} buttons" : null,
            device.HasWheel == true ? "Scroll wheel" : null,
            device.HasHorizontalWheel ? "Tilt wheel" : null,
        }.OfType<string>().ToList();
        Summary = string.Join(" · ", Chips);
        PluginChip = hasPlugin ? "Settings inside the mouse supported" : null;

        Info =
        [
            new("Made by", Family?.Maker ?? device.Manufacturer ?? "Not reported by the mouse"),
            new("USB ID", device.UsbId ?? "Unknown"),
            new("Connection", ConnectionText(device.Connection)),
            new("Buttons", device.ButtonCount > 0 ? $"{device.ButtonCount} (Windows reports at most 5)" : "Unknown"),
            new("Scroll wheel", YesNo(device.HasWheel)),
            new("Tilt wheel", device.HasHorizontalWheel ? "Yes" : "No"),
        ];
        ReportRateText = device.SampleRateHz > 0
            ? $"{device.SampleRateHz} Hz (reported by Windows)"
            : "Not reported by Windows. Measure it on the Test buttons tab.";

        Channels = device.Collections
            .Select(c => new ChannelRow(
                c.ShortName.Length > 0 ? c.ShortName : "Main",
                c.Meaning,
                $"0x{c.UsagePage:X4} / 0x{c.Usage:X4}",
                ReportSizes(c)))
            .ToList();

        QuickActions =
        [
            hasPlugin
                ? new("", "Mouse memory", "Buttons, DPI and lighting saved inside the mouse.", "Saved in the mouse", Tabs.MouseMemory, true)
                : new("", "Mouse memory", "Settings stored inside the mouse need a plugin for its chip. See below.", "Not for this mouse yet", Tabs.MouseMemory, false),
            new("", "Test buttons", "Press each button and see it light up. Measures the report rate too.", "Any mouse", Tabs.Test, true),
            new("", "Pointer speed", "Speed, precision, double-click, trails and more.", "Windows setting", Tabs.Pointer, true),
            new("", "Scrolling", "Lines per notch and sideways scrolling.", "Windows setting", Tabs.Scroll, true),
        ];

        HowItWorks = Family is null ? MouseFamilies.UnknownMakerText : Family.HowItWorks;
        MakerTool = Family?.MakerTool ?? "The software that came with your mouse, if any";
        OpenTools = Family?.OpenTools ?? "Unknown";
        HereText = hasPlugin
            ? "This app can change the settings stored inside this mouse (Mouse memory tab), plus everything below."
            : "This app gives this mouse: the button test and Windows pointer and scroll settings. " +
              "DPI and lighting stored inside the mouse need the maker's tool for now.";
    }

    public MouseDevice Device { get; }
    public MouseFamily? Family { get; }
    public ButtonTestViewModel ButtonTest => _buttonTest ??= new ButtonTestViewModel(Device, rate => MeasuredRate = rate);
    private ButtonTestViewModel? _buttonTest;
    public HardwareViewModel Hardware => _hardware ??= new HardwareViewModel(Device, App.Sunplus);
    private HardwareViewModel? _hardware;

    public string Name => Device.Name;
    public string Summary { get; }
    public IReadOnlyList<string> Chips { get; }
    public string? PluginChip { get; }
    public bool HasPluginChip => PluginChip is not null;
    public IReadOnlyList<InfoRow> Info { get; }
    public IReadOnlyList<ChannelRow> Channels { get; }
    public IReadOnlyList<QuickAction> QuickActions { get; }
    public string HowItWorks { get; }
    public string MakerTool { get; }
    public string OpenTools { get; }
    public string HereText { get; }
    public string DeviceId => Device.InstanceId;
    public IReadOnlyList<string> Problems => Device.Problems;
    public bool HasProblems => Device.Problems.Count > 0;

    [ObservableProperty] private string _reportRateText = "";

    /// <summary>Set by the button test when it measures a higher rate.</summary>
    public int MeasuredRate
    {
        set => ReportRateText = $"{value} Hz (measured on the Test buttons tab)";
    }

    public static string ConnectionText(ConnectionType connection) => connection switch
    {
        ConnectionType.Usb => "USB",
        ConnectionType.Bluetooth => "Bluetooth",
        ConnectionType.BuiltIn => "Built-in",
        _ => "Unknown connection",
    };

    private static string YesNo(bool? value) => value switch
    {
        true => "Yes",
        false => "No",
        null => "Unknown",
    };

    private static string ReportSizes(HidCollection c)
    {
        var parts = new List<string>();
        if (c.InputReportBytes > 0) parts.Add($"in {c.InputReportBytes} B");
        if (c.OutputReportBytes > 0) parts.Add($"out {c.OutputReportBytes} B");
        if (c.FeatureReportBytes > 0) parts.Add($"settings {c.FeatureReportBytes} B");
        return parts.Count > 0 ? string.Join(", ", parts) : "none";
    }
}
