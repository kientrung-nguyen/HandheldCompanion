using HandheldCompanion.Helpers;
using HandheldCompanion.Managers;
using HandheldCompanion.Misc;
using HandheldCompanion.Utils;
using HandheldCompanion.ViewModels;
using HandheldCompanion.Views.Windows;
using System;
using System.Windows;
using System.Windows.Controls;
using Page = System.Windows.Controls.Page;

namespace HandheldCompanion.Views.QuickPages;

public partial class QuickHomePage : Page
{
    private readonly CrossThreadLock brightnessLock = new();
    private readonly CrossThreadLock volumeLock = new();

    public QuickHomePage(string Tag) : this()
    {
        this.Tag = Tag;

        ManagerFactory.multimediaManager.VolumeNotification += SystemManager_VolumeNotification;
        ManagerFactory.multimediaManager.MicrophoneVolumeNotification += SystemManager_MicrophoneVolumeNotification;
        ManagerFactory.multimediaManager.BrightnessNotification += SystemManager_BrightnessNotification;
        ManagerFactory.multimediaManager.NightLightNotification += SystemManager_NightLightNotification;
        ManagerFactory.multimediaManager.Initialized += SystemManager_Initialized;
    }

    public void Close()
    {
        ManagerFactory.multimediaManager.VolumeNotification -= SystemManager_VolumeNotification;
        ManagerFactory.multimediaManager.MicrophoneVolumeNotification -= SystemManager_MicrophoneVolumeNotification;
        ManagerFactory.multimediaManager.BrightnessNotification -= SystemManager_BrightnessNotification;
        ManagerFactory.multimediaManager.NightLightNotification -= SystemManager_NightLightNotification;
        ManagerFactory.multimediaManager.Initialized -= SystemManager_Initialized;
    }

    public QuickHomePage()
    {
        DataContext = new QuickHomePageViewModel();
        InitializeComponent();
    }

    private void QuickButton_Click(object sender, RoutedEventArgs e)
    {
        Button button = (Button)sender;
        OverlayQuickTools.GetCurrent().NavigateToPage(button.Name);
    }

    private void SystemManager_Initialized()
    {
        if (ManagerFactory.multimediaManager.HasBrightnessSupport())
        {
            UIHelper.TryBeginInvoke(() =>
            {
                SliderBrightness.IsEnabled = true;

                if (brightnessLock.TryEnter())
                {
                    try
                    {
                        SliderBrightness.Value = ManagerFactory.multimediaManager.GetBrightness();
                    }
                    catch { }
                    finally
                    {
                        brightnessLock.Exit();
                    }
                }
            });
        }

        if (ManagerFactory.multimediaManager.HasVolumeSupport())
        {
            UIHelper.TryBeginInvoke(() =>
            {
                VolumeButton.IsEnabled = true;
                SliderVolume.IsEnabled = true;

                var vol = ManagerFactory.multimediaManager.GetVolume();
                var rounded = Math.Round(vol);

                if (volumeLock.TryEnter())
                {
                    try
                    {
                        UpdateVolumeIcon(rounded);
                        SliderVolume.Value = rounded;
                    }
                    finally
                    {
                        volumeLock.Exit();
                    }
                }
            });
        }

        if (ManagerFactory.multimediaManager.HasMicrophoneSupport())
        {
            UIHelper.TryBeginInvoke(() =>
            {
                MicButton.IsEnabled = true;
                SliderMic.IsEnabled = true;

                var vol = ManagerFactory.multimediaManager.GetMicVolume();
                var rounded = Math.Round(vol);

                if (volumeLock.TryEnter())
                {
                    try
                    {
                        MicIcon.Glyph = ManagerFactory.multimediaManager.IsMicMuted() ? "\uf781" : "\ue720";
                        SliderMic.Value = rounded;
                    }
                    finally
                    {
                        volumeLock.Exit();
                    }
                }
            });
        }

        if (ManagerFactory.multimediaManager.HasNightLightSupport())
        {
            UIHelper.TryBeginInvoke(() =>
            {
                BrightnessButton.IsEnabled = true;
                if (brightnessLock.TryEnter())
                {
                    try
                    {
                        LightIcon.Glyph = ManagerFactory.multimediaManager.GetNightLight() < 1 ? "\uE706" : "\uf08c";
                    }
                    finally
                    {
                        brightnessLock.Exit();
                    }
                }

            });
        }
    }

    private void SystemManager_BrightnessNotification(int brightness)
    {
        UIHelper.TryBeginInvoke(() =>
        {
            if (Math.Abs(SliderBrightness.Value - brightness) < double.Epsilon)
                return;

            if (brightnessLock.TryEnter())
            {
                try
                {
                    if (SliderBrightness.Value != brightness)
                        SliderBrightness.Value = brightness;
                }
                catch { }
                finally
                {
                    brightnessLock.Exit();
                }
            }
        });
    }

    private void SystemManager_VolumeNotification(float volume)
    {
        var rounded = Math.Round(Convert.ToDouble(volume));

        UIHelper.TryBeginInvoke(() =>
        {
            UpdateVolumeIcon(rounded);

            SliderVolume.IsEnabled = volume >= 0;

            if (volume < 0 || Math.Abs(SliderVolume.Value - rounded) < double.Epsilon)
                return;

            if (volumeLock.TryEnter())
            {
                try
                {
                    SliderVolume.Value = rounded;
                }
                catch { }
                finally
                {
                    volumeLock.Exit();
                }
            }
        });
    }

    private void SystemManager_MicrophoneVolumeNotification(float volume)
    {
        var rounded = Math.Round(Convert.ToDouble(volume));

        UIHelper.TryBeginInvoke(() =>
        {
            MicIcon.Glyph = rounded < 0 ? "\uf781" : "\ue720";

            SliderMic.IsEnabled = volume >= 0;

            if (volume < 0 || Math.Abs(SliderMic.Value - rounded) < double.Epsilon)
                return;

            if (volumeLock.TryEnter())
            {
                try
                {
                    SliderMic.Value = rounded;
                }
                catch { }
                finally
                {
                    volumeLock.Exit();
                }
            }
        });
    }

    private void SystemManager_NightLightNotification(bool enabled)
    {
        UIHelper.TryBeginInvoke(() =>
        {
            if (brightnessLock.TryEnter())
            {
                try
                {
                    LightIcon.Glyph = !enabled ? "\uE706" : "\uf08c";
                }
                finally
                {
                    brightnessLock.Exit();
                }
            }
        });
    }

    private void SliderBrightness_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded)
            return;

        // If we're setting the value from a notification/init, don't feedback into SetBrightness
        if (brightnessLock.IsEntered())
            return;

        try
        {
            lock (brightnessLock)
            {
                ManagerFactory.multimediaManager.SetBrightness(SliderBrightness.Value);
            }
        }
        catch { }
    }

    private void SliderVolume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded)
            return;

        if (volumeLock.IsEntered())
            return;

        try
        {
            lock (volumeLock)
            {
                ManagerFactory.multimediaManager.Unmute();
                ManagerFactory.multimediaManager.SetVolume(SliderVolume.Value);
            }
        }
        catch { }
    }


    private void SliderMic_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded)
            return;

        if (volumeLock.IsEntered())
            return;

        try
        {
            lock (volumeLock)
            {
                ManagerFactory.multimediaManager.MicUnmute();
                ManagerFactory.multimediaManager.SetMicVolume(SliderMic.Value);
            }
        }
        catch { }
    }

    private void UpdateVolumeIcon(double volume, bool mute = false)
    {
        VolumeIcon.Glyph = mute ? "\uE74F" :
            volume switch
            {
                <= 0 => "\uE74F",// Mute icon
                <= 33 => "\uE993",// Low volume icon
                <= 65 => "\uE994",// Medium volume icon
                _ => "\uE995",// High volume icon (default)
            };
    }

    private void BrightnessButton_Click(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded)
            return;

        // prevent update loop
        if (brightnessLock.TryEnter())
        {
            try
            {
                // UI thread
                UIHelper.TryBeginInvoke(() =>
                {
                    if (!NightLight.Supported)
                        return;

                    var wasEnabled = NightLight.Get() >= 1;
                    NightLight.Set(!wasEnabled);
                    LightIcon.Glyph = !wasEnabled ? "\uE706" : "\uf08c";
                    //ToastManager.RunToast(
                    //    $"Night light {(isEnabled.Value ? Properties.Resources.On : Properties.Resources.Off)}",
                    //    isEnabled.Value ? ToastIcons.Nightlight : ToastIcons.NightlightOff);
                });
            }
            finally
            {
                brightnessLock.Exit();
            }
        }
    }

    private void VolumeButton_Click(object sender, RoutedEventArgs e)
    {
        if (volumeLock.TryEnter())
        {
            try
            {
                // UI thread
                UIHelper.TryBeginInvoke(() =>
                {
                    ManagerFactory.multimediaManager.ToggleMute();
                    UpdateVolumeIcon(
                        ManagerFactory.multimediaManager.GetVolume(),
                        ManagerFactory.multimediaManager.IsMuted()
                        );
                    //ToastManager.RunToast(
                    //    isMute.Value ? Properties.Resources.Muted : Properties.Resources.Unmuted,
                    //    isMute.Value ? ToastIcons.VolumeMute : ToastIcons.Volume);
                });
            }
            catch { }
            finally
            {
                volumeLock.Exit();
            }
        }
    }

    private void MicButton_Click(object sender, RoutedEventArgs e)
    {
        if (volumeLock.TryEnter())
        {
            try
            {
                // UI thread
                UIHelper.TryBeginInvoke(() =>
                {
                    ManagerFactory.multimediaManager.ToggleMicMute();
                    MicIcon.Glyph = ManagerFactory.multimediaManager.IsMicMuted() ? "\uf781" : "\ue720";
                });
            }
            catch { }
            finally
            {
                volumeLock.Exit();
            }
        }
    }
}