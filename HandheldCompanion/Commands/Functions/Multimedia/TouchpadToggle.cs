using HandheldCompanion.Managers;
using HandheldCompanion.Views.Windows;
using System;
using System.Threading;

namespace HandheldCompanion.Commands.Functions.Multimedia;

[Serializable]
public class TouchpadToggle : FunctionCommands
{
    public TouchpadToggle()
    {
        Name = Properties.Resources.Hotkey_touchpadToggle;
        Description = Properties.Resources.Hotkey_touchpadToggleDesc;
        Glyph = "\uEFA5";
        OnKeyDown = true;
    }

    public override void Execute(bool IsKeyDown, bool IsKeyUp, bool IsBackground)
    {
        var status = ManagerFactory.deviceManager.ToggleTouchpad();
        Thread.Sleep(200);
        if (status is not null)
            ToastManager.RunToast($"{Properties.Resources.Hotkey_touchpadToggle} {((bool)status ? Properties.Resources.On : Properties.Resources.Off)}",
                ToastIcons.Touchpad);
        base.Execute(IsKeyDown, IsKeyUp, false);
    }

    public override bool IsToggled => ManagerFactory.deviceManager.GetToggleTouchpadState() ?? false;

    public override object Clone()
    {
        TouchpadToggle commands = new()
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
