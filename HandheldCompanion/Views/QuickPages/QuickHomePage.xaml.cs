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

/// <summary>
///     Interaction logic for QuickHomePage.xaml
/// </summary>
public partial class QuickHomePage : Page
{
    private CrossThreadLock brightnessLock = new();
    private CrossThreadLock volumeLock = new();
    private CrossThreadLock microphoneLock = new();
    private CrossThreadLock nightlightLock = new();

    public QuickHomePage(string Tag) : this()
    {
        this.Tag = Tag;

        ManagerFactory.multimediaManager.VolumeNotification += MultimediaManager_VolumeNotification;
        ManagerFactory.multimediaManager.BrightnessNotification += MultimediaManager_BrightnessNotification;
        ManagerFactory.multimediaManager.NightLightNotification += MultimediaManager_NightLightNotification;
        ManagerFactory.multimediaManager.Initialized += MultimediaManager_Initialized;

        VolumeSupport.IsEnabled = ManagerFactory.multimediaManager.HasVolumeSupport();
        SliderBrightness.IsEnabled = ManagerFactory.multimediaManager.HasBrightnessSupport();
        BrightnessButton.IsEnabled = ManagerFactory.multimediaManager.HasNightLightSupport();
    }
	
	public void Close()
    {
        // manage events
        ManagerFactory.multimediaManager.VolumeNotification -= MultimediaManager_VolumeNotification;
        ManagerFactory.multimediaManager.BrightnessNotification -= MultimediaManager_BrightnessNotification;
        ManagerFactory.multimediaManager.NightLightNotification -= MultimediaManager_NightLightNotification;
        ManagerFactory.multimediaManager.Initialized -= MultimediaManager_Initialized;
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

    private void MultimediaManager_Initialized()
    {
        if (ManagerFactory.multimediaManager.HasBrightnessSupport())
        {
            lock (brightnessLock)
            {
                // UI thread
                UIHelper.TryInvoke(() =>
                {
                    SliderBrightness.IsEnabled = true;
                    SliderBrightness.Value = ScreenBrightness.Get();
                });
            }
        }

        if (ManagerFactory.multimediaManager.HasNightLightSupport())
        {
            lock (brightnessLock)
            {
                // UI thread
                Application.Current.Dispatcher.Invoke(() =>
                {
                    LightIcon.Glyph = NightLight.Get() == 0 ? "\uE706" : "\uf08c";
                });
            }
        }

        if (ManagerFactory.multimediaManager.HasVolumeSupport())
        {
            lock (volumeLock)
            {
                // UI thread
                UIHelper.TryInvoke(() =>
                {
                    SliderVolume.IsEnabled = true;
                    SliderVolume.Value = SoundControl.AudioGet();
                    UpdateVolumeIcon((float)SliderVolume.Value, SoundControl.AudioMuted() ?? true);

                    MicIcon.Glyph = SoundControl.MicrophoneMuted() ?? true ? "\uf781" : "\ue720";
                });
            }

            lock (microphoneLock)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    MicIcon.Glyph = SoundControl.MicrophoneMuted() ?? true ? "\uf781" : "\ue720";
                });
            }
        }
    }

    private void MultimediaManager_NightLightNotification(bool enabled)
    {
        if (nightlightLock.TryEnter())
        {
            try
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    LightIcon.Glyph = !enabled ? "\uE706" : "\uf08c";
                });
            }
            finally
            {
                nightlightLock.Exit();
            }
        }
    }

    private void MultimediaManager_BrightnessNotification(int brightness)
    {
        if (brightnessLock.TryEnter())
        {
            try
            {
                // UI thread
                UIHelper.TryInvoke(() =>
                {
                    if (SliderBrightness.Value != brightness)
                        SliderBrightness.Value = brightness;
                });
            }
            catch { }
            finally
            {
                brightnessLock.Exit();
            }
        }
    }

    private void MultimediaManager_VolumeNotification(SoundDirections flow, float volume, bool isMute)
    {
        switch (flow)
        {
            case SoundDirections.Output:
                if (volumeLock.TryEnter())
                {
                    try
                    {
                        // UI thread
                UIHelper.TryInvoke(() =>
                        {
                            UpdateVolumeIcon(volume, isMute);
                            SliderVolume.Value = Math.Round(volume);
                        });
                    }
                    finally
                    {
                        volumeLock.Exit();
                    }
                }
                break;
            case SoundDirections.Input:
                if (microphoneLock.TryEnter())
                {
                    try
                    {
                        // UI thread
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            MicIcon.Glyph = isMute ? "\uf781" : "\ue720";
                        });
                    }
            catch { }
                    finally
                    {
                        microphoneLock.Exit();
                    }
                }
                break;
        }

    }

    private void SliderBrightness_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded)
            return;

        // prevent update loop
        if (brightnessLock.IsEntered())
            return;

        lock (brightnessLock)
            ManagerFactory.multimediaManager.SetBrightness(SliderBrightness.Value);
    }

    private void SliderVolume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded)
            return;

        // prevent update loop
        if (volumeLock.IsEntered())
            return;

        lock (volumeLock)
            ManagerFactory.multimediaManager.SetVolume(SliderVolume.Value);
    }

    private void UpdateVolumeIcon(float volume, bool mute = false)
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

    private void VolumeButton_Click(object sender, RoutedEventArgs e)
    {
        if (volumeLock.TryEnter())
        {
            try
            {
                // UI thread
                UIHelper.TryInvoke(() =>
                {
                    var isMute = SoundControl.ToggleAudio();
                    UpdateVolumeIcon(SoundControl.AudioGet(), isMute ?? true);
                    if (isMute is not null)
                        ToastManager.RunToast(
                            isMute.Value ? Properties.Resources.Muted : Properties.Resources.Unmuted,
                            isMute.Value ? ToastIcons.VolumeMute : ToastIcons.Volume);
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
        if (!IsLoaded)
            return;

        // prevent update loop
        if (microphoneLock.TryEnter())
        {
            try
            {
                // UI thread
                UIHelper.TryInvoke(() =>
                {
                    var isMute = SoundControl.ToggleMicrophone();
                    MicIcon.Glyph = (isMute ?? true) ? "\uf781" : "\ue720";
                    if (isMute is not null)
                        ToastManager.RunToast(
                            isMute.Value ? Properties.Resources.Muted : Properties.Resources.Unmuted,
                            isMute.Value ? ToastIcons.MicrophoneMute : ToastIcons.Microphone);
                });
            }
            finally
            {
                microphoneLock.Exit();
            }
        }
    }

    private void BrightnessButton_Click(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded)
            return;

        // prevent update loop
        if (nightlightLock.TryEnter())
        {
            try
            {
                // UI thread
                UIHelper.TryInvoke(() =>
                {
                    var isEnabled = NightLight.Toggle();
                    if (isEnabled is not null)
                    {
                        LightIcon.Glyph = !isEnabled.Value ? "\uE706" : "\uf08c";
                        ToastManager.RunToast(
                            $"Night light {(isEnabled.Value ? Properties.Resources.On : Properties.Resources.Off)}",
                            isEnabled.Value ? ToastIcons.Nightlight : ToastIcons.NightlightOff);
                    }
                });
            }
            finally
            {
                nightlightLock.Exit();
            }
        }
    }
}
