using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MouseConfig.Core;
using MouseConfig.Core.Settings;

namespace MouseConfig.App.ViewModels;

/// <summary>
/// The Pointer and Scroll tabs. Changes apply right away (sliders wait until you stop dragging),
/// run off the UI thread one at a time, and are read back from Windows.
/// </summary>
public sealed partial class MouseSettingsViewModel : ObservableObject
{
    private static readonly TimeSpan SliderQuietTime = TimeSpan.FromMilliseconds(300);

    private readonly MouseSettingsEditor? _editor;
    private readonly Log _log;
    private readonly Action<string> _setStatus;
    private readonly SemaphoreSlim _oneAtATime = new(1, 1);
    private readonly Dictionary<SettingsGroup, DispatcherTimer> _pending = [];
    private bool _loading;

    public MouseSettingsViewModel(Func<MouseSettingsEditor> createEditor, Log log, Action<string> setStatus)
    {
        _log = log;
        _setStatus = setStatus;
        foreach (SettingsGroup group in Enum.GetValues<SettingsGroup>())
        {
            var timer = new DispatcherTimer { Interval = SliderQuietTime };
            timer.Tick += async (_, _) =>
            {
                timer.Stop();
                await ApplyAsync(group);
            };
            _pending[group] = timer;
        }
        try
        {
            _editor = createEditor();
            OriginalText = $"Your original settings were saved on {_editor.Original.TakenAt:yyyy-MM-dd HH:mm}.";
            LoadFromEditor();
        }
        catch (Exception error)
        {
            log.Error("Couldn't read or save the original Windows mouse settings", error);
            CanEdit = false;
            OriginalText = $"Editing is off: couldn't read or save your original settings ({error.Message}).";
        }
    }

    public bool CanEdit { get; } = true;
    public string OriginalText { get; }

    // ---------- Pointer ----------
    [ObservableProperty] private int _pointerSpeed;
    [ObservableProperty] private bool _enhancePrecision;
    [ObservableProperty] private bool _swapButtons;
    [ObservableProperty] private int _doubleClickMs;
    [ObservableProperty] private bool _clickLock;
    [ObservableProperty] private int _clickLockMs;
    [ObservableProperty] private bool _hideWhileTyping;
    [ObservableProperty] private bool _trailsOn;
    [ObservableProperty] private int _trailLength = 3;
    [ObservableProperty] private bool _showLocationWithCtrl;
    [ObservableProperty] private bool _snapToDefaultButton;

    // ---------- Scroll ----------
    [ObservableProperty] private bool _oneScreenAtATime;
    [ObservableProperty] private int _wheelLines;
    [ObservableProperty] private int _wheelChars;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UndoPointerCommand))]
    private bool _canUndoPointer;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UndoScrollCommand))]
    private bool _canUndoScroll;

    partial void OnPointerSpeedChanged(int value) => Queue(SettingsGroup.Pointer, slider: true);
    partial void OnEnhancePrecisionChanged(bool value) => Queue(SettingsGroup.Pointer);
    partial void OnSwapButtonsChanged(bool value) => Queue(SettingsGroup.Pointer);
    partial void OnDoubleClickMsChanged(int value) => Queue(SettingsGroup.Pointer, slider: true);
    partial void OnClickLockChanged(bool value) => Queue(SettingsGroup.Pointer);
    partial void OnClickLockMsChanged(int value) => Queue(SettingsGroup.Pointer, slider: true);
    partial void OnHideWhileTypingChanged(bool value) => Queue(SettingsGroup.Pointer);
    partial void OnTrailsOnChanged(bool value) => Queue(SettingsGroup.Pointer);
    partial void OnTrailLengthChanged(int value) => Queue(SettingsGroup.Pointer, slider: true);
    partial void OnShowLocationWithCtrlChanged(bool value) => Queue(SettingsGroup.Pointer);
    partial void OnSnapToDefaultButtonChanged(bool value) => Queue(SettingsGroup.Pointer);
    partial void OnOneScreenAtATimeChanged(bool value) => Queue(SettingsGroup.Scroll);
    partial void OnWheelLinesChanged(int value) => Queue(SettingsGroup.Scroll, slider: true);
    partial void OnWheelCharsChanged(int value) => Queue(SettingsGroup.Scroll, slider: true);

    [RelayCommand(CanExecute = nameof(CanUndoPointer))]
    private Task UndoPointerAsync() => RunAsync("Undone", e => e.Undo(SettingsGroup.Pointer));

    [RelayCommand(CanExecute = nameof(CanUndoScroll))]
    private Task UndoScrollAsync() => RunAsync("Undone", e => e.Undo(SettingsGroup.Scroll));

    [RelayCommand]
    private Task PointerDefaultsAsync() => RunAsync("Pointer settings set to Windows defaults",
        e => e.ResetToWindowsDefaults(SettingsGroup.Pointer));

    [RelayCommand]
    private Task ScrollDefaultsAsync() => RunAsync("Scroll settings set to Windows defaults",
        e => e.ResetToWindowsDefaults(SettingsGroup.Scroll));

    [RelayCommand]
    private Task RestoreOriginalAsync() => RunAsync("Your original mouse settings are back", e => e.RestoreOriginal());

    /// <summary>Call when the window comes back to the front, to show changes made in Windows Settings meanwhile.</summary>
    public async Task ReloadAsync()
    {
        if (_editor is null || _pending.Values.Any(t => t.IsEnabled))
        {
            return;
        }
        await _oneAtATime.WaitAsync();
        try
        {
            if (await Task.Run(_editor.Reload))
            {
                LoadFromEditor();
                _setStatus("Mouse settings were changed outside this app; showing the new values.");
            }
        }
        catch (Exception error)
        {
            _log.Error("Couldn't re-read Windows mouse settings", error);
            _setStatus($"Couldn't re-read Windows mouse settings: {error.Message}");
        }
        finally
        {
            _oneAtATime.Release();
        }
    }

    private void Queue(SettingsGroup group, bool slider = false)
    {
        if (_loading || _editor is null)
        {
            return;
        }
        DispatcherTimer timer = _pending[group];
        timer.Stop();
        if (slider)
        {
            timer.Start();
        }
        else
        {
            _ = ApplyAsync(group);
        }
    }

    private Task ApplyAsync(SettingsGroup group) => RunAsync(editor =>
    {
        WindowsMouseSettings before = editor.Current;
        WindowsMouseSettings wanted = BuildWanted(before);
        List<string> names = wanted.DifferencesFrom(before).Where(f => f.Group == group).Select(f => f.Name).ToList();
        return names.Count == 0 ? null : ($"Saved: {string.Join(", ", names)}", () => editor.Change(group, wanted));
    });

    private Task RunAsync(string doneMessage, Action<MouseSettingsEditor> action) =>
        RunAsync(editor => (doneMessage, () => action(editor)));

    /// <summary>
    /// <paramref name="prepare"/> runs on the UI thread while holding the lock (so it can read the editor and the
    /// screen safely); the work it returns runs on a background thread, because Windows tells every open app
    /// about each change and a stuck app could otherwise freeze this window.
    /// </summary>
    private async Task RunAsync(Func<MouseSettingsEditor, (string Message, Action Work)?> prepare)
    {
        if (_editor is null)
        {
            return;
        }
        await _oneAtATime.WaitAsync();
        string what = "change";
        try
        {
            if (prepare(_editor) is not { } job)
            {
                return;
            }
            what = job.Message;
            await Task.Run(job.Work);
            _setStatus(job.Message);
        }
        catch (Exception error)
        {
            _log.Error($"Changing mouse settings failed ({what})", error);
            _setStatus($"Couldn't change the setting: {error.Message}");
        }
        finally
        {
            LoadFromEditor(); // always show what Windows really has now
            _oneAtATime.Release();
        }
    }

    private WindowsMouseSettings BuildWanted(WindowsMouseSettings current)
    {
        WindowsMouseSettings wanted = current with
        {
            PointerSpeed = PointerSpeed,
            SwapButtons = SwapButtons,
            DoubleClickMs = DoubleClickMs,
            ClickLock = ClickLock,
            ClickLockMs = ClickLockMs,
            HideWhileTyping = HideWhileTyping,
            PointerTrails = TrailsOn ? TrailLength : 0,
            ShowLocationWithCtrl = ShowLocationWithCtrl,
            SnapToDefaultButton = SnapToDefaultButton,
            WheelLines = OneScreenAtATime ? WindowsMouseSettings.OneScreenAtATime : WheelLines,
            WheelChars = WheelChars,
        };
        return EnhancePrecision == current.EnhancePrecision ? wanted : wanted.WithEnhancePrecision(EnhancePrecision);
    }

    private void LoadFromEditor()
    {
        if (_editor is null)
        {
            return;
        }
        WindowsMouseSettings s = _editor.Current;
        // A tab whose slider change is still waiting keeps what's on screen, so that change isn't lost.
        bool loadPointer = !_pending[SettingsGroup.Pointer].IsEnabled;
        bool loadScroll = !_pending[SettingsGroup.Scroll].IsEnabled;
        _loading = true;
        try
        {
            CanUndoPointer = _editor.CanUndo(SettingsGroup.Pointer);
            CanUndoScroll = _editor.CanUndo(SettingsGroup.Scroll);
            if (loadScroll)
            {
                OneScreenAtATime = s.WheelLines == WindowsMouseSettings.OneScreenAtATime;
                if (!OneScreenAtATime)
                {
                    WheelLines = s.WheelLines;
                }
                WheelChars = s.WheelChars;
            }
            if (!loadPointer)
            {
                return;
            }
            PointerSpeed = s.PointerSpeed;
            EnhancePrecision = s.EnhancePrecision;
            SwapButtons = s.SwapButtons;
            DoubleClickMs = s.DoubleClickMs;
            ClickLock = s.ClickLock;
            ClickLockMs = s.ClickLockMs;
            HideWhileTyping = s.HideWhileTyping;
            TrailsOn = s.PointerTrails > 1;
            if (s.PointerTrails > 1)
            {
                TrailLength = s.PointerTrails;
            }
            ShowLocationWithCtrl = s.ShowLocationWithCtrl;
            SnapToDefaultButton = s.SnapToDefaultButton;
        }
        finally
        {
            _loading = false;
        }
    }
}
