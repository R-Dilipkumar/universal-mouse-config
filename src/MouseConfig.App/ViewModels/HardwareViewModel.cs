using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MouseConfig.Core.Devices;
using MouseConfig.Core.Input;
using MouseConfig.Plugins.Sunplus1BCF;

namespace MouseConfig.App.ViewModels;

public sealed record DpiChoice(int Step, string Text);

public sealed record LightingChoice(int Mode, string Name);

/// <summary>
/// The Mouse memory tab: buttons, DPI and lighting stored inside the mouse. Edits collect in a draft; nothing is
/// written until Save and a Yes. Pressing a button on the mouse highlights its row.
/// </summary>
public sealed partial class HardwareViewModel : ObservableObject, IInputConsumer
{
    private static readonly TimeSpan FoundHighlightTime = TimeSpan.FromSeconds(1.5);

    private readonly MouseDevice _mouse;
    private readonly SunplusPlugin _plugin;
    private readonly DispatcherTimer _foundTimer;
    private SunplusConfig? _current;
    private SunplusConfig? _draft;
    private bool _isTestedIdentity;
    private string _identityHex = "";
    private bool _showing;

    public HardwareViewModel(MouseDevice mouse, SunplusPlugin plugin)
    {
        _mouse = mouse;
        _plugin = plugin;
        _foundTimer = new DispatcherTimer { Interval = FoundHighlightTime };
        _foundTimer.Tick += (_, _) =>
        {
            _foundTimer.Stop();
            foreach (SlotEditorViewModel slot in Slots)
            {
                slot.IsFound = false;
            }
        };
        if (!SunplusPlugin.Matches(mouse))
        {
            UnsupportedReason = "This mouse has no plugin here yet. Every maker stores DPI and lighting with its own commands, " +
                                "and this app never sends guessed commands to a mouse. Use the maker's tool for those.";
        }
        else if (SunplusPlugin.SettingsChannel(mouse) is null)
        {
            UnsupportedReason = "This mouse has the right USB ID but no settings channel (COL04), so its settings can't be read.";
        }
    }

    public string MouseName => _mouse.Name;
    public string? UnsupportedReason { get; }
    public bool IsSupported => UnsupportedReason is null;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ReadCommand), nameof(SaveCommand), nameof(DiscardCommand), nameof(RestoreBackupCommand))]
    private bool _isBusy;

    [ObservableProperty] private bool _hasRead;
    [ObservableProperty] private string _state = "Reading only asks the mouse for its settings; it changes nothing.";
    [ObservableProperty] private bool _stateIsError;
    [ObservableProperty] private string _identity = "";
    [ObservableProperty] private string _backupText = "";
    [ObservableProperty] private string _hexDump = "";
    [ObservableProperty] private string _listenState = "";
    [ObservableProperty] private string _foundText = "Tip: press a button on your mouse to find its row.";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand), nameof(RestoreBackupCommand))]
    private bool _canBeChanged;

    [ObservableProperty] private IReadOnlyList<ConfigCheck> _checks = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand), nameof(DiscardCommand))]
    private int _changeCount;

    [ObservableProperty] private string _changeText = "No changes";
    [ObservableProperty] private string _draftProblem = "";

    public ObservableCollection<SlotEditorViewModel> Slots { get; } = [];
    public ObservableCollection<DpiChoice> DpiChoices { get; } = [];
    public IReadOnlyList<LightingChoice> LightingChoices { get; } =
        SunplusConfig.LightingModeChoices.Select(c => new LightingChoice(c.Mode, c.Name)).ToList();

    [ObservableProperty] private DpiChoice? _selectedDpi;
    [ObservableProperty] private LightingChoice? _selectedLighting;
    [ObservableProperty] private int _lightingSpeed = 4;

    /// <summary>Off has no speed.</summary>
    public bool SpeedApplies => SelectedLighting?.Mode != SunplusConfig.LightingModeOff;

    /// <summary>The slot waiting for its next key press or click, if any.</summary>
    public SlotEditorViewModel? CapturingSlot => Slots.FirstOrDefault(s => s.IsCapturing);

    partial void OnSelectedDpiChanged(DpiChoice? value) => UpdateDraft();
    partial void OnLightingSpeedChanged(int value) => UpdateDraft();

    partial void OnSelectedLightingChanged(LightingChoice? value)
    {
        OnPropertyChanged(nameof(SpeedApplies));
        UpdateDraft();
    }

    private bool CanRead => IsSupported && !IsBusy;
    private bool CanSave => !IsBusy && CanBeChanged && ChangeCount > 0 && _draft is not null;
    private bool CanDiscard => !IsBusy && ChangeCount > 0;
    private bool CanRestore => !IsBusy && CanBeChanged && _current is not null;

    /// <summary>Reads the mouse the first time the tab is opened (reading is safe; it changes nothing).</summary>
    public void ReadIfNeeded()
    {
        if (IsSupported && !HasRead && !IsBusy)
        {
            ReadCommand.Execute(null);
        }
    }

    [RelayCommand(CanExecute = nameof(CanRead))]
    private Task ReadAsync() => RunAsync("Reading from the mouse…", async () =>
    {
        HardwareReadResult result = await _plugin.ReadAsync(_mouse);
        _isTestedIdentity = result.IsTestedIdentity;
        _identityHex = result.IdentityHex;
        Show(result.Config);
        BackupText = result.NewBackupFile is { } created
            ? $"Backup saved: {created}"
            : $"Backup from the first read kept: {result.ExistingBackupFile}";
        return $"Read from the mouse at {DateTime.Now:HH:mm}. Nothing was changed.";
    });

    [RelayCommand(CanExecute = nameof(CanSave))]
    private Task SaveAsync() => RunAsync("Saving to the mouse…", async () =>
    {
        SunplusConfig before = _current!;
        SunplusConfig wanted = _draft!;
        string changes = string.Join("\n", DescribeChanges(before, wanted).Select(c => "  " + c));
        bool mainButton = Slots.Any(s => s.IsChanged && s.Number <= 3);
        string clickNote = mainButton
            ? "\n\nYou're changing a main button. If you end up unable to click, use the keyboard: press Tab until Restore backup is selected, then Enter."
            : "";
        return await WriteAsync(before, wanted, $"Save these changes in the mouse?\n\n{changes}{clickNote}", "Saved in the mouse");
    });

    [RelayCommand(CanExecute = nameof(CanDiscard))]
    private void Discard()
    {
        if (_current is not null)
        {
            Show(_current);
            State = "Changes thrown away. The mouse wasn't touched.";
        }
    }

    [RelayCommand(CanExecute = nameof(CanRestore))]
    private Task RestoreBackupAsync() => RunAsync("Restoring the backup…", async () =>
    {
        SunplusConfig before = _current!;
        SunplusConfig backup = _plugin.LoadBackup(_mouse) ?? throw new InvalidOperationException("There's no backup for this mouse yet.");
        SunplusConfig wanted = before.WithWritableRegionsFrom(backup);
        if (wanted.DifferencesFrom(before).Count == 0)
        {
            return "The mouse already matches the backup. Nothing was written.";
        }
        string changes = string.Join("\n", DescribeChanges(before, wanted).Select(c => "  " + c));
        return await WriteAsync(before, wanted, $"Put the backup's settings back into the mouse?\n\n{changes}", "Backup restored");
    });

    // ---------- Press a button to find it ----------
    public bool BelongsToMouse(InputEvent input) => InputDecoding.BelongsTo(input.DeviceInstanceId, _mouse);
    public List<(ushort UsagePage, ushort Usage)> UsagesToListen() => InputDecoding.UsagesToListen(_mouse);

    public void Show(InputEvent input)
    {
        if (!input.IsDown || CapturingSlot is not null || Slots.Count == 0)
        {
            return;
        }
        List<SlotEditorViewModel> found = Slots.Where(s => SlotMatches(s.CurrentCode, input)).ToList();
        if (found.Count == 0)
        {
            return;
        }
        foreach (SlotEditorViewModel slot in Slots)
        {
            slot.IsFound = found.Contains(slot);
        }
        FoundText = $"You pressed the button in {string.Join(" and ", found.Select(s => s.Title))} ({SunplusActions.NameOf(found[0].CurrentCode)}).";
        _foundTimer.Stop();
        _foundTimer.Start();
    }

    /// <summary>Whether a slot's stored action produces this input. Lets the user find a button by pressing it.</summary>
    private static bool SlotMatches(byte[] code, InputEvent input) => input.Kind switch
    {
        InputKind.MouseButton => code[0] == 0x01 && code[1] == (byte)(1 << (input.Code - 1)) && code[2] == 0,
        InputKind.Key => SunplusActions.IsKey(code) && HidKeys.UsageFromVirtualKey(input.Code) == code[2],
        InputKind.MediaKey => code[0] == 0x03 && ((code[1] << 8) | code[2]) == input.Code,
        _ => false,
    };

    // ---------- Draft ----------
    private void UpdateDraft()
    {
        if (_showing || _current is null)
        {
            return;
        }
        try
        {
            SunplusConfig draft = _current;
            foreach (SlotEditorViewModel slot in Slots.Where(s => s.IsChanged))
            {
                draft = draft.WithButton(slot.Number, slot.WantedCode!);
            }
            if (SelectedDpi is { } dpi && dpi.Step != draft.ActiveDpiSlot)
            {
                draft = draft.WithActiveDpi(dpi.Step);
            }
            if (SelectedLighting is { } light && (light.Mode != draft.LightingMode || LightingSpeed != draft.LightingSpeed))
            {
                draft = draft.WithLighting(light.Mode, LightingSpeed);
            }
            _draft = draft;
            DraftProblem = Slots.Any(s => s.WantedCode is null) ? "A button set to \"Keyboard key…\" has no key yet." : "";
            ChangeCount = DraftProblem.Length > 0 ? 0 : DescribeChanges(_current, draft).Count;
        }
        catch (Exception error)
        {
            _draft = null;
            ChangeCount = 0;
            DraftProblem = error.Message.Replace("Not changing the mouse: ", "Can't save yet: ");
        }
        ChangeText = ChangeCount switch
        {
            0 => DraftProblem.Length > 0 ? DraftProblem : "No changes",
            1 => "1 change not saved yet",
            int n => $"{n} changes not saved yet",
        };
    }

    private static List<string> DescribeChanges(SunplusConfig before, SunplusConfig after)
    {
        var changes = before.Buttons.Zip(after.Buttons)
            .Where(pair => !pair.First.Code.AsSpan().SequenceEqual(pair.Second.Code))
            .Select(pair => $"Slot {pair.First.Number}: {pair.First.ActionName}  →  {pair.Second.ActionName}")
            .ToList();
        if (before.ActiveDpiSlot != after.ActiveDpiSlot)
        {
            changes.Add($"Active DPI: {DpiText(before, before.ActiveDpiSlot)}  →  {DpiText(after, after.ActiveDpiSlot)}");
        }
        if (before.LightingMode != after.LightingMode || before.LightingSpeed != after.LightingSpeed)
        {
            changes.Add($"Lighting: {before.LightingModeName} (speed {before.LightingSpeed})  →  {after.LightingModeName} (speed {after.LightingSpeed})");
        }
        return changes;
    }

    private static string DpiText(SunplusConfig config, int step) =>
        step >= 1 && step <= config.DpiValues.Count && config.DpiValues[step - 1] is { } dpi ? $"{dpi} DPI" : $"step {step}";

    private async Task<string> WriteAsync(SunplusConfig before, SunplusConfig wanted, string question, string doneText)
    {
        string risk = _isTestedIdentity
            ? ""
            : $"\n\nNote: your mouse reports identity {_identityHex}, which the reference tool wasn't tested with. " +
              "Every safety check passed and a backup is saved, so the risk is small, but it isn't zero.";
        string text = $"{question}\n\nOnly the button and DPI/lighting areas are written, then everything is read back and compared.{risk}";
        if (!App.Confirm("Change the mouse?", text))
        {
            return "Cancelled. Nothing was written.";
        }
        SunplusConfig after = await _plugin.WriteAsync(_mouse, before, wanted, userApprovedUntestedModel: !_isTestedIdentity);
        Show(after);
        return $"{doneText} at {DateTime.Now:HH:mm} and read back: it matches. Unplug and replug the mouse to check it keeps them.";
    }

    private async Task RunAsync(string busyText, Func<Task<string>> work)
    {
        IsBusy = true;
        StateIsError = false;
        State = busyText;
        try
        {
            State = await work();
        }
        catch (Exception error)
        {
            App.Log.Error($"Mouse memory: {busyText} failed", error);
            StateIsError = true;
            State = $"Failed: {error.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Show(SunplusConfig config)
    {
        _showing = true;
        try
        {
            _current = config;
            _draft = config;
            Identity = _isTestedIdentity
                ? $"{_identityHex} (the tested model)"
                : $"{_identityHex} (not the tested model {Convert.ToHexString(SunplusSession.TestedIdentity)}; all safety checks still apply)";
            Checks = config.Checks();
            CanBeChanged = config.CanBeChanged;
            HexDump = config.HexDump();

            Slots.Clear();
            foreach (ButtonSlot slot in config.Buttons)
            {
                Slots.Add(new SlotEditorViewModel(slot, UpdateDraft,
                    started => { foreach (SlotEditorViewModel other in Slots.Where(s => s != started)) other.CancelCapture(); }));
            }

            DpiChoices.Clear();
            for (int step = 1; step <= config.DpiValues.Count; step++)
            {
                DpiChoices.Add(new DpiChoice(step, config.DpiValues[step - 1] is { } dpi ? $"{dpi} DPI" : $"Step {step}"));
            }
            SelectedDpi = DpiChoices.FirstOrDefault(d => d.Step == config.ActiveDpiSlot);
            SelectedLighting = LightingChoices.FirstOrDefault(l => l.Mode == config.LightingMode);
            LightingSpeed = Math.Clamp(config.LightingSpeed, SunplusConfig.MinLightingSpeed, SunplusConfig.MaxLightingSpeed);
            HasRead = true;
        }
        finally
        {
            _showing = false;
        }
        ChangeCount = 0;
        DraftProblem = "";
        ChangeText = "No changes";
    }
}
