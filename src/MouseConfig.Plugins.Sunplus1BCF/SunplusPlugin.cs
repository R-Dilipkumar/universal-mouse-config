using MouseConfig.Core;
using MouseConfig.Core.Devices;

namespace MouseConfig.Plugins.Sunplus1BCF;

public sealed record HardwareReadResult(SunplusConfig Config, string IdentityHex, bool IsTestedIdentity,
    string? NewBackupFile, string? ExistingBackupFile);

/// <summary>Hardware settings for Sunplus 1BCF:08A0 mice (Redgear A-15/A-17 and others with the same chip).</summary>
public sealed class SunplusPlugin
{
    public const ushort VendorId = 0x1BCF;
    public const ushort ProductId = 0x08A0;
    private const ushort SettingsUsagePage = 0xFF00;
    private const int SettingsFeatureBytes = 8;

    // One hardware session at a time, across the whole app.
    private static readonly SemaphoreSlim OneSession = new(1, 1);

    private readonly AppPaths _paths;
    private readonly Log _log;
    private readonly Func<string, IFeatureChannel> _openChannel;

    public SunplusPlugin(AppPaths paths, Log log, Func<string, IFeatureChannel>? openChannel = null)
    {
        _paths = paths;
        _log = log;
        _openChannel = openChannel ?? (path => new HidFeatureChannel(path));
    }

    public static bool Matches(MouseDevice mouse) => mouse.VendorId == VendorId && mouse.ProductId == ProductId;

    /// <summary>The settings channel (COL04: vendor page 0xFF00, 8-byte feature report), or null if this mouse doesn't have it.</summary>
    public static HidCollection? SettingsChannel(MouseDevice mouse) => mouse.Collections.FirstOrDefault(c =>
        c.UsagePage == SettingsUsagePage && c.FeatureReportBytes == SettingsFeatureBytes && c.DevicePath.Length > 0);

    /// <summary>Reads the settings block (no writes) and saves a backup the first time this kind of mouse is read.</summary>
    public async Task<HardwareReadResult> ReadAsync(MouseDevice mouse)
    {
        HidCollection channel = SettingsChannel(mouse)
            ?? throw new InvalidOperationException("This mouse has no settings channel (COL04).");
        await OneSession.WaitAsync();
        try
        {
            return await Task.Run(() =>
            {
                IFeatureChannel feature = _openChannel(channel.DevicePath);
                try
                {
                    var session = new SunplusSession(feature);
                    SunplusConfig config = session.ReadConfig();
                    string identity = Convert.ToHexString(session.Identity ?? []);
                    _log.Info($"Sunplus read OK, identity {identity}, {SunplusConfig.Size} bytes");
                    (string? created, string? existing) = SaveFirstBackup(mouse, config);
                    return new HardwareReadResult(config, identity, session.IsTestedIdentity, created, existing);
                }
                finally
                {
                    (feature as IDisposable)?.Dispose();
                }
            });
        }
        finally
        {
            OneSession.Release();
        }
    }

    /// <summary>
    /// Writes <paramref name="wanted"/> to the mouse, then reads the whole block back in a fresh session.
    /// Any difference from what was asked is a loud error. Returns what the mouse holds now.
    /// </summary>
    public async Task<SunplusConfig> WriteAsync(MouseDevice mouse, SunplusConfig expectedCurrent, SunplusConfig wanted,
        bool userApprovedUntestedModel)
    {
        HidCollection channel = SettingsChannel(mouse)
            ?? throw new InvalidOperationException("This mouse has no settings channel (COL04).");
        await OneSession.WaitAsync();
        try
        {
            return await Task.Run(() =>
            {
                _log.Info($"Sunplus write: changing bytes {string.Join(",", wanted.DifferencesFrom(expectedCurrent))}");
                WithChannel(channel, feature =>
                {
                    new SunplusSession(feature).WriteConfig(expectedCurrent, wanted, userApprovedUntestedModel);
                    return true;
                });
                Thread.Sleep(100);
                SunplusConfig after = WithChannel(channel, feature => new SunplusSession(feature).ReadConfig());
                IReadOnlyList<int> wrong = after.DifferencesFrom(wanted);
                if (wrong.Count > 0)
                {
                    _log.Error($"Sunplus write: read-back differs at bytes {string.Join(",", wrong)}{Environment.NewLine}{after.HexDump()}");
                    throw new SunplusProtocolException(
                        $"The mouse doesn't hold what was written (bytes {string.Join(", ", wrong)} differ). Your backup is safe; try Restore backup.");
                }
                _log.Info("Sunplus write: read-back matches");
                return after;
            });
        }
        finally
        {
            OneSession.Release();
        }
    }

    /// <summary>The first backup's block, or null if there is none.</summary>
    public SunplusConfig? LoadBackup(MouseDevice mouse)
    {
        string? path = FindBackup(mouse);
        return path is null ? null : new SunplusConfig(File.ReadAllBytes(path));
    }

    private T WithChannel<T>(HidCollection channel, Func<IFeatureChannel, T> work)
    {
        IFeatureChannel feature = _openChannel(channel.DevicePath);
        try
        {
            return work(feature);
        }
        finally
        {
            (feature as IDisposable)?.Dispose();
        }
    }

    private string? FindBackup(MouseDevice mouse) => Directory.Exists(_paths.BackupsFolder)
        ? Directory.GetFiles(_paths.BackupsFolder, $"{mouse.VendorId:X4}-{mouse.ProductId:X4}-*.bin").Order().FirstOrDefault()
        : null;

    /// <summary>Writes a backup only if none exists yet for this USB ID. Never overwrites one.</summary>
    private (string? Created, string? Existing) SaveFirstBackup(MouseDevice mouse, SunplusConfig config)
    {
        string? existing = FindBackup(mouse);
        if (existing is not null)
        {
            return (null, existing);
        }
        Directory.CreateDirectory(_paths.BackupsFolder);
        string path = Path.Combine(_paths.BackupsFolder, $"{mouse.VendorId:X4}-{mouse.ProductId:X4}-{DateTime.Now:yyyy-MM-dd-HHmm}.bin");
        using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
        {
            file.Write(config.Data);
        }
        _log.Info($"Saved first backup of the mouse settings: {path}");
        return (path, null);
    }
}
