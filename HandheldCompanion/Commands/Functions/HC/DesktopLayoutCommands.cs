﻿using HandheldCompanion.Managers;
using System;

namespace HandheldCompanion.Commands.Functions.HC
{
    [Serializable]
    public class DesktopLayoutCommands : FunctionCommands
    {
        private const string SettingsName = "DesktopLayoutEnabled";

        public DesktopLayoutCommands()
        {
            base.Name = Properties.Resources.Hotkey_DesktopLayoutEnabled;
            base.Description = Properties.Resources.Hotkey_DesktopLayoutEnabledDesc;
            base.Glyph = "\uE961";
            base.OnKeyUp = true;

            ManagerFactory.settingsManager.SettingValueChanged += SettingsManager_SettingValueChanged;
        }

        private void SettingsManager_SettingValueChanged(string name, object value, bool temporary)
        {
            switch (name)
            {
                case SettingsName:
                    base.Execute(OnKeyDown, OnKeyUp, true);
                    break;
            }
        }

        public override void Execute(bool IsKeyDown, bool IsKeyUp, bool IsBackground)
        {
            bool value = !ManagerFactory.settingsManager.Get<bool>(SettingsName);
            ManagerFactory.settingsManager.Set(SettingsName, value, false);

            base.Execute(IsKeyDown, IsKeyUp, false);
        }

        public override bool IsToggled => ManagerFactory.settingsManager.Get<bool>(SettingsName);

        public override object Clone()
        {
            DesktopLayoutCommands commands = new()
            {
                commandType = this.commandType,
                Name = this.Name,
                Description = this.Description,
                Glyph = this.Glyph,
                OnKeyUp = this.OnKeyUp,
                OnKeyDown = this.OnKeyDown
            };

            return commands;
        }

        public override void Dispose()
        {
            ManagerFactory.settingsManager.SettingValueChanged -= SettingsManager_SettingValueChanged;
            base.Dispose();
        }
    }
}
