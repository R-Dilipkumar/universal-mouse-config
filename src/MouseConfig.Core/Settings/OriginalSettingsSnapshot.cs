using System.Text.Json;

namespace MouseConfig.Core.Settings;

/// <summary>
/// The user's Windows mouse settings as they were the first time the app ran. Written once, never overwritten,
/// so "Restore my original settings" always goes back to before this app changed anything.
/// </summary>
public sealed record OriginalSettingsSnapshot(int SchemaVersion, DateTime TakenAt, WindowsMouseSettings Settings)
{
    public const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static OriginalSettingsSnapshot LoadOrCreate(string path, Func<WindowsMouseSettings> readCurrent, DateTime now)
    {
        if (File.Exists(path))
        {
            OriginalSettingsSnapshot? loaded = JsonSerializer.Deserialize<OriginalSettingsSnapshot>(File.ReadAllText(path), JsonOptions);
            if (loaded is null || loaded.Settings is null)
            {
                throw new InvalidDataException($"The saved original settings file is empty or broken: {path}");
            }
            if (loaded.SchemaVersion != CurrentSchemaVersion)
            {
                throw new InvalidDataException(
                    $"The saved original settings file has version {loaded.SchemaVersion}; this app reads version {CurrentSchemaVersion}: {path}");
            }
            return loaded;
        }

        var snapshot = new OriginalSettingsSnapshot(CurrentSchemaVersion, now, readCurrent());
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // Write to a temp file first so a crash can't leave a half-written snapshot behind.
        string temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(snapshot, JsonOptions));
        File.Move(temp, path, overwrite: false);
        return snapshot;
    }
}
