﻿using HandheldCompanion.Managers;
using HandheldCompanion.Views.Windows;
using System;

namespace HandheldCompanion.Commands.Functions.Multimedia
{
    [Serializable]
    public class BrightnessDecrease : FunctionCommands
    {
        public BrightnessDecrease()
        {
            Name = Properties.Resources.Hotkey_decreaseBrightness;
            Description = Properties.Resources.Hotkey_decreaseBrightnessDesc;
            Glyph = "\uEC8A";
            OnKeyDown = true;
        }

        public override void Execute(bool IsKeyDown, bool IsKeyUp, bool IsBackground)
        {
            ToastManager.RunToast($"{ManagerFactory.multimediaManager.AdjustBrightness(-2)}", ToastIcons.BrightnessDown);
            base.Execute(IsKeyDown, IsKeyUp, false);
        }

        public override object Clone()
        {
            BrightnessDecrease commands = new()
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
