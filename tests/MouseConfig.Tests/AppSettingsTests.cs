using MouseConfig.Core.Settings;

namespace MouseConfig.Tests;

public sealed class AppSettingsTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "MouseConfigTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Settings_file_round_trips_and_missing_file_gives_defaults()
    {
        var file = new AppSettingsFile(Path.Combine(_folder, "settings.json"));
        Assert.Equal(AppTheme.Dark, file.Load().Theme);

        file.Save(new AppSettings { Theme = AppTheme.Light });

        Assert.Equal(AppTheme.Light, file.Load().Theme);
        Assert.Contains("\"Light\"", File.ReadAllText(Path.Combine(_folder, "settings.json"))); // enums saved as words
    }

    [Fact]
    public void File_from_when_remapping_existed_still_loads_and_drops_it_on_save()
    {
        string path = Path.Combine(_folder, "settings.json");
        Directory.CreateDirectory(_folder);
        File.WriteAllText(path, """
            { "SchemaVersion": 1, "Theme": "Light",
              "Remap": { "Enabled": true, "Buttons": { "4": { "Kind": "Key", "VirtualKey": 67 } }, "ReverseScroll": true, "ScrollBoost": 1.5, "StrayScrollFilter": 2 } }
            """);
        var file = new AppSettingsFile(path);

        AppSettings loaded = file.Load();
        file.Save(loaded);

        Assert.Equal(AppTheme.Light, loaded.Theme);
        Assert.DoesNotContain("Remap", File.ReadAllText(path));
    }

    [Fact]
    public void Newer_or_broken_settings_file_is_an_error_and_left_alone()
    {
        string path = Path.Combine(_folder, "settings.json");
        Directory.CreateDirectory(_folder);
        var file = new AppSettingsFile(path);

        File.WriteAllText(path, """{ "SchemaVersion": 2, "Theme": "Light" }""");
        Assert.Contains("newer version", Assert.Throws<InvalidDataException>(file.Load).Message);

        File.WriteAllText(path, "{ not json");
        Assert.Throws<InvalidDataException>(file.Load);
        Assert.Equal("{ not json", File.ReadAllText(path));
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }
}
