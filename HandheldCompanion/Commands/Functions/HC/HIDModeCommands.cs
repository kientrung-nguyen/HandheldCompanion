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

            Update();

            ManagerFactory.profileManager.Applied += ProfileManager_Applied;
        }

        private void ProfileManager_Applied(Profile profile, UpdateSource source)
        {
            IsEnabled = profile.HID == HIDmode.NotSelected;
            Update(profile.HID);
        }

        public void Update(HIDmode profileMode = HIDmode.NotSelected)
        {
            HIDmode currentHIDmode = profileMode == HIDmode.NotSelected ? (HIDmode)ManagerFactory.settingsManager.Get<int>(SettingsName) : profileMode;
            switch (currentHIDmode)
            {
                case HIDmode.Xbox360Controller:
                    LiveGlyph = "\uE001";
                    break;
                case HIDmode.DualShock4Controller:
                    LiveGlyph = "\uE000";
                    break;
            }

            base.Update();
        }

        public override void Execute(bool IsKeyDown, bool IsKeyUp, bool IsBackground)
        {
            if (IsEnabled)
            {
                HIDmode currentHIDmode = (HIDmode)ManagerFactory.settingsManager.Get<int>(SettingsName);
                switch (currentHIDmode)
                {
                    case HIDmode.Xbox360Controller:
                        ManagerFactory.settingsManager.Set(SettingsName, (int)HIDmode.DualShock4Controller);
                        break;
                    case HIDmode.DualShock4Controller:
                        ManagerFactory.settingsManager.Set(SettingsName, (int)HIDmode.Xbox360Controller);
                        break;
                    default:
                        break;
                }
            }

            Update();
            base.Execute(IsKeyDown, IsKeyUp, false);
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
            ManagerFactory.profileManager.Applied -= ProfileManager_Applied;
            base.Dispose();
        }
    }
}
