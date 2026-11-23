namespace MouseConfig.Core.Settings;

/// <summary>
/// Changes Windows' mouse settings with undo per tab, Windows defaults per tab, and "restore original".
/// Every write is read back; if Windows didn't keep a value, the change fails loudly. Not thread-safe: call it from one place at a time.
/// </summary>
public sealed class MouseSettingsEditor
{
    private readonly IMouseSettingsSystem _system;
    private readonly Dictionary<SettingsGroup, Stack<WindowsMouseSettings>> _history = new()
    {
        [SettingsGroup.Pointer] = new(),
        [SettingsGroup.Scroll] = new(),
    };

    public MouseSettingsEditor(IMouseSettingsSystem system, OriginalSettingsSnapshot original)
    {
        _system = system;
        Original = original;
        Current = system.Read();
    }

    public WindowsMouseSettings Current { get; private set; }
    public OriginalSettingsSnapshot Original { get; }

    public bool CanUndo(SettingsGroup group) => _history[group].Count > 0;

    /// <summary>Applies <paramref name="wanted"/>'s values for one tab. Other tabs' values are left as they are.</summary>
    public void Change(SettingsGroup group, WindowsMouseSettings wanted)
    {
        WindowsMouseSettings before = Current;
        WindowsMouseSettings target = before.WithGroupFrom(wanted, group);
        if (target == before)
        {
            return;
        }
        WriteAndCheck(before, target);
        _history[group].Push(before);
    }

    public void Undo(SettingsGroup group)
    {
        if (_history[group].Count == 0)
        {
            return;
        }
        WindowsMouseSettings previous = _history[group].Peek();
        WriteAndCheck(Current, Current.WithGroupFrom(previous, group));
        _history[group].Pop();
    }

    public void ResetToWindowsDefaults(SettingsGroup group) => Change(group, WindowsMouseSettings.WindowsDefaults);

    public void RestoreOriginal()
    {
        Change(SettingsGroup.Pointer, Original.Settings);
        Change(SettingsGroup.Scroll, Original.Settings);
    }

    /// <summary>Picks up changes made outside the app (e.g. in Windows Settings). Returns true if anything changed.</summary>
    public bool Reload()
    {
        WindowsMouseSettings now = _system.Read();
        bool changed = now != Current;
        Current = now;
        return changed;
    }

    private void WriteAndCheck(WindowsMouseSettings from, WindowsMouseSettings to)
    {
        try
        {
            _system.Write(from, to);
        }
        finally
        {
            Current = _system.Read();
        }
        List<string> notKept = to.DifferencesFrom(Current).Select(f => f.Name).ToList();
        if (notKept.Count > 0)
        {
            throw new InvalidOperationException($"Windows didn't keep: {string.Join(", ", notKept)}");
        }
    }
}
