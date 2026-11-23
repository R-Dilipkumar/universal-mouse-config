using MouseConfig.Core;
using MouseConfig.Core.Settings;

namespace MouseConfig.App;

/// <summary>
/// Holds the app's saved settings. If the file couldn't be read (broken, or from a newer version), the app runs on
/// defaults and never saves over the file, so nothing the user had is lost.
/// </summary>
public sealed class SettingsStore
{
    private readonly AppSettingsFile _file;
    private readonly Log _log;

    public SettingsStore(AppSettingsFile file, Log log)
    {
        _file = file;
        _log = log;
        try
        {
            Current = file.Load();
        }
        catch (Exception error)
        {
            log.Error("Couldn't read settings.json; using defaults and not saving", error);
            LoadProblem = error.Message;
            Current = new AppSettings();
        }
    }

    public AppSettings Current { get; private set; }

    /// <summary>Why the saved settings couldn't be used, or null.</summary>
    public string? LoadProblem { get; }

    /// <summary>Changes and saves the settings. Returns an error message, or null when saved.</summary>
    public string? Update(Func<AppSettings, AppSettings> change)
    {
        Current = change(Current);
        if (LoadProblem is not null)
        {
            return $"Not saved: {LoadProblem}";
        }
        try
        {
            _file.Save(Current);
            return null;
        }
        catch (Exception error)
        {
            _log.Error("Saving settings.json failed", error);
            return $"Couldn't save settings: {error.Message}";
        }
    }
}
