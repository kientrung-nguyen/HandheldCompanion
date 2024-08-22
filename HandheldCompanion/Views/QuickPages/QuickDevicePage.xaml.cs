using HandheldCompanion.Devices;
using HandheldCompanion.Managers;
using HandheldCompanion.Managers.Desktop;
using HandheldCompanion.Misc;
using HandheldCompanion.Views.Windows;
using iNKORE.UI.WPF.Modern.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Timers;
using System.Windows;
using System.Windows.Controls;
using Windows.Devices.Radios;
using WindowsDisplayAPI;
using Page = System.Windows.Controls.Page;

namespace HandheldCompanion.Views.QuickPages;

/// <summary>
///     Interaction logic for QuickDevicePage.xaml
/// </summary>
public partial class QuickDevicePage : Page
{
    private IReadOnlyList<Radio> radios;
    private Timer radioTimer;

    public QuickDevicePage()
    {
        InitializeComponent();

        MultimediaManager.PrimaryScreenChanged += DesktopManager_PrimaryScreenChanged;
        MultimediaManager.DisplaySettingsChanged += DesktopManager_DisplaySettingsChanged;
        MultimediaManager.Initialized += MultimediaManager_Initialized;
        MultimediaManager.NightLightNotification += MultimediaManager_NightLightNotification;
        SettingsManager.SettingValueChanged += SettingsManager_SettingValueChanged;
        ProfileManager.Applied += ProfileManager_Applied;
        ProfileManager.Discarded += ProfileManager_Discarded;

        LegionGoPanel.Visibility = IDevice.GetCurrent() is LegionGo ? Visibility.Visible : Visibility.Collapsed;
        DynamicLightingPanel.Visibility = IDevice.GetCurrent().Capabilities.HasFlag(DeviceCapabilities.DynamicLighting) ? Visibility.Visible : Visibility.Collapsed;
        panelNightlight.Visibility = MultimediaManager.HasNightLightSupport() ? Visibility.Visible : Visibility.Collapsed;
        NightLightSchedule.IsEnabled = NightLightToggle.IsEnabled = MultimediaManager.HasNightLightSupport();

        // why is that part of a timer ?
        radioTimer = new(1000);
        radioTimer.Elapsed += RadioTimer_Elapsed;
        radioTimer.Start();
    }

    private void MultimediaManager_NightLightNotification(bool enabled)
    {
        if (MultimediaManager.HasNightLightSupport())
        {
            // UI thread
            Application.Current.Dispatcher.Invoke(() =>
            {
                NightLightToggle.IsOn = enabled;
            });
        }
    }

    private void MultimediaManager_Initialized()
    {
        if (MultimediaManager.HasNightLightSupport())
        {
            // UI thread
            Application.Current.Dispatcher.Invoke(() =>
            {
                NightLightToggle.IsOn = NightLight.Get() == 1;
            });
        }
    }

    public QuickDevicePage(string Tag) : this()
    {
        this.Tag = Tag;
    }

    private void ProfileManager_Applied(Profile profile, UpdateSource source)
    {
        // Go to profile integer scaling resolution
        if (profile.IntegerScalingEnabled)
        {
            //DesktopScreen desktopScreen = MultimediaManager.PrimaryDesktop;
            //var profileResolution = desktopScreen?.screenDividers.FirstOrDefault(d => d.divider == profile.IntegerScalingDivider);
            //if (profileResolution is not null)
            //{
            //    SetResolution(profileResolution.resolution);
            //}
        }
        else
        {
            // UI thread (async)
            Application.Current.Dispatcher.Invoke(() =>
            {
                // Revert back to resolution in device settings
                SetResolution();
            });
        }

        // UI thread (async)
        Application.Current.Dispatcher.Invoke(() =>
        {
            var canChangeDisplay = !profile.IntegerScalingEnabled;
            DisplayStack.IsEnabled = canChangeDisplay;
            ResolutionOverrideStack.Visibility = canChangeDisplay ? Visibility.Collapsed : Visibility.Visible;
        });
    }

    private void ProfileManager_Discarded(Profile profile)
    {
        // UI thread (async)
        Application.Current.Dispatcher.Invoke(() =>
        {
            SetResolution();

            if (profile.IntegerScalingEnabled)
            {
                DisplayStack.IsEnabled = true;
                ResolutionOverrideStack.Visibility = Visibility.Collapsed;
            }
        });
    }

    private void SettingsManager_SettingValueChanged(string? name, object value)
    {
        // UI thread
        Application.Current.Dispatcher.Invoke(() =>
        {
            switch (name)
            {
                case "ScreenFrequencyAuto":
                    AutoScreenToggle.IsOn = SettingsManager.Get<bool>(name);
                    break;
                case "NightLightSchedule":
                    NightLightSchedule.IsOn = SettingsManager.Get<bool>(name);
                    break;
                case "NightLightTurnOn":
                    NightlightTurnOn.SelectedDateTime = SettingsManager.Get<DateTime?>(name);
                    break;
                case "NightLightTurnOff":
                    NightlightTurnOff.SelectedDateTime = SettingsManager.Get<DateTime?>(name);
                    break;
                case "LEDSettingsEnabled":
                    UseDynamicLightingToggle.IsOn = Convert.ToBoolean(value);
                    break;
            }
        });
    }

    private void RadioTimer_Elapsed(object? sender, ElapsedEventArgs e)
    {
        new Task(async () =>
        {
            // Get the Bluetooth adapter
            //BluetoothAdapter adapter = await BluetoothAdapter.GetDefaultAsync();

            // Get the Bluetooth radio
            radios = await Radio.GetRadiosAsync();

            // UI thread
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (radios is null)
                {
                    WifiToggle.IsEnabled = false;
                    BluetoothToggle.IsEnabled = false;
                    return;
                }

                Radio? wifiRadio = radios.FirstOrDefault(radio => radio.Kind == RadioKind.WiFi);
                Radio? bluetoothRadio = radios.FirstOrDefault(radio => radio.Kind == RadioKind.Bluetooth);

                // WIFI
                WifiToggle.IsEnabled = wifiRadio != null;
                WifiToggle.IsOn = wifiRadio?.State == RadioState.On;

                // Bluetooth
                BluetoothToggle.IsEnabled = bluetoothRadio != null;
                BluetoothToggle.IsOn = bluetoothRadio?.State == RadioState.On;
            });
        }).Start();
    }

    private void DesktopManager_PrimaryScreenChanged(Display? screen)
    {
        ComboBoxResolution.Items.Clear();
        if (screen is not null)
        {
            foreach (var setting in screen.DisplayScreen.GetPossibleSettings()
                .DistinctBy(setting => new
                {
                    setting.Resolution.Width,
                    setting.Resolution.Height
                })
                .OrderByDescending(setting => (ulong)setting.Resolution.Height * (ulong)setting.Resolution.Width))
            {
                ComboBoxResolution.Items.Add(new ComboBoxItem
                {
                    Tag = setting,
                    Content = $"{setting.Resolution.Width} x {setting.Resolution.Height}"
                });
            }
        }
    }

    private void DesktopManager_DisplaySettingsChanged(Display? desktopScreen)
    {
        // We don't want to change the combobox when it's changed from profile integer scaling
        var currentProfile = ProfileManager.GetCurrent();
        if (ComboBoxResolution.SelectedItem is not null && currentProfile is not null && currentProfile.IntegerScalingEnabled)
            return;

        if (desktopScreen is null)
            return;

        foreach (ComboBoxItem item in ComboBoxResolution.Items)
        {
            if (item.Tag is DisplayPossibleSetting resolution &&
                resolution.Resolution.Width == desktopScreen.DisplayScreen.CurrentSetting.Resolution.Width &&
                resolution.Resolution.Height == desktopScreen.DisplayScreen.CurrentSetting.Resolution.Height)
            {
                ComboBoxResolution.SelectedItem = item;
                break;
            }
        }

        foreach (ComboBoxItem item in ComboBoxFrequency.Items)
        {
            if (item.Tag is DisplayPossibleSetting frequency &&
                frequency.Resolution.Width == desktopScreen.DisplayScreen.CurrentSetting.Resolution.Width &&
                frequency.Resolution.Height == desktopScreen.DisplayScreen.CurrentSetting.Resolution.Height &&
                frequency.Frequency == desktopScreen.DisplayScreen.CurrentSetting.Frequency
                )
            {
                ComboBoxFrequency.SelectedItem = item;
                break;
            }
        }

    }

    private void ComboBoxResolution_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ComboBoxResolution.SelectedItem is null)
            return;

        if (ScreenControl.PrimaryDisplay is null)
            return;

        var selectedResolution = (ComboBoxItem)ComboBoxResolution.SelectedItem;
        var resolution = (DisplayPossibleSetting)selectedResolution.Tag;
        var frequencies = ScreenControl.PrimaryDisplay.DisplayScreen.GetPossibleSettings()
            .Where(setting =>
                setting.Resolution.Width == resolution.Resolution.Width &&
                setting.Resolution.Height == resolution.Resolution.Height)
            .DistinctBy(setting => setting.Frequency)
            .OrderByDescending(setting => setting.Frequency);

        var currFrequency = ScreenControl.PrimaryDisplay.DisplayScreen.CurrentSetting;

        ComboBoxFrequency.Items.Clear();
        foreach (var frequency in frequencies)
        {
            var item = new ComboBoxItem
            {
                Content = $"{frequency.Frequency} Hz",
                Tag = frequency
            };

            ComboBoxFrequency.Items.Add(item);

            if (frequency.Frequency == currFrequency.Frequency)
                ComboBoxFrequency.SelectedItem = item;
        }

        SetResolution();
    }

    private void ComboBoxFrequency_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ComboBoxFrequency.SelectedItem is null)
            return;

        SetResolution();
    }

    private void SetResolution()
    {
        if (ScreenControl.PrimaryDisplay is null) return;

        if (ComboBoxResolution.SelectedItem is null || ComboBoxFrequency.SelectedItem is null) return;

        var selectedResolution = (ComboBoxItem)ComboBoxResolution.SelectedItem;
        var selectedFrequency = (ComboBoxItem)ComboBoxFrequency.SelectedItem;

        var resolution = (DisplayPossibleSetting)selectedResolution.Tag;
        var frequency = (DisplayPossibleSetting)selectedFrequency.Tag;

        var currSetting = ScreenControl.PrimaryDisplay.DisplayScreen.CurrentSetting;

        if (currSetting.Resolution.Width == resolution.Resolution.Width &&
            currSetting.Resolution.Height == resolution.Resolution.Height &&
            currSetting.Frequency == frequency.Frequency)
            return;

        ScreenControl.Set(ScreenControl.PrimaryDisplay, ScreenControl.PrimaryDisplay.DisplayScreen.GetPossibleSettings().First(setting =>
                setting.Resolution.Width == resolution.Resolution.Width &&
                setting.Resolution.Height == resolution.Resolution.Height &&
                setting.Frequency == frequency.Frequency &&
                setting.ColorDepth == currSetting.ColorDepth
            ));

    }

    //public void SetResolution(ScreenResolution resolution)
    //{
    //    // update current screen resolution
    //    MultimediaManager.SetResolution(resolution.Width, resolution.Height, MultimediaManager.PrimaryDesktop.GetCurrentFrequency(), resolution.BitsPerPel);
    //}

    private void WIFIToggle_Toggled(object sender, RoutedEventArgs e)
    {
        foreach (Radio radio in radios.Where(r => r.Kind == RadioKind.WiFi))
            _ = radio.SetStateAsync(WifiToggle.IsOn ? RadioState.On : RadioState.Off);
    }

    private void BluetoothToggle_Toggled(object sender, RoutedEventArgs e)
    {
        foreach (Radio radio in radios.Where(r => r.Kind == RadioKind.Bluetooth))
            _ = radio.SetStateAsync(BluetoothToggle.IsOn ? RadioState.On : RadioState.Off);
    }

    private void UseDynamicLightingToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded)
            return;

        SettingsManager.Set("LEDSettingsEnabled", UseDynamicLightingToggle.IsOn);
    }

    internal void Close()
    {
        radioTimer.Stop();
    }

    private void Toggle_cFFanSpeed_Toggled(object sender, RoutedEventArgs e)
    {
        if (IDevice.GetCurrent() is LegionGo device)
        {
            ToggleSwitch toggleSwitch = (ToggleSwitch)sender;
            device.SetFanFullSpeedAsync(toggleSwitch.IsOn);
        }
    }

    private void NightLightToggle_Toggled(object sender, RoutedEventArgs e)
    {
        var isEnabled = NightLight.Set(NightLightToggle.IsOn);
        if (isEnabled is not null)
            ToastManager.RunToast(
                $"Night light {(isEnabled.Value ? Properties.Resources.On : Properties.Resources.Off)}",
                isEnabled.Value ? ToastIcons.Nightlight : ToastIcons.NightlightOff);
    }

    private void NightLightSchedule_Toggled(object sender, RoutedEventArgs e)
    {
        SettingsManager.Set("NightLightSchedule", NightLightSchedule.IsOn);
    }

    private void NightlightTurnOn_SelectedDateTimeChanged(object sender, RoutedPropertyChangedEventArgs<DateTime?> e)
    {

        if (NightlightTurnOn.SelectedDateTime != null)
        {
            SettingsManager.Set("NightLightTurnOn", NightlightTurnOn.SelectedDateTime);
            NightLight.Auto();
        }
    }

    private void NightlightTurnOff_SelectedDateTimeChanged(object sender, RoutedPropertyChangedEventArgs<DateTime?> e)
    {
        if (NightlightTurnOff.SelectedDateTime != null)
        {
            SettingsManager.Set("NightLightTurnOff", NightlightTurnOff.SelectedDateTime);
            NightLight.Auto();
        }
    }

    private void AutoScreenToggle_Toggled(object sender, RoutedEventArgs e)
    {
        SettingsManager.Set("ScreenFrequencyAuto", AutoScreenToggle.IsOn);

    }
}