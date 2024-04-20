using HandheldCompanion.Managers;
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
using static System.Windows.Forms.VisualStyles.VisualStyleElement.TaskbarClock;
using Application = System.Windows.Application;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Page = System.Windows.Controls.Page;
using Button = System.Windows.Controls.Button;
using PowerLineStatus = System.Windows.Forms.PowerLineStatus;
using Screen = WpfScreenHelper.Screen;
using SystemManager = HandheldCompanion.Managers.SystemManager;
using SystemPowerManager = Windows.System.Power.PowerManager;
using Timer = System.Timers.Timer;

namespace HandheldCompanion.Views.Windows;

/// <summary>
///     Interaction logic for QuickTools.xaml
/// </summary>
public partial class OverlayQuickTools : GamepadWindow
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


    private LockObject brightnessLock = new();
    private LockObject volumeLock = new();

    private Dictionary<string, System.Windows.Controls.Button> tabButtons = new();

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

        MultimediaManager.DisplaySettingsChanged += SystemManager_DisplaySettingsChanged;

        ProfileManager.Applied += ProfileManager_Applied;
        SettingsManager.SettingValueChanged += SettingsManager_SettingValueChanged;

        // create pages
        homePage = new("quickhome");
        devicePage = new("quickdevice");
        performancePage = new("quickperformance");
        profilesPage = new("quickprofiles");
        overlayPage = new("quickoverlay");
        suspenderPage = new("quicksuspender");

        _pages.Add("QuickHomePage", homePage);
        _pages.Add("QuickDevicePage", devicePage);
        _pages.Add("QuickPerformancePage", performancePage);
        _pages.Add("QuickProfilesPage", profilesPage);
        _pages.Add("QuickOverlayPage", overlayPage);
        _pages.Add("QuickSuspenderPage", suspenderPage);
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

        // UI thread (async)
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            switch (name)
            {
                case "OnScreenDisplayLevel":
                    {
                        var overlayLevel = Convert.ToInt16(value);
                        t_CurrentOverlayLevel.Text = onScreenDisplayLevels[overlayLevel];

                    }
                    break;
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

    private void UpdateLocation(int QuickToolsLocation)
    {
        // UI thread (async)
        Application.Current.Dispatcher.Invoke(() =>
        {
            switch (QuickToolsLocation)
            {
                // top, left
                // bottom, left
                case 0:
                case 2:
                    this.SetWindowPosition(WindowPositions.Left, Screen.PrimaryScreen);
                    Left += Margin.Left;
                    break;

                // top, right
                // bottom, right
                default:
                case 1:
                case 3:
                    this.SetWindowPosition(WindowPositions.Right, Screen.PrimaryScreen);
                    Left -= Margin.Right;
                    break;
            }
            Width = MinWidth = MaxWidth = (int)(Screen.PrimaryScreen.WpfBounds.Width / 2.5);
            Height = MinHeight = MaxHeight = (int)Screen.PrimaryScreen.WpfBounds.Height - (6.0d * Margin.Top);
            //Height = MinHeight = MaxHeight = (int)(Screen.PrimaryScreen.WpfBounds.Height - (6.0d * Margin.Top));
            Top = Margin.Top;
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
            var KeyStatus = string.Empty;
            switch (status.PowerLineStatus)
            {
                case PowerLineStatus.Online:
                    KeyStatus = "Charging";
                    break;
                default:
                    {
                        var energy = SystemPowerManager.EnergySaverStatus;
                        switch (energy)
                        {
                            case EnergySaverStatus.On:
                                KeyStatus = "Saver";
                                break;
                        }
                    }
                    break;
            }

            // get battery key
            var keyValue = (int)Math.Truncate(status.BatteryLifePercent * 10);

            // set key
            var Key = $"Battery{KeyStatus}{keyValue}";

            if (SystemManager.PowerStatusIcon.TryGetValue(Key, out var glyph))
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
        hwndSource = PresentationSource.FromVisual(this) as HwndSource;
        if (hwndSource != null)
        {
            hwndSource.AddHook(WndProc);
            hwndSource.CompositionTarget.RenderMode = RenderMode.SoftwareOnly;
            WinAPI.SetWindowPos(hwndSource.Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE);
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
        //navView.SelectedItem = navView.MenuItems[0];

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
        navHeader.Text = ((Page)((ContentControl)sender).Content).Title;
    }

    private void UpdateTime(object? sender, EventArgs e)
    {
        var timeFormat = CultureInfo.InstalledUICulture.DateTimeFormat.ShortTimePattern;
        PlatformManager.HWiNFO.ReaffirmRunningProcess();
        Time.Text = string.Empty;
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            if (PlatformManager.HWiNFO.MonitoredSensors.TryGetValue(Platforms.HWiNFO.SensorElementType.CPUUsage, out var cpuUsage) &&
                PlatformManager.HWiNFO.MonitoredSensors.TryGetValue(Platforms.HWiNFO.SensorElementType.CPUTemperature, out var cpuTemp) &&
                PlatformManager.HWiNFO.MonitoredSensors.TryGetValue(Platforms.HWiNFO.SensorElementType.CPUPower, out var cpuPower) &&
                PlatformManager.HWiNFO.MonitoredSensors.TryGetValue(Platforms.HWiNFO.SensorElementType.PhysicalMemoryUsage, out var cpuMem) &&
                !float.IsNaN(PlatformManager.HWiNFO.CPUFanSpeed))
            {
                CPUUsage.Text = $"{cpuUsage.Value:0}{cpuUsage.Unit}";
                CPUTemp.Text = $"{cpuTemp.Value:0}{cpuTemp.Unit}";
                CPUPower.Text = $"{cpuPower.Value:0}{cpuPower.Unit}";
                CPUMem.Text = $"{(cpuMem.Value / 1024):0.0}GB";
                CPUFan.Text = $"{PlatformManager.HWiNFO.CPUFanSpeed}rpm";
            }

            if (PlatformManager.HWiNFO.MonitoredSensors.TryGetValue(Platforms.HWiNFO.SensorElementType.GPUUsage, out var gpuUsage) &&
                PlatformManager.HWiNFO.MonitoredSensors.TryGetValue(Platforms.HWiNFO.SensorElementType.GPUTemperature, out var gpuTemp) &&
                PlatformManager.HWiNFO.MonitoredSensors.TryGetValue(Platforms.HWiNFO.SensorElementType.GPUPower, out var gpuPower) &&
                PlatformManager.HWiNFO.MonitoredSensors.TryGetValue(Platforms.HWiNFO.SensorElementType.GPUMemoryUsage, out var gpuMem))
            {
                GPUUsage.Text = $"{gpuUsage.Value:0}{gpuUsage.Unit}";
                GPUTemp.Text = $"{gpuTemp.Value:0}{gpuTemp.Unit}";
                GPUPower.Text = $"{gpuPower.Value:0}{gpuPower.Unit}";
                GPUMem.Text = $"{(gpuMem.Value / 1024):0.0}GB";
            }
            Time.Text = $"{DateTime.Now.ToString(timeFormat)}";
        });
    }

    #endregion


    private void SystemManager_Initialized()
    {
        // UI thread (async)
        Application.Current.Dispatcher.Invoke(() =>
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
        using (new ScopedLock(brightnessLock))
        {
            // UI thread
            Application.Current.Dispatcher.Invoke(() =>
            {
                SliderBrightness.Value = brightness;
            });
        }
    }

    private void SystemManager_VolumeNotification(float volume)
    {
        // UI thread
        using (new ScopedLock(volumeLock))
        {
            // UI thread
            Application.Current.Dispatcher.Invoke(() =>
            {
                UpdateVolumeIcon(volume);
                SliderVolume.Value = Math.Round(volume);
            });
        }
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

    private void QuickButton_Click(object sender, RoutedEventArgs e)
    {
        var button = (Button)sender;
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
}