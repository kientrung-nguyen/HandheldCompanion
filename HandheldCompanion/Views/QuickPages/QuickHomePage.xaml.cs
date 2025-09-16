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

        MultimediaManager.VolumeNotification += MultimediaManager_VolumeNotification;
        MultimediaManager.BrightnessNotification += MultimediaManager_BrightnessNotification;
        MultimediaManager.NightLightNotification += MultimediaManager_NightLightNotification;
        MultimediaManager.Initialized += MultimediaManager_Initialized;

        VolumeSupport.IsEnabled = MultimediaManager.HasVolumeSupport();
        BrightnessSupport.IsEnabled = MultimediaManager.HasBrightnessSupport();
        NightLightSupport.IsEnabled = MultimediaManager.HasNightLightSupport();
        GPUManager.Hooked += GPUManager_Hooked;
    }

    private void GPUManager_Hooked(GraphicsProcessingUnit.GPU GPU)
    {
        // do something
    }

    public QuickHomePage()
    {
        DataContext = new QuickHomePageViewModel();
        InitializeComponent();
    }

    private void QuickButton_Click(object sender, RoutedEventArgs e)
    {
        Button button = (Button)sender;
        MainWindow.overlayquickTools.NavView_Navigate(button.Name);
    }

    private void MultimediaManager_Initialized()
    {
        if (MultimediaManager.HasBrightnessSupport())
        {
            lock (brightnessLock)
            {
                // UI thread
                Application.Current.Dispatcher.Invoke(() =>
                {
                    SliderBrightness.IsEnabled = true;
                    SliderBrightness.Value = ScreenBrightness.Get();
                });
            }
        }

        if (MultimediaManager.HasNightLightSupport())
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

        if (MultimediaManager.HasVolumeSupport())
        {
            lock (volumeLock)
            {
                // UI thread
                Application.Current.Dispatcher.Invoke(() =>
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
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (SliderBrightness.Value != brightness)
                        SliderBrightness.Value = brightness;
                });
            }
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
                Application.Current.Dispatcher.Invoke(() =>
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
            MultimediaManager.SetBrightness(SliderBrightness.Value);
    }

    private void SliderVolume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded)
            return;

        // prevent update loop
        if (volumeLock.IsEntered())
            return;

        lock (volumeLock)
            MultimediaManager.SetVolume(SliderVolume.Value);
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
                Application.Current.Dispatcher.Invoke(() =>
                {
                    var isMute = SoundControl.ToggleAudio();
                    UpdateVolumeIcon(SoundControl.AudioGet(), isMute ?? true);
                    if (isMute is not null)
                        ToastManager.RunToast(
                            isMute.Value ? Properties.Resources.Muted : Properties.Resources.Unmuted,
                            isMute.Value ? ToastIcons.VolumeMute : ToastIcons.Volume);
                });
            }
            finally
            {
                volumeLock.Exit();
            }
        }

    }


    private void MicButton_Click(object sender, RoutedEventArgs e)
    {
        if (microphoneLock.TryEnter())
        {
            try
            {
                // UI thread
                Application.Current.Dispatcher.Invoke(() =>
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
        if (nightlightLock.TryEnter())
        {
            try
            {
                // UI thread
                Application.Current.Dispatcher.Invoke(() =>
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
