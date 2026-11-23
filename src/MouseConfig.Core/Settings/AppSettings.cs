using System.Text.Json;
using System.Text.Json.Serialization;

namespace MouseConfig.Core.Settings;

[JsonConverter(typeof(JsonStringEnumConverter<AppTheme>))]
public enum AppTheme
{
    Dark,
    Light,
    /// <summary>Follow Windows' light/dark choice.</summary>
    System,
}

/// <summary>The app's own saved choices, in %AppData%\MouseConfig\settings.json.</summary>
public sealed record AppSettings
{
    /// <summary>Bump this, add a migration in <see cref="AppSettingsFile"/>, and a test, whenever the file's shape changes.</summary>
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public AppTheme Theme { get; init; } = AppTheme.Dark;
    // App-side remapping was removed on 2026-11-24 (the user wants nothing running after setup). Older files
    // still have a "Remap" section; it's ignored on load and dropped on the next save, so no version bump.
}

/// <summary>Loads and saves <see cref="AppSettings"/>. Saving writes a temp file first, so a crash can't leave half a file.</summary>
public sealed class AppSettingsFile
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _path;

    public AppSettingsFile(string path)
    {
        _path = path;
    }

    /// <summary>
    /// The saved settings, or defaults when there is no file yet. A broken file or one from a newer app version
    /// is an error: the caller shows it and must not save over it (that would lose the user's settings).
    /// </summary>
    public AppSettings Load()
    {
        if (!File.Exists(_path))
        {
            return new AppSettings();
        }
        string json = File.ReadAllText(_path);
        int version = ReadVersion(json);
        if (version > AppSettings.CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"settings.json is from a newer version of this app (format {version}; this app reads up to {AppSettings.CurrentSchemaVersion}). It was left untouched.");
        }
        // Version 1 is the first format, so there is nothing to migrate yet.
        return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions)
               ?? throw new InvalidDataException("settings.json is empty.");
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        string temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings with { SchemaVersion = AppSettings.CurrentSchemaVersion }, JsonOptions));
        File.Move(temp, _path, overwrite: true);
    }

    private static int ReadVersion(string json)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty(nameof(AppSettings.SchemaVersion), out JsonElement v) && v.TryGetInt32(out int n) ? n : 0;
        }
        catch (JsonException error)
        {
            throw new InvalidDataException($"settings.json isn't valid ({error.Message}). It was left untouched.", error);
        }
    }
}
