using HandheldCompanion.Managers;
using HandheldCompanion.Properties;
using HandheldCompanion.Utils;
using HandheldCompanion.Views;
using HandheldCompanion.Views.Windows;
using System;

namespace HandheldCompanion.Commands.Functions.HC
{
    [Serializable]
    public class OverlayPerformanceCommands : FunctionCommands
    {
        public OverlayPerformanceCommands()
        {
            base.Name = Resources.InputsHotkey_OnScreenDisplayToggle;
            base.Description = Resources.InputsHotkey_OnScreenDisplayToggleDesc;
            base.Glyph = "\uE78B";
            base.OnKeyUp = true;

            MainWindow.overlayModel.IsVisibleChanged += IsVisibleChanged;
        }

        private void IsVisibleChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
        {
            base.Execute(OnKeyDown, OnKeyUp);
        }

        public override void Execute(bool IsKeyDown, bool IsKeyUp)
        {
            // check current OSD level
            // .. if 0 (disabled) -> set OSD level to LastOnScreenDisplayLevel
            // .. else (enabled) -> set OSD level to 0
            var currentProfile = ProfileManager.GetCurrent();
            currentProfile.OnScreenDisplayToggle = !currentProfile.OnScreenDisplayToggle;
            ToastManager.RunToast($"Overlay Performance {(currentProfile.OnScreenDisplayToggle ? Resources.On : Resources.Off)}", ToastIcons.Game);
            ProfileManager.UpdateOrCreateProfile(currentProfile, UpdateSource.Background);

            base.Execute(IsKeyDown, IsKeyUp);
        }

        public override bool IsToggled => ProfileManager.GetCurrent().OnScreenDisplayToggle;

        public override object Clone()
        {
            OverlayPerformanceCommands commands = new()
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
            MainWindow.overlayModel.IsVisibleChanged -= IsVisibleChanged;
            base.Dispose();
        }
    }
}
