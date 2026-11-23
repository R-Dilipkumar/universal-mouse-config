namespace MouseConfig.Core.Input;

public enum InputKind
{
    /// <summary>Code = button number 1–5.</summary>
    MouseButton,
    /// <summary>Code = wheel delta; positive is away from you (up).</summary>
    Wheel,
    /// <summary>Code = tilt delta; positive is right.</summary>
    TiltWheel,
    /// <summary>Code = Windows virtual-key number.</summary>
    Key,
    /// <summary>Code = HID consumer usage (e.g. 0xE9 volume up).</summary>
    MediaKey,
    /// <summary>Code = HID generic-desktop usage (e.g. 0x82 sleep).</summary>
    SystemKey,
    /// <summary>A report on the maker's own channel. Detail = the bytes in hex.</summary>
    MakerData,
    /// <summary>The mouse moved. Used to measure how many reports per second it sends.</summary>
    Move,
}

/// <summary>One thing the mouse sent, as seen by the button test.</summary>
public sealed record InputEvent(InputKind Kind, string DeviceInstanceId, int Code, bool IsDown, string Detail);
