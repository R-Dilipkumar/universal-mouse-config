namespace MouseConfig.Core.Devices;

/// <summary>
/// What is known about a mouse maker or chip, by USB vendor ID: how its settings are normally changed,
/// and which tools can do it. Vendor names checked against usb.ids (version 2026.06.26) on 2026-11-09.
/// Research notes: docs/research.md.
/// </summary>
public sealed record MouseFamily(
    ushort VendorId,
    string Maker,
    string HowItWorks,
    string MakerTool,
    string OpenTools,
    bool HasPluginHere);

public static class MouseFamilies
{
    private const string BrandAppPerModel = "Each brand ships its own Windows app for each model; there is no shared standard.";

    public static IReadOnlyList<MouseFamily> All { get; } =
    [
        new(0x046D, "Logitech",
            "Gaming and office mice store DPI, report rate, buttons and lighting through Logitech's HID++ protocol.",
            "Logitech G HUB (gaming), Logi Options+ (office)",
            "Solaar and libratbag (Linux), OpenRGB (lighting), OpenMouse (browser)", false),
        new(0x1532, "Razer",
            "Settings are stored with Razer's own commands.",
            "Razer Synapse (and Synapse Web, in beta)",
            "OpenRazer (Linux), OpenRGB (lighting, also on Windows)", false),
        new(0x1038, "SteelSeries",
            "Settings are stored with SteelSeries' own commands; they differ per model.",
            "SteelSeries GG",
            "rivalcfg (also works on Windows), libratbag, OpenRGB", false),
        new(0x1B1C, "Corsair",
            "Settings are stored with Corsair's own commands.",
            "Corsair iCUE",
            "ckb-next (Linux, macOS), OpenRGB", false),
        new(0x1E7D, "ROCCAT (Turtle Beach)",
            "Settings are stored with ROCCAT's own commands.",
            "ROCCAT Swarm / Turtle Beach Swarm II",
            "libratbag, OpenRGB", false),
        new(0x0951, "HyperX (Kingston)",
            "Settings are stored with HyperX's own commands.",
            "HyperX NGENUITY",
            "OpenRGB (lighting)", false),
        new(0x03F0, "HP / HyperX",
            "Newer HyperX mice use HP's vendor ID; settings use HyperX's own commands.",
            "HyperX NGENUITY / HP OMEN Gaming Hub",
            "OpenRGB (lighting)", false),
        new(0x0B05, "ASUS (ROG / TUF)",
            "Settings are stored with ASUS' own commands.",
            "ASUS Armoury Crate",
            "libratbag (asus driver), OpenRGB", false),
        new(0x045E, "Microsoft",
            "Most Microsoft mice store nothing inside; button changes are done by software on the PC.",
            "Microsoft Mouse and Keyboard Center",
            "None needed", false),
        new(0x258A, "SinoWealth chip (Glorious, Redragon and many budget brands)",
            "A chip maker used by many brands. " + BrandAppPerModel,
            "The app from your mouse's brand",
            "libratbag (sinowealth driver), gloriousctl, mouse_m908 (Redragon)", false),
        new(0x04D9, "Holtek chip (many budget brands)",
            "A chip maker used by many brands. " + BrandAppPerModel,
            "The app from your mouse's brand",
            "Few; some models in OpenRGB", false),
        new(0x1A81, "Holtek chip (many budget brands)",
            "A chip maker used by many brands. " + BrandAppPerModel,
            "The app from your mouse's brand",
            "Few; some models in OpenRGB", false),
        new(0x093A, "Pixart chip (many brands)",
            "Pixart makes mouse sensors and chips used by many brands. " + BrandAppPerModel,
            "The app from your mouse's brand",
            "Few", false),
        new(0x1BCF, "Sunplus chip (Redgear, Philips and others)",
            "Settings are stored with Sunplus' commands on a vendor channel (COL04).",
            "Often none, or a brand app that's hard to find",
            "redgear-a17-linux (Linux)", true),
        new(0x1AF3, "ZOWIE (BenQ)",
            "ZOWIE mice need no software: DPI and report rate are set with buttons on the mouse.",
            "None needed",
            "None needed", false),
        new(0x1EA7, "Sharkoon", BrandAppPerModel, "Sharkoon software for your model", "Few", false),
        new(0x2516, "Cooler Master", "Settings are stored with Cooler Master's own commands.", "Cooler Master MasterPlus+", "OpenRGB (lighting)", false),
        new(0x1462, "MSI", "Settings are stored with MSI's own commands.", "MSI Center", "OpenRGB (lighting)", false),
        new(0x0738, "Mad Catz", BrandAppPerModel, "Mad Catz software for your model", "Few", false),
        new(0x24AE, "Rapoo", "Most Rapoo mice store nothing inside; gaming models use Rapoo's own app.", "Rapoo software for your model", "Few", false),
        new(0x0458, "Genius (KYE Systems)", "Most store nothing inside.", "Genius software for your model", "Few", false),
        new(0x046A, "CHERRY", "Most store nothing inside.", "CHERRY KEYS", "Few", false),
        new(0x413C, "Dell", "Most Dell mice store nothing inside.", "Dell Peripheral Manager / Dell Display and Peripheral Manager", "None needed", false),
        new(0x17EF, "Lenovo", "Most Lenovo mice store nothing inside.", "Lenovo Vantage / Lenovo Legion Gaming Peripherals", "None needed", false),
        new(0x05AC, "Apple", "Apple mice are made for macOS; on Windows they work as basic mice.", "Boot Camp drivers (Magic Mouse scrolling)", "Magic Mouse Utilities (paid, third party)", false),
        new(0x2717, "Xiaomi", "Most store nothing inside.", "None for most models", "None needed", false),
        new(0x25A7, "Areson (many budget wireless mice)", "Budget wireless mice usually store nothing and need no software.", "None", "None needed", false),
        new(0x248A, "Maxxter (budget wireless receivers)", "Budget wireless mice usually store nothing and need no software.", "None", "None needed", false),
        new(0x1A2C, "China Resource Semico (budget mice and receivers)", "Budget mice usually store nothing and need no software.", "None", "None needed", false),
        new(0x0E8F, "GreenAsia (budget devices)", "Budget mice usually store nothing and need no software.", "None", "None needed", false),
        new(0x1915, "Nordic Semiconductor (wireless chip)", "A wireless chip used inside many receivers. " + BrandAppPerModel, "The app from your mouse's brand", "Few", false),
        new(0x0C45, "Microdia (budget devices)", "Budget mice usually store nothing and need no software.", "None", "None needed", false),
    ];

    public static MouseFamily? Find(ushort? vendorId) =>
        vendorId is { } id ? All.FirstOrDefault(f => f.VendorId == id) : null;

    /// <summary>The same guidance for any mouse not in the list, so every mouse gets an answer.</summary>
    public static string UnknownMakerText =>
        "This maker isn't in the app's list yet. Most mice without their own software store nothing inside, so Windows settings " +
        "are all they need. If your mouse came with software, that software is what changes its DPI and lighting.";
}
