using System.Windows.Controls;
using System.Windows.Input;
using MouseConfig.App.ViewModels;

namespace MouseConfig.App.Views;

public partial class HardwareView : UserControl
{
    public HardwareView()
    {
        InitializeComponent();
    }

    private SlotEditorViewModel? CapturingSlot => (DataContext as HardwareViewModel)?.CapturingSlot;

    /// <summary>
    /// After "Record…", the next key pressed becomes that slot's key. Only that one press is taken,
    /// so Tab works normally afterwards (no keyboard trap).
    /// </summary>
    private void OnKeyCapture(object sender, KeyEventArgs e)
    {
        if (CapturingSlot is not { } slot)
        {
            return;
        }
        Key key = e.Key switch
        {
            Key.System => e.SystemKey,          // keys pressed with Alt
            Key.ImeProcessed => e.ImeProcessedKey,
            _ => e.Key,
        };
        e.Handled = true;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt
            or Key.LWin or Key.RWin)
        {
            return; // wait for the main key; the held modifier is read with it
        }
        ModifierKeys held = Keyboard.Modifiers;
        slot.Capture(KeyInterop.VirtualKeyFromKey(key),
            held.HasFlag(ModifierKeys.Control), held.HasFlag(ModifierKeys.Shift),
            held.HasFlag(ModifierKeys.Alt), held.HasFlag(ModifierKeys.Windows));
    }

    /// <summary>After "Record…", the next click inside this tab (mouse or touchpad) sets that mouse button as the action.</summary>
    private void OnMouseCapture(object sender, MouseButtonEventArgs e)
    {
        if (CapturingSlot is not { } slot)
        {
            return;
        }
        int button = e.ChangedButton switch
        {
            MouseButton.Left => 1,
            MouseButton.Right => 2,
            MouseButton.Middle => 3,
            MouseButton.XButton1 => 4,
            MouseButton.XButton2 => 5,
            _ => 0,
        };
        if (button > 0)
        {
            slot.CaptureMouseButton(button);
            e.Handled = true; // the click only records; it doesn't also press whatever is under the pointer
        }
    }
}
