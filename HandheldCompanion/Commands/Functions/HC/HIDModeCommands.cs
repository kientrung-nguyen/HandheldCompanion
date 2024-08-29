﻿using HandheldCompanion.Managers;
using HandheldCompanion.Utils;
using System;

namespace HandheldCompanion.Commands.Functions.HC
{
    [Serializable]
    public class HIDModeCommands : FunctionCommands
    {
        private const string SettingsName = "HIDmode";

        public HIDModeCommands()
        {
            base.Name = Properties.Resources.Hotkey_ChangeHIDMode;
            base.Description = Properties.Resources.Hotkey_ChangeHIDModeDesc;
            base.OnKeyUp = true;
            base.FontFamily = "PromptFont";
            base.Glyph = "\u243C";

            HIDmode currentHIDmode = (HIDmode)SettingsManager.Get<int>(SettingsName);
            switch (currentHIDmode)
            {
                case HIDmode.Xbox360Controller:
                    LiveGlyph = "\uE001";
                    break;
                case HIDmode.DualShock4Controller:
                    LiveGlyph = "\uE000";
                    break;
            }

            ProfileManager.Applied += ProfileManager_Applied;
        }

        private void ProfileManager_Applied(Profile profile, UpdateSource source)
        {
            IsEnabled = profile.HID == HIDmode.NotSelected;
            base.Update();
        }

        public override void Execute(bool IsKeyDown, bool IsKeyUp)
        {
            if (IsEnabled)
            {
                HIDmode currentHIDmode = (HIDmode)SettingsManager.Get<int>(SettingsName);
                switch (currentHIDmode)
                {
                    case HIDmode.Xbox360Controller:
                        SettingsManager.Set(SettingsName, (int)HIDmode.DualShock4Controller);
                        LiveGlyph = "\uE000";
                        break;
                    case HIDmode.DualShock4Controller:
                        SettingsManager.Set(SettingsName, (int)HIDmode.Xbox360Controller);
                        LiveGlyph = "\uE001";
                        break;
                    default:
                        break;
                }
            }

            base.Update();
            base.Execute(IsKeyDown, IsKeyUp);
        }

        public override object Clone()
        {
            HIDModeCommands commands = new()
            {
                commandType = this.commandType,
                Name = this.Name,
                Description = this.Description,
                FontFamily = this.FontFamily,
                Glyph = this.Glyph,
                LiveGlyph = this.LiveGlyph,
                OnKeyUp = this.OnKeyUp,
                OnKeyDown = this.OnKeyDown,
            };

            return commands;
        }

        public override void Dispose()
        {
            ProfileManager.Applied -= ProfileManager_Applied;
            base.Dispose();
        }
    }
}
