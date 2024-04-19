using HandheldCompanion.Managers;
using HandheldCompanion.Utils;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Page = System.Windows.Controls.Page;

namespace HandheldCompanion.Views.QuickPages;

/// <summary>
///     Interaction logic for QuickHomePage.xaml
/// </summary>
public partial class QuickHomePage : Page
{
    /*
    private LockObject brightnessLock = new();
    private LockObject volumeLock = new();
    */

    public QuickHomePage(string Tag) : this()
    {
        this.Tag = Tag;

        HotkeysManager.HotkeyCreated += HotkeysManager_HotkeyCreated;
        HotkeysManager.HotkeyUpdated += HotkeysManager_HotkeyUpdated;

        /*
        MultimediaManager.VolumeNotification += SystemManager_VolumeNotification;
        MultimediaManager.BrightnessNotification += SystemManager_BrightnessNotification;
        MultimediaManager.Initialized += SystemManager_Initialized;

        ProfileManager.Applied += ProfileManager_Applied;
        SettingsManager.SettingValueChanged += SettingsManager_SettingValueChanged;
        */
    }

    public QuickHomePage()
    {
        InitializeComponent();
    }

    private void HotkeysManager_HotkeyUpdated(Hotkey hotkey)
    {
        UpdatePins();
    }

    private void HotkeysManager_HotkeyCreated(Hotkey hotkey)
    {
        UpdatePins();
    }

    private void UpdatePins()
    {
        // todo, implement quick hotkey order
        QuickHotkeys.Children.Clear();

        foreach (var hotkey in HotkeysManager.Hotkeys.Values.Where(item => item.IsPinned))
            QuickHotkeys.Children.Add(hotkey.GetPin());
    }

    private void QuickButton_Click(object sender, RoutedEventArgs e)
    {
        Button button = (Button)sender;
        MainWindow.overlayquickTools.NavView_Navigate(button.Name);
    }

    /*
    private void SystemManager_Initialized()
    {
        // UI thread (async)
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            if (MultimediaManager.HasBrightnessSupport())
            {
                SliderBrightness.IsEnabled = true;
                SliderBrightness.Value = MultimediaManager.GetBrightness();
            }

            if (MultimediaManager.HasVolumeSupport())
            {
                SliderVolume.IsEnabled = true;
                SliderVolume.Value = Math.Round(MultimediaManager.GetVolume());
                UpdateVolumeIcon((float)SliderVolume.Value, MultimediaManager.GetMute());
            }
        });
    }

    private void SystemManager_BrightnessNotification(int brightness)
    {
        // UI thread
        Application.Current.Dispatcher.Invoke(() =>
        {
            using (new ScopedLock(brightnessLock))
                SliderBrightness.Value = brightness;
        });
    }

    private void SystemManager_VolumeNotification(float volume)
    {
        // UI thread
        Application.Current.Dispatcher.Invoke(() =>
        {
            using (new ScopedLock(volumeLock))
            {
                UpdateVolumeIcon(volume);
                SliderVolume.Value = Math.Round(volume);
            }
        });
    }

    private void SliderBrightness_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded)
            return;

        // wait until lock is released
        if (brightnessLock)
            return;

       MultimediaManager.SetBrightness(SliderBrightness.Value);
    }

    private void SliderVolume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded)
            return;

        // wait until lock is released
        if (volumeLock)
            return;

        MultimediaManager.SetVolume(SliderVolume.Value);
    }

    private void ProfileManager_Applied(Profile profile, UpdateSource source)
    {
        // UI thread (async)
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            t_CurrentProfile.Text = profile.ToString();
        });
    }

    private void SettingsManager_SettingValueChanged(string name, object value)
    {
        string[] onScreenDisplayLevels = {
            Properties.Resources.OverlayPage_OverlayDisplayLevel_Disabled,
            Properties.Resources.OverlayPage_OverlayDisplayLevel_Minimal,
            Properties.Resources.OverlayPage_OverlayDisplayLevel_Extended,
            Properties.Resources.OverlayPage_OverlayDisplayLevel_Full,
            Properties.Resources.OverlayPage_OverlayDisplayLevel_Custom,
            Properties.Resources.OverlayPage_OverlayDisplayLevel_External,
        };

        switch (name)
        {
            case "OnScreenDisplayLevel":
                {
                    var overlayLevel = Convert.ToInt16(value);

                    // UI thread (async)
                    Application.Current.Dispatcher.BeginInvoke(() =>
                    {
                        t_CurrentOverlayLevel.Text = onScreenDisplayLevels[overlayLevel];
                    });
                }
                break;
        }

    }
    */
    /*
    private void UpdateVolumeIcon(float volume, bool mute = false)
    {
        string glyph = mute ? "\uE74F" :
            volume switch
        {
            <= 0 => "\uE74F",// Mute icon
            <= 33 => "\uE993",// Low volume icon
            <= 65 => "\uE994",// Medium volume icon
            _ => "\uE995",// High volume icon (default)
        };
        VolumeIcon.Glyph = glyph;
    }

    private void VolumeButton_Click(object sender, RoutedEventArgs e)
    {
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            UpdateVolumeIcon((float)MultimediaManager.GetVolume(), MultimediaManager.ToggleMute());
        });
    }
    */
}
