using HandheldCompanion.Managers;
using HandheldCompanion.Views.Windows;
using System;

namespace HandheldCompanion.Commands.Functions.Multimedia;

[Serializable]
public class TouchscreenToggle : FunctionCommands
{
    public TouchscreenToggle()
    {
        Name = Properties.Resources.Hotkey_touchscreenToggle;
        Description = Properties.Resources.Hotkey_touchscreenToggleDesc;
        Glyph = "\uebfc";
        OnKeyDown = true;
    }

    public override void Execute(bool IsKeyDown, bool IsKeyUp, bool IsBackground)
    {
        var status = ManagerFactory.deviceManager.ToggleTouchscreen();
        if (status is not null)
            ToastManager.RunToast($"{Properties.Resources.Hotkey_touchscreenToggle} {((bool)status ? Properties.Resources.On : Properties.Resources.Off)}",
                ToastIcons.Touchscreen);
        base.Execute(IsKeyDown, IsKeyUp, false);
    }

    public override bool IsToggled => ManagerFactory.deviceManager.GetToggleTouchscreenState() ?? false;

    public override object Clone()
    {
        TouchscreenToggle commands = new()
        {
            commandType = commandType,
            Name = Name,
            Description = Description,
            Glyph = Glyph,
            OnKeyUp = OnKeyUp,
            OnKeyDown = OnKeyDown
        };

        return commands;
    }
}
