using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MouseConfig.Plugins.Sunplus1BCF;

namespace MouseConfig.App.ViewModels;

public sealed record ActionChoice(string Name, byte[]? Code)
{
    /// <summary>The "Keyboard key…" entry: its code comes from the key picker.</summary>
    public bool IsKeyChoice => Code is null;

    public override string ToString() => Name;
}

/// <summary>One button slot in the Hardware tab: pick an action, or any keyboard key with Ctrl/Shift/Alt/Win.</summary>
public sealed partial class SlotEditorViewModel : ObservableObject
{
    private static readonly ActionChoice KeyChoice = new("Keyboard key…", null);

    private readonly Action _changed;
    private readonly Action<SlotEditorViewModel> _startedCapture;
    private bool _settingSeveral;

    public SlotEditorViewModel(ButtonSlot slot, Action changed, Action<SlotEditorViewModel> startedCapture)
    {
        _changed = changed;
        _startedCapture = startedCapture;
        Number = slot.Number;
        CurrentCode = slot.Code;
        NowText = $"now: {slot.ActionName}";

        Choices = [.. SunplusActions.All.Where(a => a.Name != "Key mode").Select(a => new ActionChoice(a.Name, a.Code)), KeyChoice];
        if (SunplusActions.IsKey(slot.Code) && HidKeys.Name(slot.Code[2]) is not null)
        {
            // Backing fields, not properties: no change events while the editor is still being built.
            _ctrl = (slot.Code[1] & HidKeys.ModCtrl) != 0;
            _shift = (slot.Code[1] & HidKeys.ModShift) != 0;
            _alt = (slot.Code[1] & HidKeys.ModAlt) != 0;
            _win = (slot.Code[1] & HidKeys.ModWin) != 0;
            _keyUsage = slot.Code[2];
            _selectedChoice = Choices.FirstOrDefault(c => c.Code is not null && c.Code.AsSpan().SequenceEqual(slot.Code)) ?? KeyChoice;
        }
        else
        {
            ActionChoice? known = Choices.FirstOrDefault(c => c.Code is not null && c.Code.AsSpan().SequenceEqual(slot.Code));
            if (known is null)
            {
                known = new ActionChoice($"{slot.ActionName} (keep as is)", slot.Code);
                Choices.Insert(0, known);
            }
            _selectedChoice = known;
        }
        UpdateKeyText();
    }

    public int Number { get; }
    public string Title => $"Slot {Number}";
    public string NowText { get; }
    public byte[] CurrentCode { get; }
    public List<ActionChoice> Choices { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsKeyMode))]
    private ActionChoice _selectedChoice;

    [ObservableProperty] private bool _ctrl;
    [ObservableProperty] private bool _shift;
    [ObservableProperty] private bool _alt;
    [ObservableProperty] private bool _win;
    [ObservableProperty] private byte? _keyUsage;
    [ObservableProperty] private bool _isCapturing;

    /// <summary>Briefly true after the user pressed the physical button that this slot controls.</summary>
    [ObservableProperty] private bool _isFound;
    [ObservableProperty] private string _keyText = "";

    public bool IsKeyMode => SelectedChoice.IsKeyChoice;

    /// <summary>What this slot should hold, or null while "Keyboard key…" is chosen but no key picked yet.</summary>
    public byte[]? WantedCode => SelectedChoice.IsKeyChoice
        ? KeyUsage is { } usage ? SunplusActions.KeyCode(Modifiers, usage) : null
        : SelectedChoice.Code;

    public bool IsChanged => WantedCode is { } code && !code.AsSpan().SequenceEqual(CurrentCode);
    public string WantedName => WantedCode is { } code ? SunplusActions.NameOf(code) : "(no key picked)";

    private byte Modifiers => (byte)((Ctrl ? HidKeys.ModCtrl : 0) | (Shift ? HidKeys.ModShift : 0)
                                     | (Alt ? HidKeys.ModAlt : 0) | (Win ? HidKeys.ModWin : 0));

    /// <summary>"Record": the next key press or mouse click (mouse or touchpad) becomes this slot's action.</summary>
    [RelayCommand]
    private void StartCapture()
    {
        _startedCapture(this);
        IsCapturing = true;
        KeyText = "Press any key or click a mouse button…";
    }

    public void CancelCapture()
    {
        if (IsCapturing)
        {
            IsCapturing = false;
            UpdateKeyText();
        }
    }

    /// <summary>Called by the view with the pressed key. Modifiers held during the press become the tick boxes.</summary>
    public void Capture(int virtualKey, bool ctrl, bool shift, bool alt, bool win)
    {
        IsCapturing = false;
        if (HidKeys.UsageFromVirtualKey(virtualKey) is not { } usage)
        {
            UpdateKeyText();
            KeyText = "That key can't be stored in the mouse. Try another.";
            return;
        }
        _settingSeveral = true; // the parts below would each report a change; report once at the end
        try
        {
            (Ctrl, Shift, Alt, Win) = (ctrl, shift, alt, win);
            KeyUsage = usage;
            SelectedChoice = KeyChoice;
        }
        finally
        {
            _settingSeveral = false;
        }
        Changed();
    }

    /// <summary>Called by the view with the clicked mouse button: 1 left, 2 right, 3 middle, 4 back, 5 forward.</summary>
    public void CaptureMouseButton(int button)
    {
        IsCapturing = false;
        byte[] code = button switch
        {
            1 => [0x01, 0x01, 0x00],
            2 => [0x01, 0x02, 0x00],
            3 => [0x01, 0x04, 0x00],
            4 => [0x01, 0x08, 0x00],
            5 => [0x01, 0x10, 0x00],
            _ => throw new ArgumentOutOfRangeException(nameof(button)),
        };
        SelectedChoice = Choices.First(c => c.Code is not null && c.Code.AsSpan().SequenceEqual(code));
        UpdateKeyText();
    }

    partial void OnSelectedChoiceChanged(ActionChoice value) => Changed();
    partial void OnCtrlChanged(bool value) => Changed();
    partial void OnShiftChanged(bool value) => Changed();
    partial void OnAltChanged(bool value) => Changed();
    partial void OnWinChanged(bool value) => Changed();
    partial void OnKeyUsageChanged(byte? value) => Changed();

    private void Changed()
    {
        if (_settingSeveral)
        {
            return;
        }
        UpdateKeyText();
        OnPropertyChanged(nameof(IsChanged));
        _changed();
    }

    private void UpdateKeyText()
    {
        if (IsCapturing)
        {
            return;
        }
        if (!IsKeyMode)
        {
            KeyText = "";
            return;
        }
        KeyText = KeyUsage is { } usage
            ? $"{HidKeys.ModifierText(Modifiers)}{HidKeys.Name(usage)}"
            : "No key yet: click Record, then press one.";
    }
}
