using HandheldCompanion.Managers;
using HandheldCompanion.Properties;
using HandheldCompanion.Views.Windows;
using System;
using System.Threading;

namespace HandheldCompanion.Commands.Functions.Multimedia
{
    [Serializable]
    public class TouchpadToggle : FunctionCommands
    {
        public TouchpadToggle()
        {
            Name = Properties.Resources.InputsHotkey_touchpadToggle;
            Description = Properties.Resources.InputsHotkey_touchpadToggleDesc;
            Glyph = "\uEFA5";
            OnKeyDown = true;
        }

        public override void Execute(bool IsKeyDown, bool IsKeyUp)
        {
            var status = DeviceManager.ToggleTouchpad();
            Thread.Sleep(200);
            if (status is not null)
                ToastManager.RunToast($"{Resources.InputsHotkey_touchpadToggle} {((bool)status ? Resources.On : Resources.Off)}",
                    ToastIcons.Touchpad);
            base.Execute(IsKeyDown, IsKeyUp);
        }

        public override bool IsToggled => DeviceManager.GetToggleTouchpadState() ?? false;

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
}
