namespace MouseConfig.Core;

/// <summary>Writes one text log file per day into the logs folder.</summary>
public sealed class Log
{
    private readonly string _logsFolder;
    private readonly Lock _writeLock = new();

    public Log(string logsFolder)
    {
        _logsFolder = logsFolder;
    }

    public string TodayFile => Path.Combine(_logsFolder, $"{DateTime.Now:yyyy-MM-dd}.log");

    public void Info(string message) => Write("INFO", message);

    public void Error(string message, Exception? error = null) =>
        Write("ERROR", error is null ? message : $"{message}{Environment.NewLine}{error}");

    private void Write(string level, string message)
    {
        string line = $"{DateTime.Now:HH:mm:ss.fff} {level} {message}{Environment.NewLine}";
        lock (_writeLock)
        {
            Directory.CreateDirectory(_logsFolder);
            File.AppendAllText(TodayFile, line);
        }
    }
}
