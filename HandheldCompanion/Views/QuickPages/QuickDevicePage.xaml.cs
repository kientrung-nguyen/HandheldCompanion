using HandheldCompanion.Devices;
using HandheldCompanion.Helpers;
using HandheldCompanion.Managers;
using HandheldCompanion.Misc;
using HandheldCompanion.Shared;
using HandheldCompanion.Utils;
using HandheldCompanion.Views.Windows;
using iNKORE.UI.WPF.Modern.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Timers;
using System.Windows;
using System.Windows.Controls;
using Windows.Devices.Bluetooth;
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

        // manage events
        ManagerFactory.multimediaManager.PrimaryScreenChanged += MultimediaManager_PrimaryScreenChanged;
        ManagerFactory.multimediaManager.DisplaySettingsChanged += MultimediaManager_DisplaySettingsChanged;
        ManagerFactory.multimediaManager.Initialized += MultimediaManager_Initialized;
        ManagerFactory.multimediaManager.NightLightNotification += MultimediaManager_NightLightNotification;
        ManagerFactory.settingsManager.SettingValueChanged += SettingsManager_SettingValueChanged;
        ManagerFactory.profileManager.Applied += ProfileManager_Applied;
        ManagerFactory.profileManager.Discarded += ProfileManager_Discarded;

        // Device specific
        AYANEOFlipDSPanel.Visibility = IDevice.GetCurrent() is AYANEOFlipDS ? Visibility.Visible : Visibility.Collapsed;

        // Capabilities specific
        DynamicLightingPanel.IsEnabled = IDevice.GetCurrent().Capabilities.HasFlag(DeviceCapabilities.DynamicLighting);
        FanOverridePanel.Visibility = IDevice.GetCurrent().Capabilities.HasFlag(DeviceCapabilities.FanOverride) ? Visibility.Visible : Visibility.Collapsed;

        NightLightToggle.Visibility = ManagerFactory.multimediaManager.HasNightLightSupport() ? Visibility.Visible : Visibility.Collapsed;
        NightLightSchedule.IsEnabled = NightLightToggle.IsEnabled = ManagerFactory.multimediaManager.HasNightLightSupport();

        // why is that part of a timer ?
        radioTimer = new(1000);
        radioTimer.Elapsed += RadioTimer_Elapsed;
        radioTimer.Start();
    }

    public void Close()
    {
        // manage events
        ManagerFactory.multimediaManager.PrimaryScreenChanged -= MultimediaManager_PrimaryScreenChanged;
        ManagerFactory.multimediaManager.DisplaySettingsChanged -= MultimediaManager_DisplaySettingsChanged;
        ManagerFactory.multimediaManager.NightLightNotification -= MultimediaManager_NightLightNotification;
        ManagerFactory.settingsManager.SettingValueChanged -= SettingsManager_SettingValueChanged;
        ManagerFactory.profileManager.Applied -= ProfileManager_Applied;
        ManagerFactory.profileManager.Discarded -= ProfileManager_Discarded;

        radioTimer.Stop();
    }

    private void MultimediaManager_NightLightNotification(bool enabled)
    {
        if (ManagerFactory.multimediaManager.HasNightLightSupport())
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
        if (ManagerFactory.multimediaManager.HasNightLightSupport())
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
            var primaryDisplay = ScreenControl.PrimaryDisplay;
            var index = 1;
            var possibleResolutions = ScreenControl.PrimaryDisplay.DisplayScreen.GetPossibleSettings()
                .DistinctBy(setting => new
                {
                    setting.Resolution.Width,
                    setting.Resolution.Height
                })
                .OrderByDescending(setting => (ulong)setting.Resolution.Height * (ulong)setting.Resolution.Width);
            if (possibleResolutions.Any())
            {
                var nativeResolution = possibleResolutions.First();
                while (possibleResolutions.FirstOrDefault(r => r.Resolution.Height == (nativeResolution.Resolution.Height / index)) is DisplayPossibleSetting possibleSetting && possibleSetting is not null)
                {
                    if (index == profile.IntegerScalingDivider)
                    {
                        LogManager.LogError($"1/{index} {possibleSetting.Resolution.Width} x {possibleSetting.Resolution.Height}");
                        var currSetting = ScreenControl.PrimaryDisplay.DisplayScreen.CurrentSetting;
                        if (currSetting.Resolution.Width == possibleSetting.Resolution.Width &&
                            currSetting.Resolution.Height == possibleSetting.Resolution.Height)
                            return;
                        ScreenControl.Set(primaryDisplay, primaryDisplay.DisplayScreen.GetPossibleSettings().First(setting =>
                            setting.Resolution.Width == possibleSetting.Resolution.Width &&
                            setting.Resolution.Height == possibleSetting.Resolution.Height &&
                            setting.Frequency == currSetting.Frequency &&
                            setting.ColorDepth == currSetting.ColorDepth
                        ));
                        break;
                    }
                    index++;
                }
            }
        }

        // UI thread
        UIHelper.TryInvoke(() =>
        {
            var canChangeDisplay = !profile.IntegerScalingEnabled;
            DisplayStack.IsEnabled = canChangeDisplay;
            ResolutionOverrideStack.Visibility = canChangeDisplay ? Visibility.Collapsed : Visibility.Visible;
        });
    }

    private void ProfileManager_Discarded(Profile profile, bool swapped, Profile nextProfile)
    {
        // don't bother discarding settings, new one will be enforce shortly
        if (swapped && nextProfile.IntegerScalingEnabled)
            return;

        if (profile.IntegerScalingEnabled)
        {
            // UI thread
            UIHelper.TryInvoke(() =>
            {
                DisplayStack.IsEnabled = true;
                ResolutionOverrideStack.Visibility = Visibility.Collapsed;

                // restore default resolution
                if (profile.IntegerScalingDivider != 1)
                    SetResolution();

            });
        }
    }

    private void SettingsManager_SettingValueChanged(string? name, object value, bool temporary)
    {
        // UI thread
        UIHelper.TryInvoke(() =>
        {
            switch (name)
            {
                case "ScreenFrequencyAuto":
                    AutoScreenToggle.IsOn = ManagerFactory.settingsManager.Get<bool>(name);
                    break;
                case "NightLightSchedule":
                    NightLightSchedule.IsOn = ManagerFactory.settingsManager.Get<bool>(name);
                    break;
                case "NightLightTurnOn":
                    NightlightTurnOn.SelectedDateTime = ManagerFactory.settingsManager.Get<DateTime?>(name);
                    break;
                case "NightLightTurnOff":
                    NightlightTurnOff.SelectedDateTime = ManagerFactory.settingsManager.Get<DateTime?>(name);
                    break;
                case "LEDSettingsEnabled":
                    UseDynamicLightingToggle.IsOn = Convert.ToBoolean(value);
                    break;
                case "AYANEOFlipScreenEnabled":
                    Toggle_AYANEOFlipScreen.IsOn = Convert.ToBoolean(value);
                    break;
                case "AYANEOFlipScreenBrightness":
                    Slider_AYANEOFlipScreenBrightness.Value = Convert.ToDouble(value);
                    break;
            }
        });
    }

    private void RadioTimer_Elapsed(object? sender, ElapsedEventArgs e)
    {
        new Task(async () =>
        {
            // Get the Bluetooth adapter
            BluetoothAdapter adapter = await BluetoothAdapter.GetDefaultAsync();

            // Get the Bluetooth radio
            radios = await Radio.GetRadiosAsync();

            // UI thread
            UIHelper.TryInvoke(() =>
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

    private CrossThreadLock multimediaLock = new();
    private void MultimediaManager_PrimaryScreenChanged(Display screen)
    {
        if (multimediaLock.TryEnter())
        {
            try
            {
                // UI thread
                UIHelper.TryInvoke(() =>
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
                });
            }
            catch { }
            finally
            {
                multimediaLock.Exit();
            }
        }
    }

    private void MultimediaManager_DisplaySettingsChanged(Display desktopScreen)
    {
        if (multimediaLock.TryEnter())
        {
            try
            {
                // We don't want to change the combobox when it's changed from profile integer scaling
                var currentProfile = ManagerFactory.profileManager.GetCurrent();
                if (currentProfile is not null && currentProfile.IntegerScalingEnabled)
                {
                    ProfileManager_Applied(currentProfile, UpdateSource.Background);
                    return;
                }

                // UI thread
                UIHelper.TryInvoke(() =>
                {
                    foreach (ComboBoxItem item in ComboBoxResolution.Items)
                    {
                        if (item.Tag is DisplayPossibleSetting resolution 
                            && resolution.Resolution.Width == desktopScreen.DisplayScreen.CurrentSetting.Resolution.Width
                            && resolution.Resolution.Height == desktopScreen.DisplayScreen.CurrentSetting.Resolution.Height
                            && (ComboBoxResolution.SelectedItem is null
                                || (ComboBoxResolution.SelectedItem is ComboBoxItem selectedItem
                                    && selectedItem.Tag is DisplayPossibleSetting selectedResolution
                                    && (selectedResolution.Resolution.Width != desktopScreen.DisplayScreen.CurrentSetting.Resolution.Width
                                        || selectedResolution.Resolution.Height != desktopScreen.DisplayScreen.CurrentSetting.Resolution.Height))))
                        {
                            ComboBoxResolution.SelectedItem = item;

                            var frequencies = desktopScreen.DisplayScreen.GetPossibleSettings()
                                .Where(setting =>
                                    setting.Resolution.Width == resolution.Resolution.Width &&
                                    setting.Resolution.Height == resolution.Resolution.Height)
                                .DistinctBy(setting => setting.Frequency)
                                .OrderByDescending(setting => setting.Frequency);

                            var currFrequency = desktopScreen.DisplayScreen.CurrentSetting;

                            ComboBoxFrequency.Items.Clear();
                            foreach (var frequency in frequencies)
                            {
                                var frequencyItem = new ComboBoxItem
                                {
                                    Content = $"{frequency.Frequency} Hz",
                                    Tag = frequency
                                };

                                ComboBoxFrequency.Items.Add(frequencyItem);

                                if (frequency.Frequency == currFrequency.Frequency)
                                    ComboBoxFrequency.SelectedItem = frequencyItem;
                            }
                            break;
                        }
                    }
                });
            }
            catch { }
            finally
            {
                multimediaLock.Exit();
            }
        }
    }

    private void ComboBoxResolution_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ComboBoxResolution.SelectedItem is null)
            return;

        // prevent update loop
        if (multimediaLock.IsEntered())
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

        // prevent update loop
        if (multimediaLock.IsEntered())
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

        ManagerFactory.settingsManager.Set("LEDSettingsEnabled", UseDynamicLightingToggle.IsOn);
    }

    private void Toggle_FanOverride_Toggled(object sender, RoutedEventArgs e)
    {
        ToggleSwitch toggleSwitch = (ToggleSwitch)sender;
        if (IDevice.GetCurrent() is LegionGo device)
            device.SetFanFullSpeed(toggleSwitch.IsOn);
        else if (IDevice.GetCurrent() is ClawA2VM claw8)
            claw8.SetFanFullSpeed(toggleSwitch.IsOn);
    }

    private void NightLightToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded)
            return;

        var isEnabled = NightLight.Set(NightLightToggle.IsOn);
        if (isEnabled is not null)
            ToastManager.RunToast(
                $"Night light {(isEnabled.Value ? Properties.Resources.On : Properties.Resources.Off)}",
                isEnabled.Value ? ToastIcons.Nightlight : ToastIcons.NightlightOff);
    }

    private void NightLightSchedule_Toggled(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded)
            return;

        ManagerFactory.settingsManager.Set("NightLightSchedule", NightLightSchedule.IsOn);
    }

    private void NightlightTurnOn_SelectedDateTimeChanged(object sender, RoutedPropertyChangedEventArgs<DateTime?> e)
    {
        if (NightlightTurnOn.SelectedDateTime != null)
        {
            ManagerFactory.settingsManager.Set("NightLightTurnOn", NightlightTurnOn.SelectedDateTime);
            NightLight.Auto();
        }
    }

    private void NightlightTurnOff_SelectedDateTimeChanged(object sender, RoutedPropertyChangedEventArgs<DateTime?> e)
    {
        if (NightlightTurnOff.SelectedDateTime != null)
        {
            ManagerFactory.settingsManager.Set("NightLightTurnOff", NightlightTurnOff.SelectedDateTime);
            NightLight.Auto();
        }
    }

    private void AutoScreenToggle_Toggled(object sender, RoutedEventArgs e)
    {

        if (!IsLoaded)
            return;

        ManagerFactory.settingsManager.Set("ScreenFrequencyAuto", AutoScreenToggle.IsOn);

    }

    private async void Toggle_AYANEOFlipScreen_Toggled(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded)
            return;

        bool enabled = Toggle_AYANEOFlipScreen.IsOn;
        if (!enabled)
        {
            // todo: translate me
            Task<ContentDialogResult> dialogTask = new Dialog(OverlayQuickTools.GetCurrent())
            {
                Title = "Warning",
                Content = "To reactivate the lower screen, press the dual screen button on your device.",
                CloseButtonText = Properties.Resources.ProfilesPage_Cancel,
                PrimaryButtonText = Properties.Resources.ProfilesPage_OK
            }.ShowAsync();

            await dialogTask; // sync call

            switch (dialogTask.Result)
            {
                case ContentDialogResult.Primary:
                    break;

                default:
                case ContentDialogResult.None:
                    // restore previous state
                    Toggle_AYANEOFlipScreen.IsOn = true;
                    return;
            }

            ManagerFactory.settingsManager.Set("AYANEOFlipScreenEnabled", enabled);
        }
    }

    private void Slider_AYANEOFlipScreenBrightness_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        var value = Slider_AYANEOFlipScreenBrightness.Value;
        if (double.IsNaN(value))
            return;

        if (!IsLoaded)
            return;

        ManagerFactory.settingsManager.Set("AYANEOFlipScreenBrightness", value);
    }
}