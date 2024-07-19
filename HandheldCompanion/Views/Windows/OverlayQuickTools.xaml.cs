﻿using HandheldCompanion.Managers;
using HandheldCompanion.Managers.Desktop;
using HandheldCompanion.Utils;
using HandheldCompanion.Views.Classes;
using HandheldCompanion.Views.QuickPages;
using iNKORE.UI.WPF.Modern.Controls;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Navigation;
using System.Windows.Threading;
using Windows.System.Power;
using WpfScreenHelper;
using WpfScreenHelper.Enum;
using Application = System.Windows.Application;
using Button = System.Windows.Controls.Button;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Page = System.Windows.Controls.Page;
using PowerLineStatus = System.Windows.Forms.PowerLineStatus;
using Screen = WpfScreenHelper.Screen;
using SystemManager = HandheldCompanion.Managers.SystemManager;
using SystemPowerManager = Windows.System.Power.PowerManager;
using Timer = System.Timers.Timer;

namespace HandheldCompanion.Views.Windows;

/// <summary>
///     Interaction logic for QuickTools.xaml
/// </summary>
public partial class OverlayQuickTools : GamepadWindow, INotifyPropertyChanged
{
    private const int SC_MOVE = 0xF010;
    private readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

    const UInt32 SWP_NOSIZE = 0x0001;
    const UInt32 SWP_NOMOVE = 0x0002;
    const UInt32 SWP_NOACTIVATE = 0x0010;
    const UInt32 SWP_NOZORDER = 0x0004;

    // Define the Win32 API constants and functions
    const int WM_PAINT = 0x000F;
    const int WM_ACTIVATEAPP = 0x001C;
    const int WM_ACTIVATE = 0x0006;
    const int WM_SETFOCUS = 0x0007;
    const int WM_KILLFOCUS = 0x0008;
    const int WM_NCACTIVATE = 0x0086;
    const int WM_SYSCOMMAND = 0x0112;
    const int WM_WINDOWPOSCHANGING = 0x0046;
    const int WM_SHOWWINDOW = 0x0018;
    const int WM_MOUSEACTIVATE = 0x0021;

    const int WS_EX_NOACTIVATE = 0x08000000;
    const int GWL_EXSTYLE = -20;

    private HwndSource hwndSource;

    private Dictionary<UIElement, CacheMode> cacheModes = new();
    private Timer WM_PAINT_TIMER;

    // page vars
    private readonly Dictionary<string, Page> _pages = new();

    private bool autoHide;
    private bool isClosing;
    private readonly DispatcherTimer clockUpdateTimer;

    public QuickHomePage homePage;
    public QuickDevicePage devicePage;
    public QuickPerformancePage performancePage;
    public QuickProfilesPage profilesPage;
    public QuickOverlayPage overlayPage;
    public QuickSuspenderPage suspenderPage;

    private static OverlayQuickTools currentWindow;
    private string preNavItemTag;


    private CrossThreadLock brightnessLock = new();
    private CrossThreadLock volumeLock = new();

    private Dictionary<string, Button> tabButtons = new();

    private Dictionary<string, bool> _activeTabs;

    public Dictionary<string, bool> activeTabs
    {
        get { return _activeTabs; }
        set
        {
            _activeTabs = value;

        }
    }

    public OverlayQuickTools()
    {
        InitializeComponent();
        currentWindow = this;

        // used by gamepad navigation
        Tag = "QuickTools";

        PreviewKeyDown += HandleEsc;

        clockUpdateTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(1000)
        };
        clockUpdateTimer.Tick += UpdateTime;

        WM_PAINT_TIMER = new(250) { AutoReset = false };
        WM_PAINT_TIMER.Elapsed += WM_PAINT_TIMER_Tick;

        // create manager(s)
        SystemManager.PowerStatusChanged += PowerManager_PowerStatusChanged;

        MultimediaManager.VolumeNotification += SystemManager_VolumeNotification;
        MultimediaManager.BrightnessNotification += SystemManager_BrightnessNotification;
        MultimediaManager.Initialized += SystemManager_Initialized;
        ProfileManager.Applied += ProfileManager_Applied;

        MultimediaManager.DisplaySettingsChanged += SystemManager_DisplaySettingsChanged;

        SettingsManager.SettingValueChanged += SettingsManager_SettingValueChanged;

        // create pages
        homePage = new("quickhome");
        devicePage = new("quickdevice");
        //performancePage = new("quickperformance");
        profilesPage = new("quickprofiles");
        //overlayPage = new("quickoverlay");
        suspenderPage = new("quicksuspender");

        _pages.Add("QuickHomePage", homePage);
        _pages.Add("QuickDevicePage", devicePage);
        //_pages.Add("QuickPerformancePage", performancePage);
        _pages.Add("QuickProfilesPage", profilesPage);
        //_pages.Add("QuickOverlayPage", overlayPage);
        _pages.Add("QuickSuspenderPage", suspenderPage);

        activeTabs = new()
        {
            { "QuickHomePage", true },
            { "QuickDevicePage", false },
            //{ "QuickPerformancePage", false },
            { "QuickProfilesPage", false },
            { "QuickOverlayPage", false },
            { "QuickSuspenderPage", false }
        };

        tabButtons = new()
        {
            { "QuickHomePage", QuickHomePage },
            { "QuickDevicePage", QuickDevicePage },
            { "QuickProfilesPage", QuickProfilesPage },
            { "QuickOverlayPage", QuickOverlayPage },
            { "QuickSuspenderPage", QuickSuspenderPage }
        };
        NotifyPropertyChanged(nameof(activeTabs));
    }

    public event PropertyChangedEventHandler PropertyChanged;

    private void NotifyPropertyChanged(string propertyName = "")
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        foreach (var activeTab in activeTabs)
        {
            if (activeTab.Value)
                tabButtons[activeTab.Key].Style = Application.Current.FindResource("AccentButtonStyle") as Style;

            else
                tabButtons[activeTab.Key].Style = Application.Current.FindResource("DefaultButtonStyle") as Style;
        }
    }

    public void LoadPages_MVVM()
    {
        overlayPage = new QuickOverlayPage();
        performancePage = new QuickPerformancePage();

        _pages.Add("QuickOverlayPage", overlayPage);
        _pages.Add("QuickPerformancePage", performancePage);
    }

    public static OverlayQuickTools GetCurrent()
    {
        return currentWindow;
    }

    private void SettingsManager_SettingValueChanged(string name, object value)
    {
        string[] onScreenDisplayLevels = [
            Properties.Resources.OverlayPage_OverlayDisplayLevel_Disabled,
            Properties.Resources.OverlayPage_OverlayDisplayLevel_Minimal,
            Properties.Resources.OverlayPage_OverlayDisplayLevel_Extended,
            Properties.Resources.OverlayPage_OverlayDisplayLevel_Full,
            Properties.Resources.OverlayPage_OverlayDisplayLevel_Custom,
            Properties.Resources.OverlayPage_OverlayDisplayLevel_External,
        ];

        // UI thread
        Application.Current.Dispatcher.Invoke(() =>
        {
            switch (name)
            {
                case "QuickToolsLocation":
                    {
                        var QuickToolsLocation = Convert.ToInt32(value);
                        UpdateLocation(QuickToolsLocation);
                    }
                    break;
                case "QuickToolsAutoHide":
                    {
                        autoHide = Convert.ToBoolean(value);
                    }
                    break;
            }
        });
    }

    private void SystemManager_DisplaySettingsChanged(DesktopScreen desktopScreen, ScreenResolution resolution)
    {
        int QuickToolsLocation = SettingsManager.GetInt("QuickToolsLocation");
        UpdateLocation(QuickToolsLocation);
    }

    private void UpdateLocation(int quickToolsLocation)
    {
        // UI thread (async)
        Application.Current.Dispatcher.Invoke(() =>
        {
            switch (quickToolsLocation)
            {
                // top, left
                // bottom, left
                case 0:
                case 2:
                    this.SetWindowPosition(WindowPositions.Left, Screen.PrimaryScreen);
                    //Left += Margin.Left;
                    break;

                // top, right
                // bottom, right
                default:
                case 1:
                case 3:
                    this.SetWindowPosition(WindowPositions.Right, Screen.PrimaryScreen);
                    //Left -= Margin.Right;
                    break;
            }

            Top = 0;
            Height = MinHeight = MaxHeight = (int)(Screen.PrimaryScreen.WpfWorkingArea.Height/* - (8.0d * Margin.Top)*/);
            //Top = Margin.Top;
        });
    }

    private void PowerManager_PowerStatusChanged(PowerStatus status)
    {
        // UI thread (async)
        Application.Current.Dispatcher.Invoke(() =>
        {
            var BatteryLifePercent = (int)Math.Truncate(status.BatteryLifePercent * 100.0f);
            BatteryIndicatorPercentage.Text = $"{BatteryLifePercent}%";

            // get status key
            var keyStatus = string.Empty;
            switch (status.PowerLineStatus)
            {
                case PowerLineStatus.Online:
                    keyStatus = "Charging";
                    break;
                default:
                    {
                        var energy = SystemPowerManager.EnergySaverStatus;
                        switch (energy)
                        {
                            case EnergySaverStatus.On:
                                keyStatus = "Saver";
                                break;
                        }
                    }
                    break;
            }

            // get battery key
            var keyValue = (int)Math.Truncate(status.BatteryLifePercent * 10);

            // set key
            var key = $"Battery{keyStatus}{keyValue}";

            if (SystemManager.PowerStatusIcon.TryGetValue(key, out var glyph))
                BatteryIndicatorIcon.Glyph = glyph;

            if (status.BatteryLifeRemaining > 0)
            {
                var time = TimeSpan.FromSeconds(status.BatteryLifeRemaining);

                string remaining;
                if (status.BatteryLifeRemaining >= 3600)
                    remaining = $"{time.Hours}h {time.Minutes}m";
                else
                    remaining = $"{time.Minutes}m";

                BatteryIndicatorLifeRemaining.Text = $"({remaining})";
                BatteryIndicatorLifeRemaining.Visibility = Visibility.Visible;
            }
            else if (status.BatteryFullLifetime > 0)
            {
                var time = TimeSpan.FromSeconds(status.BatteryFullLifetime);

                string remaining;
                if (status.BatteryFullLifetime >= 3600)
                    remaining = $"{time.Hours}h {time.Minutes}m";
                else
                    remaining = $"{time.Minutes}m";

                BatteryIndicatorLifeRemaining.Text = $"({remaining})";
                BatteryIndicatorLifeRemaining.Visibility = Visibility.Visible;
            }
            else
            {
                BatteryIndicatorLifeRemaining.Text = string.Empty;
                BatteryIndicatorLifeRemaining.Visibility = Visibility.Collapsed;
            }
        });
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        // load gamepad navigation maanger
        gamepadFocusManager = new(this, ContentFrame);
        if (PresentationSource.FromVisual(this) is HwndSource hwndSource)
        {
            hwndSource.AddHook(WndProc);
            hwndSource.CompositionTarget.RenderMode = RenderMode.Default;
            WinAPI.SetWindowPos(hwndSource.Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE);
            int QuickToolsLocation = SettingsManager.GetInt("QuickToolsLocation");
            UpdateLocation(QuickToolsLocation);
        }
    }

    private IntPtr prevWParam = new(0x0000000000000086);
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case WM_SYSCOMMAND:
                {
                    var command = wParam.ToInt32() & 0xfff0;
                    if (command == SC_MOVE) handled = true;
                }
                break;

            case WM_SETFOCUS:
                {
                    if (hwndSource != null)
                        WinAPI.SetWindowPos(hwndSource.Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE);
                    handled = true;
                }
                break;

            case WM_NCACTIVATE:
                {
                    // prevent window from loosing its fancy style
                    if (wParam == 0 && (lParam == 0))
                    {
                        if (prevWParam != new IntPtr(0x0000000000000086))
                            if (autoHide && Visibility == Visibility.Visible)
                                ToggleVisibility();
                        handled = true;
                    }

                    if (wParam == 1)
                    {
                        handled = true;
                    }

                    prevWParam = wParam;
                }
                break;

            case WM_ACTIVATEAPP:
                {
                    if (wParam == 0)
                    {
                        if (hwndSource != null)
                            WPFUtils.SendMessage(hwndSource.Handle, WM_NCACTIVATE, WM_NCACTIVATE, 0);
                    }
                }
                break;

            case WM_ACTIVATE:
                {
                    // WA_INACTIVE
                    if (wParam == 0)
                        handled = true;
                }
                break;

            case WM_MOUSEACTIVATE:
                {
                    handled = true;
                }
                break;

            case WM_PAINT:
                {
                    // Loop through all visual elements in the window
                    foreach (var element in WPFUtils.FindVisualChildren<UIElement>(this))
                    {
                        if (element.CacheMode is not null)
                        {
                            // Store the previous CacheMode value
                            cacheModes[element] = element.CacheMode.Clone();

                            // Set the CacheMode to null
                            element.CacheMode = null;
                        }
                    }

                    WM_PAINT_TIMER.Stop();
                    WM_PAINT_TIMER.Start();
                }
                break;
        }

        return IntPtr.Zero;
    }

    private void WM_PAINT_TIMER_Tick(object? sender, EventArgs e)
    {
        // UI thread
        Application.Current.Dispatcher.Invoke(() =>
        {
            // Set the CacheMode back to the previous value
            foreach (UIElement element in cacheModes.Keys)
                element.CacheMode = cacheModes[element];
        });
    }

    private void HandleEsc(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            ToggleVisibility();
    }

    public void ToggleVisibility()
    {
        // UI thread
        Application.Current.Dispatcher.Invoke(() =>
        {
            switch (Visibility)
            {
                case Visibility.Collapsed:
                case Visibility.Hidden:
                    Show();
                    Focus();

                    if (hwndSource != null)
                        WPFUtils.SendMessage(hwndSource.Handle, WM_NCACTIVATE, WM_NCACTIVATE, 0);

                    InvokeGotGamepadWindowFocus();

                    clockUpdateTimer.Start();
                    break;
                case Visibility.Visible:
                    Hide();

                    InvokeLostGamepadWindowFocus();

                    clockUpdateTimer.Stop();
                    break;
            }
        });
    }

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        e.Cancel = !isClosing;

        if (!isClosing)
            ToggleVisibility();
        else
        {
            // close pages
            devicePage.Close();
        }
    }

    public void Close(bool v)
    {
        isClosing = v;
        Close();
    }

    #region navView

    private void navView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.InvokedItemContainer is not null)
        {
            var navItem = (NavigationViewItem)args.InvokedItemContainer;
            var navItemTag = (string)navItem.Tag;

            switch (navItemTag)
            {
                default:
                    preNavItemTag = navItemTag;
                    break;
                case "shortcutKeyboard":
                case "shortcutDesktop":
                case "shortcutESC":
                case "shortcutExpand":
                    HotkeysManager.TriggerRaised(navItemTag, null, 0, false, true);
                    break;
            }

            NavView_Navigate(preNavItemTag);
        }
    }

    public void NavView_Navigate(string navItemTag)
    {
        var item = _pages.FirstOrDefault(p => p.Key.Equals(navItemTag));
        var _page = item.Value;

        // Get the page type before navigation so you can prevent duplicate
        // entries in the backstack.
        var preNavPageType = ContentFrame.CurrentSourcePageType;
        //var preNavPageType = ContentFrame.Content?.GetType();

        // Only navigate if the selected page isn't currently loaded.
        if (_page is not null && !Equals(preNavPageType, _page)) NavView_Navigate(_page);
    }

    public void NavView_Navigate(Page _page)
    {
        ContentFrame.Navigate(_page);
    }

    private void navView_Loaded(object sender, RoutedEventArgs e)
    {
        // Add handler for ContentFrame navigation.
        ContentFrame.Navigated += On_Navigated;
        // NavView doesn't load any page by default, so load home page.
        navView.SelectedItem = navView.FooterMenuItems[0];

        // If navigation occurs on SelectionChanged, this isn't needed.
        // Because we use ItemInvoked to navigate, we need to call Navigate
        // here to load the home page.
        preNavItemTag = "QuickHomePage";
        NavView_Navigate(preNavItemTag);
    }

    private void navView_BackRequested(NavigationView sender, NavigationViewBackRequestedEventArgs args)
    {
        TryGoBack();
    }

    private bool TryGoBack()
    {
        if (!ContentFrame.CanGoBack)
            return false;

        // Don't go back if the nav pane is overlayed.
        if (navView.IsPaneOpen &&
            (navView.DisplayMode == NavigationViewDisplayMode.Compact ||
             navView.DisplayMode == NavigationViewDisplayMode.Minimal))
            return false;

        ContentFrame.GoBack();
        return true;
    }

    private void On_Navigated(object sender, NavigationEventArgs e)
    {
        navView.IsBackEnabled = ContentFrame.CanGoBack;
        if (ContentFrame.SourcePageType is not null)
        {
            var preNavPageType = ContentFrame.CurrentSourcePageType;
            var preNavPageName = preNavPageType.Name;

            var NavViewItem = navView.FooterMenuItems
                .OfType<NavigationViewItem>().FirstOrDefault(n => n.Tag.Equals(preNavPageName));

            if (NavViewItem is not null)
                navView.SelectedItem = NavViewItem;
            navHeader.Text = ((Page)((ContentControl)sender).Content).Title;
        }
    }

    private void UpdateTime(object? sender, EventArgs e)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            Time.Text = $"{DateTime.Now.ToString(CultureInfo.InstalledUICulture.DateTimeFormat.ShortTimePattern).ToLowerInvariant()}";
        });
    }

    #endregion


    private void SystemManager_Initialized()
    {
        if (MultimediaManager.HasBrightnessSupport())
        {
            lock (brightnessLock)
            {
                // UI thread
                Application.Current.Dispatcher.Invoke(() =>
                {
                    SliderBrightness.IsEnabled = true;
                    SliderBrightness.Value = MultimediaManager.GetBrightness();
                });
            }
        }

        if (MultimediaManager.HasVolumeSupport())
        {
            lock (volumeLock)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    SliderVolume.IsEnabled = true;
                    SliderVolume.Value = Math.Round(MultimediaManager.GetVolume());
                    UpdateVolumeIcon((float)SliderVolume.Value, MultimediaManager.GetMute());
                });
            }
        }
    }

    private void SystemManager_BrightnessNotification(int brightness)
    {
        if (brightnessLock.TryEnter())
        {
            try
            {
                // UI thread
                Application.Current.Dispatcher.Invoke(() =>
                {
                    SliderBrightness.Value = brightness;
                });
            }
            finally
            {
                brightnessLock.Exit();
            }
        }
    }

    private void SystemManager_VolumeNotification(float volume)
    {
        if (volumeLock.TryEnter())
        {
            try
            {
                // UI thread
                var isMute = MultimediaManager.GetMute();
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
    }

    private void SliderBrightness_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded)
            return;

        // prevent update loop
        if (brightnessLock.IsEntered())
            return;

        MultimediaManager.SetBrightness(SliderBrightness.Value);
    }

    private void SliderVolume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded)
            return;

        // prevent update loop
        if (volumeLock.IsEntered())
            return;

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
                var isMute = MultimediaManager.ToggleMute();
                Application.Current.Dispatcher.Invoke(() =>
                {
                    UpdateVolumeIcon(float.NaN, isMute);
                });
            }
            finally
            {
                volumeLock.Exit();
            }
        }

    }

    private void QuickButton_Click(object sender, RoutedEventArgs e)
    {
        var button = (Button)sender;
        if (tabButtons.ContainsKey($"{button.Name}"))
        {
            tabButtons[$"{button.Name}"] = button;
            activeTabs[$"{button.Name}"] = true;
            activeTabs.Where(x => x.Key != $"{button.Name}").ToList().ForEach(x => activeTabs[x.Key] = false);
        }
        else
        {
            tabButtons.Add($"{button.Name}", button);
        }
        NotifyPropertyChanged(nameof(activeTabs));

        MainWindow.overlayquickTools.NavView_Navigate(button.Name);
    }

    private void ProfileManager_Applied(Profile profile, UpdateSource source)
    {
        // UI thread (async)
        Application.Current.Dispatcher.Invoke(() =>
        {
            t_CurrentProfile.Text = profile.ToString();
        });
    }

    private void GamepadWindow_Deactivated(object sender, EventArgs e)
    {
        Window window = (Window)sender;
        if (PresentationSource.FromVisual(window) is HwndSource hwndSource)
        {
            hwndSource.AddHook(WndProc);
            hwndSource.CompositionTarget.RenderMode = RenderMode.Default;
            WinAPI.SetWindowPos(hwndSource.Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE);
        }
    }

    private void GamepadWindow_PreviewLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        Window window = (Window)sender;
        if (PresentationSource.FromVisual(window) is HwndSource hwndSource)
        {
            hwndSource.AddHook(WndProc);
            hwndSource.CompositionTarget.RenderMode = RenderMode.Default;
            WinAPI.SetWindowPos(hwndSource.Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE);
        }
    }
}