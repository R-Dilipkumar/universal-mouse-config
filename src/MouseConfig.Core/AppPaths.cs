namespace MouseConfig.Core;

/// <summary>Where the app keeps its files: %AppData%\MouseConfig.</summary>
public sealed class AppPaths
{
    public AppPaths(string rootFolder)
    {
        Root = rootFolder;
    }

    public static AppPaths Default { get; } = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MouseConfig"));

    public string Root { get; }
    public string LogsFolder => Path.Combine(Root, "logs");
    public string BackupsFolder => Path.Combine(Root, "backups");
    public string SettingsFile => Path.Combine(Root, "settings.json");
    public string OriginalWindowsSettingsFile => Path.Combine(Root, "original-windows-settings.json");
}
