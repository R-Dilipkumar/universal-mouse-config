using MouseConfig.Core;

namespace MouseConfig.Tests;

public sealed class LogTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "MouseConfigTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Error_creates_folder_and_writes_message_and_exception()
    {
        var log = new Log(Path.Combine(_folder, "logs"));

        log.Error("Open failed for COL04", new InvalidOperationException("access denied"));

        string text = await File.ReadAllTextAsync(log.TodayFile, TestContext.Current.CancellationToken);
        Assert.Contains("ERROR Open failed for COL04", text);
        Assert.Contains("access denied", text);
    }

    [Fact]
    public void App_paths_live_under_the_root_folder()
    {
        var paths = new AppPaths(_folder);

        Assert.Equal(Path.Combine(_folder, "logs"), paths.LogsFolder);
        Assert.Equal(Path.Combine(_folder, "backups"), paths.BackupsFolder);
        Assert.Equal(Path.Combine(_folder, "settings.json"), paths.SettingsFile);
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }
}
