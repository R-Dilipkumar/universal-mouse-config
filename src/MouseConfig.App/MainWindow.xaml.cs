using System.Windows;
using System.Windows.Interop;
using MouseConfig.App.ViewModels;
using MouseConfig.Core;
using MouseConfig.Core.Devices;
using MouseConfig.Core.Input;
using MouseConfig.Core.Settings;

namespace MouseConfig.App;

public partial class MainWindow : Window
{
    private const double PreferredWidth = 1180;
    private const double PreferredHeight = 760;

    private readonly MainViewModel _viewModel;
    private readonly RawInputReader _inputReader = new();
    private HwndSource? _source;
    private IInputConsumer? _listening;
    private DeviceChangeWatcher? _deviceWatcher;

    public MainWindow()
    {
        InitializeComponent();
        FitToScreen();
        _viewModel = new MainViewModel(new MouseDetector(App.Log), App.Log);
        _viewModel.Settings = new MouseSettingsViewModel(
            () =>
            {
                var system = new SystemMouseSettings();
                var original = OriginalSettingsSnapshot.LoadOrCreate(
                    AppPaths.Default.OriginalWindowsSettingsFile, system.Read, DateTime.Now);
                return new MouseSettingsEditor(system, original);
            },
            App.Log,
            message => _viewModel.StatusText = message);
        _viewModel.SelectedTheme = _viewModel.ThemeChoices.First(t => t.Theme == ThemeService.Chosen);
        DataContext = _viewModel;
        if (App.Settings.LoadProblem is { } problem)
        {
            _viewModel.StatusText = $"Couldn't use your saved settings ({problem}) Using defaults.";
        }
        Loaded += async (_, _) => await _viewModel.RefreshAsync();
        Activated += async (_, _) => await _viewModel.Settings.ReloadAsync();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _source = (HwndSource)PresentationSource.FromVisual(this);
        _source.AddHook(OnMessage);
        try
        {
            _deviceWatcher = new DeviceChangeWatcher(_source);
            _deviceWatcher.DevicesChanged += async (_, _) => await _viewModel.RefreshAsync();
        }
        catch (Exception error)
        {
            App.Log.Error("Plug-in watcher failed to start", error);
            _viewModel.StatusText = $"Won't notice plugged-in mice on its own ({error.Message}). Use the scan button.";
        }
        _viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(MainViewModel.SelectedMouse) or nameof(MainViewModel.SelectedTabIndex))
            {
                UpdateListening();
            }
        };
        IsVisibleChanged += (_, _) => UpdateListening();
    }

    protected override void OnClosed(EventArgs e)
    {
        _deviceWatcher?.Dispose();
        StopListening();
        _source?.RemoveHook(OnMessage);
        base.OnClosed(e);
    }

    /// <summary>
    /// The button test and the mouse memory tab listen to the mouse only while they're open and the window shows.
    /// Opening mouse memory also reads the mouse the first time (reading changes nothing).
    /// </summary>
    private void UpdateListening()
    {
        MouseViewModel? mouse = _viewModel.SelectedMouse;
        IInputConsumer? wanted = !IsVisible || mouse is null ? null : _viewModel.SelectedTabIndex switch
        {
            ViewModels.Tabs.Test => mouse.ButtonTest,
            ViewModels.Tabs.MouseMemory when mouse.Hardware.IsSupported => mouse.Hardware,
            _ => null,
        };
        if (wanted is HardwareViewModel hardware)
        {
            hardware.ReadIfNeeded();
        }
        if (ReferenceEquals(wanted, _listening))
        {
            return;
        }
        StopListening();
        if (wanted is null)
        {
            return;
        }
        try
        {
            List<(ushort UsagePage, ushort Usage)> refused = _inputReader.Start(_source!.Handle, wanted.UsagesToListen());
            _listening = wanted;
            wanted.ListenState = refused.Count == 0
                ? $"Listening to {wanted.MouseName}."
                : $"Listening, but Windows refused these channels: {string.Join(", ", refused.Select(u => $"0x{u.UsagePage:X4}/0x{u.Usage:X4}"))}";
        }
        catch (Exception error)
        {
            App.Log.Error("Listening to the mouse failed to start", error);
            wanted.ListenState = $"Couldn't listen to the mouse: {error.Message}";
        }
    }

    private void StopListening()
    {
        if (_listening is null)
        {
            return;
        }
        _listening.ListenState = "Not listening";
        _listening = null;
        try
        {
            _inputReader.Stop();
        }
        catch (Exception error)
        {
            App.Log.Error("Listening to the mouse failed to stop", error);
            _viewModel.StatusText = $"Couldn't stop listening to the mouse: {error.Message}";
        }
    }

    private IntPtr OnMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != RawInputReader.WmInput || _listening is null)
        {
            return IntPtr.Zero;
        }
        try
        {
            foreach (InputEvent input in _inputReader.Read(lParam))
            {
                if (_listening.BelongsToMouse(input))
                {
                    _listening.Show(input);
                    // Only this mouse's own presses, never the keyboard's: helps work out what odd buttons send.
                    if (input.IsDown && input.Kind is not (InputKind.Wheel or InputKind.TiltWheel or InputKind.Move))
                    {
                        App.Log.Info($"Mouse input: {input.Kind} code=0x{input.Code:X} {input.Detail} [{input.DeviceInstanceId}]");
                    }
                }
            }
        }
        catch (Exception error)
        {
            App.Log.Error("Reading the mouse failed", error);
            _listening.ListenState = $"Stopped: {error.Message}";
            StopListening();
        }
        return IntPtr.Zero; // WPF passes WM_INPUT on to Windows' default handling, which frees the input.
    }

    /// <summary>Keep the whole window, including the status line, on screen.</summary>
    private void FitToScreen()
    {
        Rect workArea = SystemParameters.WorkArea;
        MaxWidth = workArea.Width;
        MaxHeight = workArea.Height;
        Width = Math.Min(PreferredWidth, workArea.Width);
        Height = Math.Min(PreferredHeight, workArea.Height);
        MinWidth = Math.Min(MinWidth, workArea.Width);
        MinHeight = Math.Min(MinHeight, workArea.Height);
    }
}
