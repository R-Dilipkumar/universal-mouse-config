using System.Collections;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using MouseConfig.Core.Settings;

namespace MouseConfig.App;

/// <summary>
/// Switches between the app's dark and light palettes. The palette's colours are put straight into the
/// application's resources, which take priority over the built-in Fluent theme.
/// Fluent bakes the Windows accent colour into its control colours when it loads (grey on a PC with a grey accent),
/// so those control colours are repainted with the app's own accent too (checked boxes, sliders, focus lines…).
/// </summary>
public static class ThemeService
{
    /// <summary>Fluent control colours that come from the Windows accent (checked .NET 10.0.4xx, 2026-11).</summary>
    private static readonly string[] AccentDerivedKeys =
    [
        "AccentButtonBackground", "AccentButtonBackgroundPointerOver", "AccentButtonBackgroundPressed",
        "AccentFillColorDefaultBrush", "AccentFillColorSecondaryBrush", "AccentFillColorSelectedTextBackgroundBrush",
        "AccentFillColorTertiaryBrush", "AccentTextFillColorPrimaryBrush", "AccentTextFillColorSecondaryBrush",
        "AccentTextFillColorTertiaryBrush", "CalendarViewSelectedBackground", "CalendarViewSelectedBorderBrush",
        "CalendarViewTodayBackground", "CheckBoxCheckBackgroundFillChecked", "CheckBoxCheckBackgroundFillCheckedPointerOver",
        "CheckBoxCheckBackgroundFillCheckedPressed", "CheckBoxCheckBackgroundFillIndeterminate",
        "CheckBoxCheckBackgroundFillIndeterminatePointerOver", "CheckBoxCheckBackgroundFillIndeterminatePressed",
        "CheckBoxCheckBackgroundStrokeChecked", "CheckBoxCheckBackgroundStrokeCheckedPointerOver",
        "CheckBoxCheckBackgroundStrokeCheckedPressed", "CheckBoxCheckBackgroundStrokeIndeterminate",
        "CheckBoxCheckBackgroundStrokeIndeterminatePointerOver", "CheckBoxCheckBackgroundStrokeIndeterminatePressed",
        "ComboBoxBorderBrushFocused", "ComboBoxItemPillFillBrush", "DataGridRowSelectedBackgroundThemeBrush",
        "DatePickerBorderBrushFocused", "DatePickerFocusedBorderBrush", "HyperlinkButtonForeground",
        "HyperlinkButtonForegroundPointerOver", "HyperlinkButtonForegroundPressed", "HyperlinkForeground",
        "HyperlinkForegroundPointerOver", "InfoBarInformationalSeverityIconBackground",
        "ListBoxItemBackgroundSelectedPressedThemeBrush", "ListBoxItemSelectedBackgroundPointerOverThemeBrush",
        "ListBoxItemSelectedBackgroundPressedThemeBrush", "ListBoxItemSelectedBackgroundThemeBrush", "ListViewItemPillFillBrush",
        "NavigationViewSelectionIndicatorForeground", "ProgressBarForeground", "ProgressRingForegroundThemeBrush",
        "RadioButtonCheckOuterEllipseCheckedFillPointerOver", "RadioButtonCheckOuterEllipseCheckedFillPressed",
        "RadioButtonCheckOuterEllipseCheckedStrokePressed", "RadioButtonOuterEllipseCheckedFill",
        "RadioButtonOuterEllipseCheckedStroke", "RadioButtonOuterEllipseCheckedStrokePointerOver", "RatingControlSelectedForeground",
        "SliderThumbBackground", "SliderThumbBackgroundPointerOver", "SliderTrackValueFill", "SliderTrackValueFillPointerOver",
        "SliderTrackValueFillPressed", "SystemFillColorAttentionBrush", "TextControlFocusedBorderBrush",
        "TextControlSelectionHighlightColor", "ThumbRateForeground", "ToggleButtonBackgroundChecked",
        "ToggleButtonBackgroundCheckedPointerOver", "ToggleButtonBackgroundCheckedPressed", "ToggleSwitchFillOn",
        "ToggleSwitchFillOnPointerOver", "ToggleSwitchStrokeOn", "ToggleSwitchStrokeOnPointerOver",
        "TreeViewItemSelectionIndicatorForeground",
    ];

    public static AppTheme Chosen { get; private set; } = AppTheme.Dark;

    public static void Apply(AppTheme theme)
    {
        Chosen = theme;
        bool light = theme == AppTheme.Light || (theme == AppTheme.System && WindowsUsesLightTheme());
        Application app = Application.Current;
        app.ThemeMode = light ? ThemeMode.Light : ThemeMode.Dark;
        var palette = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/MouseConfig;component/Themes/{(light ? "Light" : "Dark")}.xaml"),
        };
        foreach (DictionaryEntry entry in palette)
        {
            app.Resources[entry.Key] = entry.Value;
        }
        RepaintAccentControls(app, palette);
    }

    /// <summary>Re-applies "match Windows" when the user flips Windows between light and dark.</summary>
    public static void FollowWindowsChanges()
    {
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (Chosen == AppTheme.System && e.Category == UserPreferenceCategory.General)
            {
                Application.Current.Dispatcher.BeginInvoke(() => Apply(AppTheme.System));
            }
        };
    }

    /// <summary>
    /// For each accent-derived Fluent colour: find which Windows accent shade it was made from, and put the
    /// app's matching shade in its place (same transparency). Unknown shades get the app's main accent.
    /// </summary>
    private static void RepaintAccentControls(Application app, ResourceDictionary palette)
    {
        var shadeMap = new Dictionary<Color, Color>
        {
            [Opaque(SystemColors.AccentColor)] = (Color)palette["SystemAccentColor"],
            [Opaque(SystemColors.AccentColorLight1)] = (Color)palette["SystemAccentColorLight1"],
            [Opaque(SystemColors.AccentColorLight2)] = (Color)palette["SystemAccentColorLight2"],
            [Opaque(SystemColors.AccentColorLight3)] = (Color)palette["SystemAccentColorLight3"],
            [Opaque(SystemColors.AccentColorDark1)] = (Color)palette["SystemAccentColorDark1"],
            [Opaque(SystemColors.AccentColorDark2)] = (Color)palette["SystemAccentColorDark2"],
            [Opaque(SystemColors.AccentColorDark3)] = (Color)palette["SystemAccentColorDark3"],
        };
        var fluent = new Dictionary<string, SolidColorBrush>();
        CollectBrushes(app.Resources.MergedDictionaries, fluent);
        foreach (string key in AccentDerivedKeys)
        {
            if (!fluent.TryGetValue(key, out SolidColorBrush? original))
            {
                continue;
            }
            Color mine = shadeMap.TryGetValue(Opaque(original.Color), out Color mapped) ? mapped : (Color)palette["SystemAccentColor"];
            var brush = new SolidColorBrush(Color.FromArgb(original.Color.A, mine.R, mine.G, mine.B));
            brush.Freeze();
            app.Resources[key] = brush;
        }
    }

    private static void CollectBrushes(IEnumerable<ResourceDictionary> dictionaries, Dictionary<string, SolidColorBrush> found)
    {
        foreach (ResourceDictionary dictionary in dictionaries)
        {
            foreach (DictionaryEntry entry in dictionary)
            {
                if (entry.Key is string key && entry.Value is SolidColorBrush brush)
                {
                    found.TryAdd(key, brush);
                }
            }
            CollectBrushes(dictionary.MergedDictionaries, found);
        }
    }

    private static Color Opaque(Color color) => Color.FromRgb(color.R, color.G, color.B);

    private static bool WindowsUsesLightTheme()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int value && value != 0;
    }
}
