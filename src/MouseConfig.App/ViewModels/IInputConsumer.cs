using MouseConfig.Core.Input;

namespace MouseConfig.App.ViewModels;

/// <summary>A screen that listens to one mouse's raw input while it's open (button test, mouse memory).</summary>
public interface IInputConsumer
{
    string MouseName { get; }
    string ListenState { get; set; }
    bool BelongsToMouse(InputEvent input);
    List<(ushort UsagePage, ushort Usage)> UsagesToListen();
    void Show(InputEvent input);
}
