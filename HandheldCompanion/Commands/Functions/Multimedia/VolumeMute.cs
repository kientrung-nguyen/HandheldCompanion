using HandheldCompanion.Managers;
using HandheldCompanion.Misc;
using System;

namespace HandheldCompanion.Commands.Functions.Multimedia
{
    [Serializable]
    public class VolumeMute : FunctionCommands
    {
        public VolumeMute()
        {
            Name = Properties.Resources.Hotkey_muteVolume;
            Description = Properties.Resources.Hotkey_muteVolumeDesc;
            Glyph = "\uE74F";
            OnKeyDown = true;

            Update();

            MultimediaManager.VolumeNotification += MultimediaManager_VolumeNotification;
        }

        private void MultimediaManager_VolumeNotification(SoundDirections flow, float volume, bool isMute)
        {
            Update();
        }

        public override void Update()
        {
            switch (IsToggled)
            {
                case true:
                    LiveGlyph = "\uE74F";
                    break;
                case false:
                    LiveGlyph = "\uE767";
                    break;
            }

            base.Update();
        }

        public override void Execute(bool IsKeyDown, bool IsKeyUp, bool IsBackground)
        {
            SoundControl.ToggleAudio();

            Update();
            base.Execute(IsKeyDown, IsKeyUp, false);
        }

        public override bool IsToggled => SoundControl.AudioMuted() ?? true;

        public override object Clone()
        {
            VolumeMute commands = new()
            {
                commandType = commandType,
                Name = Name,
                Description = Description,
                Glyph = Glyph,
                LiveGlyph = LiveGlyph,
                OnKeyUp = OnKeyUp,
                OnKeyDown = OnKeyDown
            };

            return commands;
        }

        public override void Dispose()
        {
            MultimediaManager.VolumeNotification -= MultimediaManager_VolumeNotification;
            base.Dispose();
        }
    }
}
