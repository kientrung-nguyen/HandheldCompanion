using HandheldCompanion.Managers;
using HandheldCompanion.Properties;
using HandheldCompanion.Views.Windows;
using System;

namespace HandheldCompanion.Commands.Functions.Multimedia
{
    [Serializable]
    public class TouchscreenToggle : FunctionCommands
    {
        public TouchscreenToggle()
        {
            Name = Properties.Resources.InputsHotkey_touchscreenToggle;
            Description = Properties.Resources.InputsHotkey_touchscreenToggleDesc;
            Glyph = "\uebfc";
            OnKeyDown = true;
        }

        public override void Execute(bool IsKeyDown, bool IsKeyUp)
        {
            var status = DeviceManager.ToggleTouchscreen();
            if (status is not null)
                ToastManager.RunToast($"{Resources.InputsHotkey_touchscreenToggle} {((bool)status ? Resources.On : Resources.Off)}",
                    ToastIcons.Touchscreen);
            base.Execute(IsKeyDown, IsKeyUp);
        }

        public override bool IsToggled => DeviceManager.GetToggleTouchscreenState() ?? false;

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
}
