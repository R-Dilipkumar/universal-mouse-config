using System.Collections.ObjectModel;
using System.Windows.Threading;
using Key = System.Windows.Input.Key;
using KeyInterop = System.Windows.Input.KeyInterop;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MouseConfig.Core.Devices;
using MouseConfig.Core.Input;

namespace MouseConfig.App.ViewModels;

/// <summary>One thing to try in the test, ticked once it's been seen.</summary>
public sealed partial class CheckItem : ObservableObject
{
    public CheckItem(string key, string name)
    {
        Key = key;
        Name = name;
    }

    public string Key { get; }
    public string Name { get; }

    [ObservableProperty]
    private bool _seen;
}

public sealed record SeenInput(string Time, string What, string Channel, bool IsFromMakerChannel);

/// <summary>"Find my buttons": lights up the picture and lists what each press sent, and on which channel.</summary>
public sealed partial class ButtonTestViewModel : ObservableObject, IInputConsumer
{
    private const int MaxRows = 200;
    private static readonly TimeSpan FlashTime = TimeSpan.FromMilliseconds(180);
    private static readonly TimeSpan RateScreenUpdate = TimeSpan.FromMilliseconds(150);

    private readonly MouseDevice _mouse;
    private readonly Action<int> _peakRateFound;
    private readonly Dictionary<string, HidCollection> _channelById;
    private readonly DispatcherTimer _wheelFlash;
    private readonly ReportRateMeter _rate = new();
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    private TimeSpan _lastRateShown;

    public ButtonTestViewModel(MouseDevice mouse, Action<int> peakRateFound)
    {
        _mouse = mouse;
        _peakRateFound = peakRateFound;
        _channelById = mouse.Collections.ToDictionary(c => c.InstanceId, StringComparer.OrdinalIgnoreCase);
        _wheelFlash = new DispatcherTimer { Interval = FlashTime };
        _wheelFlash.Tick += (_, _) =>
        {
            _wheelFlash.Stop();
            WheelUpLit = WheelDownLit = TiltLeftLit = TiltRightLit = false;
        };

        // Unknown count (0): show everything rather than hide a button that may exist.
        int buttons = mouse.ButtonCount;
        HasBackButton = buttons == 0 || buttons >= 4;
        HasForwardButton = buttons == 0 || buttons >= 5;
        HasTilt = mouse.HasHorizontalWheel;

        var checks = new List<CheckItem>
        {
            new("b1", "Left"), new("b2", "Right"), new("b3", "Middle"),
        };
        if (HasBackButton) checks.Add(new("b4", "Back (4)"));
        if (HasForwardButton) checks.Add(new("b5", "Forward (5)"));
        if (mouse.HasWheel != false)
        {
            checks.Add(new("wheelup", "Wheel up"));
            checks.Add(new("wheeldown", "Wheel down"));
        }
        if (HasTilt)
        {
            checks.Add(new("tiltleft", "Tilt left"));
            checks.Add(new("tiltright", "Tilt right"));
        }
        Checks = checks;
    }

    public string MouseName => _mouse.Name;
    public bool HasBackButton { get; }
    public bool HasForwardButton { get; }
    public bool HasTilt { get; }
    public IReadOnlyList<CheckItem> Checks { get; }
    public ObservableCollection<SeenInput> Seen { get; } = [];

    [ObservableProperty] private bool _leftPressed;
    [ObservableProperty] private bool _rightPressed;
    [ObservableProperty] private bool _middlePressed;
    [ObservableProperty] private bool _backPressed;
    [ObservableProperty] private bool _forwardPressed;
    [ObservableProperty] private bool _wheelUpLit;
    [ObservableProperty] private bool _wheelDownLit;
    [ObservableProperty] private bool _tiltLeftLit;
    [ObservableProperty] private bool _tiltRightLit;

    [ObservableProperty]
    private string _listenState = "Not listening";

    [ObservableProperty] private string _rateText = "Move the mouse quickly in circles to measure it.";
    [ObservableProperty] private string _rateValue = "–";

    public bool BelongsToMouse(InputEvent input) => InputDecoding.BelongsTo(input.DeviceInstanceId, _mouse);

    public List<(ushort UsagePage, ushort Usage)> UsagesToListen() => InputDecoding.UsagesToListen(_mouse);

    public void Show(InputEvent input)
    {
        switch (input.Kind)
        {
            case InputKind.Move:
                MeasureRate();
                return;
            case InputKind.MouseButton:
                SetPressed(input.Code, input.IsDown);
                if (input.IsDown) Tick($"b{input.Code}");
                break;
            case InputKind.Wheel:
                WheelUpLit = input.Code > 0;
                WheelDownLit = input.Code < 0;
                Tick(input.Code > 0 ? "wheelup" : "wheeldown");
                RestartFlash();
                break;
            case InputKind.TiltWheel:
                TiltRightLit = input.Code > 0;
                TiltLeftLit = input.Code < 0;
                Tick(input.Code > 0 ? "tiltright" : "tiltleft");
                RestartFlash();
                break;
        }

        // Releases would double the list, and each wheel notch is its own event: show presses, and one row
        // per run of wheel turns in the same direction.
        bool isWheel = input.Kind is InputKind.Wheel or InputKind.TiltWheel;
        bool worthARow = isWheel ? LastRowIsNot(input.Detail) : input.IsDown;
        if (worthARow)
        {
            AddRow(input);
        }
    }

    /// <summary>Counts one movement report; shows the rate a few times a second (not on every report).</summary>
    private void MeasureRate()
    {
        TimeSpan now = _clock.Elapsed;
        int peakBefore = _rate.Peak;
        _rate.AddMove(now);
        if (now - _lastRateShown < RateScreenUpdate)
        {
            return;
        }
        _lastRateShown = now;
        if (_rate.Peak == 0)
        {
            RateText = "Keep moving…";
            return;
        }
        int? standard = ReportRateMeter.NearestStandard(_rate.Peak);
        RateValue = $"{_rate.Peak} Hz";
        RateText = standard is { } rate
            ? $"Your mouse sends about {rate} reports per second ({rate} Hz). Now: {_rate.Current} Hz."
            : $"Highest so far. Now: {_rate.Current} Hz. Keep moving fast for a steady number.";
        if (_rate.Peak != peakBefore)
        {
            _peakRateFound(standard ?? _rate.Peak);
        }
    }

    [RelayCommand]
    private void Clear()
    {
        _rate.Reset();
        RateValue = "–";
        RateText = "Move the mouse quickly in circles to measure it.";
        Seen.Clear();
        foreach (CheckItem check in Checks)
        {
            check.Seen = false;
        }
    }

    private void AddRow(InputEvent input)
    {
        string what = input.Kind switch
        {
            InputKind.Key => $"Key: {KeyName(input.Code)}",
            InputKind.MediaKey => $"Media key: {input.Detail}",
            InputKind.SystemKey => $"System key: {input.Detail}",
            InputKind.MakerData => $"Maker data: {input.Detail}",
            _ => input.Detail,
        };
        Seen.Insert(0, new SeenInput(DateTime.Now.ToString("HH:mm:ss"), what, ChannelText(input.DeviceInstanceId),
            input.Kind == InputKind.MakerData));
        while (Seen.Count > MaxRows)
        {
            Seen.RemoveAt(Seen.Count - 1);
        }
    }

    private bool LastRowIsNot(string detail) => Seen.Count == 0 || Seen[0].What != detail;

    private string ChannelText(string deviceId) =>
        _channelById.TryGetValue(deviceId, out HidCollection? channel)
            ? $"{(channel.ShortName.Length > 0 ? channel.ShortName + " · " : "")}{channel.Meaning}"
            : "Mouse";

    private static string KeyName(int virtualKey)
    {
        Key key = KeyInterop.KeyFromVirtualKey(virtualKey);
        return key == Key.None ? $"0x{virtualKey:X2}" : key.ToString();
    }

    private void SetPressed(int button, bool isDown)
    {
        switch (button)
        {
            case 1: LeftPressed = isDown; break;
            case 2: RightPressed = isDown; break;
            case 3: MiddlePressed = isDown; break;
            case 4: BackPressed = isDown; break;
            case 5: ForwardPressed = isDown; break;
        }
    }

    private void Tick(string key)
    {
        CheckItem? check = Checks.FirstOrDefault(c => c.Key == key);
        if (check is not null)
        {
            check.Seen = true;
        }
    }

    private void RestartFlash()
    {
        _wheelFlash.Stop();
        _wheelFlash.Start();
    }
}
