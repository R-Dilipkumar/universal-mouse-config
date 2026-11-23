using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MouseConfig.Core;
using MouseConfig.Core.Devices;

namespace MouseConfig.App.ViewModels;

public sealed record ThemeChoice(Core.Settings.AppTheme Theme, string Icon, string Name);

public sealed partial class MainViewModel : ObservableObject
{
    private readonly MouseDetector _detector;
    private readonly Log _log;
    private bool _isScanning;
    private bool _scanAgain;
    private bool _hasScanned;

    public MainViewModel(MouseDetector detector, Log log)
    {
        _detector = detector;
        _log = log;
    }

    public ObservableCollection<MouseViewModel> Mice { get; } = [];

    /// <summary>Windows' mouse settings (Pointer and Scroll tabs). Set once by the window.</summary>
    public MouseSettingsViewModel? Settings { get; set; }


    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private MouseViewModel? _selectedMouse;

    [ObservableProperty]
    private string _statusText = "Looking for mice…";

    /// <summary>True when the status line shows a problem, so it's drawn in the warning colour.</summary>
    [ObservableProperty]
    private bool _statusIsError;

    [ObservableProperty]
    private int _selectedTabIndex;

    public IReadOnlyList<ThemeChoice> ThemeChoices { get; } =
    [
        new(Core.Settings.AppTheme.Dark, "", "Dark"),
        new(Core.Settings.AppTheme.Light, "", "Light"),
        new(Core.Settings.AppTheme.System, "", "Auto"),
    ];

    [ObservableProperty]
    private ThemeChoice? _selectedTheme;

    partial void OnStatusTextChanged(string value) =>
        StatusIsError = value.StartsWith("Couldn't", StringComparison.Ordinal) || value.StartsWith("Failed", StringComparison.Ordinal)
                        || value.StartsWith("Not saved", StringComparison.Ordinal) || value.StartsWith("Won't", StringComparison.Ordinal);

    /// <summary>The theme was already applied at start-up, so picking the same one again does nothing.</summary>
    partial void OnSelectedThemeChanged(ThemeChoice? value)
    {
        if (value is null || value.Theme == ThemeService.Chosen)
        {
            return;
        }
        ThemeService.Apply(value.Theme);
        string? error = App.Settings.Update(s => s with { Theme = value.Theme });
        StatusText = error ?? $"Theme: {value.Name}";
    }

    [RelayCommand]
    private void GoToTab(int tabIndex) => SelectedTabIndex = tabIndex;

    [ObservableProperty]
    private string _hiddenText = "";

    [ObservableProperty]
    private bool _isEmpty;

    public bool HasSelection => SelectedMouse is not null;

    /// <summary>Scans again. A call that arrives during a scan runs one more scan after it, so no change is missed.</summary>
    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (_isScanning)
        {
            _scanAgain = true;
            return;
        }
        _isScanning = true;
        try
        {
            do
            {
                _scanAgain = false;
                await ScanOnceAsync();
            }
            while (_scanAgain);
        }
        finally
        {
            _isScanning = false;
        }
    }

    private async Task ScanOnceAsync()
    {
        DetectionResult result;
        try
        {
            result = await Task.Run(_detector.Detect);
        }
        catch (Exception error)
        {
            _log.Error("Mouse scan failed", error);
            StatusText = $"Couldn't list mice: {error.Message}";
            return;
        }

        var oldPaths = Mice.Select(m => m.Device.RawInputPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var newPaths = result.Mice.Select(m => m.RawInputPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool isFirstScan = !_hasScanned;
        _hasScanned = true;
        List<string> added = result.Mice.Where(m => !oldPaths.Contains(m.RawInputPath)).Select(m => m.Name).ToList();
        List<string> removed = Mice.Where(m => !newPaths.Contains(m.Device.RawInputPath)).Select(m => m.Name).ToList();

        string? selectedPath = SelectedMouse?.Device.RawInputPath;
        Mice.Clear();
        foreach (MouseDevice mouse in result.Mice)
        {
            Mice.Add(new MouseViewModel(mouse));
        }
        SelectedMouse = Mice.FirstOrDefault(m => string.Equals(m.Device.RawInputPath, selectedPath, StringComparison.OrdinalIgnoreCase))
                        ?? Mice.FirstOrDefault();

        IsEmpty = Mice.Count == 0;
        HiddenText = result.HiddenTouchpads.Count switch
        {
            0 => "",
            1 => "1 touchpad hidden",
            int n => $"{n} touchpads hidden",
        };

        string found = Mice.Count == 1 ? "1 mouse found" : $"{Mice.Count} mice found";
        if (isFirstScan || (added.Count == 0 && removed.Count == 0))
        {
            StatusText = found;
        }
        else
        {
            var changes = added.Select(name => $"Plugged in: {name}").Concat(removed.Select(name => $"Removed: {name}"));
            StatusText = $"{string.Join(" · ", changes)} · {found}";
        }
        _log.Info($"Scan: {found}, {result.HiddenTouchpads.Count} touchpad(s) hidden, {result.HiddenVirtualCount} virtual hidden"
                  + string.Concat(result.Mice.Select(m => $"{Environment.NewLine}  {m.Name} [{m.InstanceId}]")));
    }
}
