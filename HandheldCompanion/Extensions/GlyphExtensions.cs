using HandheldCompanion.Utils;
using HandheldCompanion.Views.Windows;

namespace HandheldCompanion.Extensions
{
    public static class GlyphExtensions
    {
        public static string ToGlyph(this MotionInput motionInput)
        {
            switch (motionInput)
            {
                default:
                case MotionInput.LocalSpace:
                    return "\uF272";
                case MotionInput.PlayerSpace:
                    return "\uF119";
                case MotionInput.WorldSpace:
                    return "\uE714";
                /*
            case MotionInput.AutoRollYawSwap:
                return "\uE7F8";
                */
                case MotionInput.JoystickSteering:
                    return "\uEC47";
            }
        }

        public static string ToGlyph(this MotionOutput motionOuput)
        {
            switch (motionOuput)
            {
                default:
                case MotionOutput.Disabled:
                    return "\uE8D8";
                case MotionOutput.RightStick:
                    return "\uF109";
                case MotionOutput.LeftStick:
                    return "\uF108";
                case MotionOutput.MoveCursor:
                    return "\uE962";
                case MotionOutput.ScrollWheel:
                    return "\uEC8F";
            }
        }

        public static string ToGlyph(this ToastIcons? icon)
        {
            return icon switch
            {
                ToastIcons.Game => "\ue7fc",
                ToastIcons.Touchscreen => "\ueda4",
                ToastIcons.Touchpad => "\uEFA5",
                ToastIcons.BrightnessUp => "\ue706",
                ToastIcons.BrightnessDown => "\uec8a",
                ToastIcons.Charger => "\ue83e",
                ToastIcons.Battery => "\ue859",
                ToastIcons.BatteryFull => "\uebb5",
                ToastIcons.VolumeUp => "\ue995",
                ToastIcons.VolumeDown => "\ue994",
                ToastIcons.VolumeMute => "\ue74f",
                ToastIcons.Volume => "\ue767",
                ToastIcons.MicrophoneMute => "\uf781",
                ToastIcons.Microphone => "\ue720",
                ToastIcons.Nightlight => "\uf08c",
                ToastIcons.NightlightOff => "\uE706",
                ToastIcons.Laptop => "\ue7f8",
                _ => "\ue713"
            };
    }
    }
}
