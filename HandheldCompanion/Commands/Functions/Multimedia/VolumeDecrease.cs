using HandheldCompanion.Managers;
using HandheldCompanion.Views.Windows;
using System;

namespace HandheldCompanion.Commands.Functions.Multimedia
{
    [Serializable]
    public class VolumeDecrease : FunctionCommands
    {
        public VolumeDecrease()
        {
            Name = Properties.Resources.Hotkey_decreaseVolume;
            Description = Properties.Resources.Hotkey_decreaseVolumeDesc;
            Glyph = "\uE993";
            OnKeyDown = true;
        }

        public override void Execute(bool IsKeyDown, bool IsKeyUp)
        {
            ToastManager.RunToast($"{MultimediaManager.AdjustVolume(-2)}", ToastIcons.VolumeDown);
            base.Execute(IsKeyDown, IsKeyUp);
        }

        public override object Clone()
        {
            VolumeDecrease commands = new()
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
