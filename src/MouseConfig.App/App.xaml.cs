using System.Windows;
using System.Windows.Threading;
using MouseConfig.Core;

namespace MouseConfig.App;

/// <summary>
/// The app only sets things up: settings inside the mouse and Windows' own settings keep working after it closes,
/// so nothing stays running in the background. Closing the window quits.
/// </summary>
public partial class App : Application
{
    public static Log Log { get; } = new(AppPaths.Default.LogsFolder);

    /// <summary>Hardware plugin for Sunplus 1BCF:08A0 (Redgear). Compiled in; one per app so its session lock is shared.</summary>
    public static MouseConfig.Plugins.Sunplus1BCF.SunplusPlugin Sunplus { get; } = new(AppPaths.Default, Log);

    /// <summary>The app's saved choices (theme).</summary>
    public static SettingsStore Settings { get; } = new(new Core.Settings.AppSettingsFile(AppPaths.Default.SettingsFile), Log);

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledUiError;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Error("Unhandled error (background)", args.ExceptionObject as Exception);

        Log.Info("App started");
        ThemeService.Apply(Settings.Current.Theme);
        ThemeService.FollowWindowsChanges();
        MainWindow = new MainWindow();
        MainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Info("App closed");
        base.OnExit(e);
    }

    /// <summary>Yes/No question with No as the default answer. Used before anything is written to a mouse.</summary>
    public static bool Confirm(string title, string text) =>
        MessageBox.Show(Current.MainWindow, text, title, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No)
        == MessageBoxResult.Yes;

    private void OnUnhandledUiError(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("Unhandled error (UI)", e.Exception);
        MessageBox.Show(
            $"Something went wrong:\n\n{e.Exception.Message}\n\nDetails were saved to:\n{Log.TodayFile}",
            "Mouse Config", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
