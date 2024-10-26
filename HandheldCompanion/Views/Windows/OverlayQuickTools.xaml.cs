using HandheldCompanion.Controllers;
using HandheldCompanion.Devices;
using HandheldCompanion.Inputs;
using HandheldCompanion.Managers;
using HandheldCompanion.Misc;
using HandheldCompanion.Utils;
using HandheldCompanion.Views.Classes;
using HandheldCompanion.Views.QuickPages;
using iNKORE.UI.WPF.Modern.Controls;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Navigation;
using System.Windows.Threading;
using Windows.Devices.Radios;
using Windows.Devices.WiFi;
using Windows.System.Power;
using WindowsDisplayAPI;
using WpfScreenHelper;
using WpfScreenHelper.Enum;
using Application = System.Windows.Application;
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
public partial class OverlayQuickTools : GamepadWindow
{
    private const int SC_MOVE = 0xF010;
    private readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

    private const int SWP_NOSIZE = 0x0001;
    private const int SWP_NOMOVE = 0x0002;
    private const int SWP_NOACTIVATE = 0x0010;
    private const int SWP_NOZORDER = 0x0004;

    // Define the Win32 API constants and functions
    private const int WM_PAINT = 0x000F;
    private const int WM_ACTIVATEAPP = 0x001C;
    private const int WM_ACTIVATE = 0x0006;
    private const int WM_SETFOCUS = 0x0007;
    private const int WM_KILLFOCUS = 0x0008;
    private const int WM_NCACTIVATE = 0x0086;
    private const int WM_SYSCOMMAND = 0x0112;
    private const int WM_WINDOWPOSCHANGING = 0x0046;
    private const int WM_SHOWWINDOW = 0x0018;
    private const int WM_MOUSEACTIVATE = 0x0021;
    private const int MA_NOACTIVATE = 0x0003;
    private const int WM_NCHITTEST = 0x0084;
    private const int HTCAPTION = 0x02;
    private const int MA_NOACTIVATEANDEAT = 4;

    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_SIZEBOX = 0x00040000;

    private const int GWL_STYLE = -16;
    private const int GWL_EXSTYLE = -20;

    private CrossThreadLock Sliding = new();
    // page vars
    private readonly Dictionary<string, Page> _pages = [];

    private bool autoHide;
    private bool isClosing;
    private readonly DispatcherTimer clockUpdateTimer = new(
        DispatcherPriority.Normal,
        Dispatcher.CurrentDispatcher
        )
    {
        IsEnabled = false,
        Interval = TimeSpan.FromMilliseconds(1000)
    };

    public QuickHomePage homePage;
    public QuickDevicePage devicePage;
    public QuickPerformancePage performancePage;
    public QuickProfilesPage profilesPage;
    public QuickOverlayPage overlayPage;
    public QuickApplicationsPage applicationsPage;

    private static OverlayQuickTools currentWindow;
    private string preNavItemTag;

    public OverlayQuickTools()
    {
        InitializeComponent();
        currentWindow = this;

        // used by gamepad navigation
        Tag = "QuickTools";

        PreviewKeyDown += HandleEsc;

        clockUpdateTimer.Tick += UpdateTime;

        WMPaintTimer.Elapsed += WMPaintTimer_Elapsed;

        // create manager(s)
        SystemManager.PowerStatusChanged += PowerManager_PowerStatusChanged;
        MultimediaManager.DisplaySettingsChanged += SystemManager_DisplaySettingsChanged;
        SettingsManager.SettingValueChanged += SettingsManager_SettingValueChanged;
        ControllerManager.ControllerSelected += ControllerManager_ControllerSelected;
        CPUName.Text = IDevice.GetCurrent().Processor.Split("w/").First();
        GPUName.Text = IDevice.GetCurrent().GraphicName;

        // create pages
        homePage = new("quickhome");
        devicePage = new("quickdevice");
        profilesPage = new("quickprofiles");
        applicationsPage = new("quickapplications");

        _pages.Add("QuickHomePage", homePage);
        _pages.Add("QuickDevicePage", devicePage);
        _pages.Add("QuickProfilesPage", profilesPage);
        _pages.Add("QuickApplicationsPage", applicationsPage);

        // load gamepad navigation manager
        gamepadFocusManager = new(this, ContentFrame);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        hwndSource.AddHook(WndProc);

        int exStyle = WinAPI.GetWindowLong(hwndSource.Handle, GWL_EXSTYLE);
        exStyle |= WS_EX_NOACTIVATE;
        WinAPI.SetWindowLong(hwndSource.Handle, GWL_EXSTYLE, exStyle);

        /*
        int Style = WinAPI.GetWindowLong(hwndSource.Handle, GWL_STYLE);
        exStyle &= ~WS_SIZEBOX;
        WinAPI.SetWindowLong(hwndSource.Handle, GWL_STYLE, Style);
        */

        WinAPI.SetWindowPos(hwndSource.Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOACTIVATE);
    }

    public void LoadPages_MVVM()
    {
        //overlayPage = new QuickOverlayPage();
        performancePage = new QuickPerformancePage();

        //_pages.Add("QuickOverlayPage", overlayPage);
        _pages.Add("QuickPerformancePage", performancePage);
    }

    public static OverlayQuickTools GetCurrent()
    {
        return currentWindow;
    }

    private void SettingsManager_SettingValueChanged(string name, object value)
    {
        // UI thread
        Application.Current.Dispatcher.Invoke(() =>
        {
            switch (name)
            {
                case "QuickToolsLocation":
                    UpdateLocation();
                    break;
                case "QuickToolsAutoHide":
                    autoHide = Convert.ToBoolean(value);
                    break;
                case "QuickToolsDevicePath":
                    UpdateLocation();
                    break;
            }
        });
    }

    private void ControllerManager_ControllerSelected(IController Controller)
    {
        // UI thread (async)
        Application.Current.Dispatcher.Invoke(() =>
        {
            QTLB.Glyph = Controller.GetGlyph(ButtonFlags.L1);
            QTRB.Glyph = Controller.GetGlyph(ButtonFlags.R1);
        });
    }

    private void SystemManager_DisplaySettingsChanged(Display desktopScreen)
    {
        // ignore if we're not ready yet
        if (!MultimediaManager.IsInitialized) return;

        UpdateLocation();
    }

    private const double _Margin = 12;
    private const double _MaxHeight = 960;
    private const double _MaxWidth = 960;
    private const WindowStyle _Style = WindowStyle.ToolWindow;
    private double _Top = 0;
    private double _Left = 0;
    private Screen targetScreen;

    private void UpdateLocation()
    {
        // pull quicktools settings
        var QuickToolsLocation = SettingsManager.Get<int>("QuickToolsLocation");
        string DevicePath = SettingsManager.Get<string>("QuickToolsDevicePath");
        string DeviceName = SettingsManager.Get<string>("QuickToolsDeviceName");

        // Attempt to find the screen with the specified friendly name
        var targetDisplay = ScreenControl.AllDisplays.FirstOrDefault(display =>
                                display.DevicePath.Equals(DevicePath) ||
                                display.ToPathDisplayTarget().FriendlyName.Equals(DeviceName)) ?? ScreenControl.PrimaryDisplay;

        // Find the corresponding Screen object
        targetScreen = targetDisplay is null
            ? Screen.PrimaryScreen
            : Screen.AllScreens.FirstOrDefault(screen => screen.DeviceName.Equals(targetDisplay.DeviceName, StringComparison.OrdinalIgnoreCase)) ?? Screen.PrimaryScreen;

        // UI thread
        Application.Current.Dispatcher.Invoke(() =>
        {
            // Common settings across cases 0 and 1
            MaxWidth = (int)Math.Min(_MaxWidth, targetScreen.WpfWorkingArea.Width);
            Width = (int)Math.Max(MinWidth, SettingsManager.Get<double>("QuickToolsWidth"));
            MaxHeight = Math.Min(targetScreen.WpfWorkingArea.Height - (_Margin * 0), _MaxHeight);
            Height = MinHeight = MaxHeight;
            WindowStyle = _Style;

            switch (QuickToolsLocation)
            {
                case 2: // Maximized
                    MaxWidth = double.PositiveInfinity;
                    MaxHeight = double.PositiveInfinity;
                    WindowStyle = WindowStyle.None;
                    break;
            }
        });

        switch (QuickToolsLocation)
        {
            case 0: // Left
                this.SetWindowPosition(WindowPositions.BottomLeft, targetScreen);
                break;

            case 1: // Right
                this.SetWindowPosition(WindowPositions.BottomRight, targetScreen);
                break;

            case 2: // Maximized
                this.SetWindowPosition(WindowPositions.Maximize, targetScreen);
                break;
        }

        // UI thread
        Application.Current.Dispatcher.Invoke(() =>
        {
            switch (QuickToolsLocation)
            {
                case 0: // Left
                    Top = _Margin * 0;
                    Left += _Margin * 0;
                    break;

                case 1: // Right
                    Top = _Margin * 0;
                    Left -= _Margin * 0;
                    break;
            }
        });

        // used by SlideIn/SlideOut
        _Top = Top;
        _Left = Left;
    }

    private void PowerManager_PowerStatusChanged(PowerStatus status)
    {
        // UI thread (async)
        lastBatteryRefresh = 0;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        // do something
    }

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    private const int WA_ACTIVE = 1;
    private const int WA_CLICKACTIVE = 2;
    private const int WA_INACTIVE = 0;
    private static readonly IntPtr HWND_TOP = new IntPtr(0);
    private const uint SWP_FRAMECHANGED = 0x0020;

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

            case WM_ACTIVATE:
                handled = true;
                WPFUtils.SendMessage(hwndSource.Handle, WM_NCACTIVATE, WM_NCACTIVATE, 0);
                break;

            case WM_PAINT:
                {
                    if (Sliding.IsEntered())
                        break;

                    DateTime drawTime = DateTime.Now;

                    double drawDiff = Math.Abs((prevDraw - drawTime).TotalMilliseconds);
                    if (drawDiff < 200)
                    {
                        if (!WMPaintPending)
                        {
                            // disable GPU acceleration
                            RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;

                            // set flag
                            WMPaintPending = true;

                            LogManager.LogError("ProcessRenderMode set to {0}", RenderOptions.ProcessRenderMode);
                        }
                    }

                    // update previous drawing time
                    prevDraw = drawTime;

                    if (WMPaintPending)
                    {
                        WMPaintTimer.Stop();
                        WMPaintTimer.Start();
                    }
                }
                break;
        }

        return IntPtr.Zero;
    }

    DateTime prevDraw = DateTime.MinValue;

    private void WMPaintTimer_Elapsed(object? sender, System.Timers.ElapsedEventArgs e)
    {
        if (WMPaintPending)
        {
            // enable GPU acceleration
            RenderOptions.ProcessRenderMode = RenderMode.Default;

            // reset flag
            WMPaintPending = false;

            LogManager.LogError("ProcessRenderMode set to {0}", RenderOptions.ProcessRenderMode);
        }
    }

    private Timer WMPaintTimer = new(100) { AutoReset = false };
    private bool WMPaintPending = false;

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
                    try { Show(); } catch { /* ItemsRepeater might have a NaN DesiredSize */ }
                    break;
                case Visibility.Visible:
                    try { Hide(); } catch { /* ItemsRepeater might have a NaN DesiredSize */ }
                    break;
            }
        });
    }

    private void SlideIn()
    {
        // set lock
        if (Sliding.TryEnter())
        {
            DoubleAnimation animation = new DoubleAnimation
            {
                From = targetScreen.WpfBounds.Height,
                To = _Top,
                Duration = TimeSpan.FromSeconds(0.17),
                AccelerationRatio = 0.25,
                DecelerationRatio = 0.75,
            };

            animation.Completed += (s, e) =>
            {
                // release lock
                Sliding.Exit();
            };

            this.BeginAnimation(Window.TopProperty, animation);
        }
    }

    private void SlideOut()
    {
        // set lock
        if (Sliding.TryEnter())
        {
            DoubleAnimation animation = new DoubleAnimation
            {
                From = _Top,
                To = targetScreen.WpfBounds.Height,
                Duration = TimeSpan.FromSeconds(0.17),
                AccelerationRatio = 0.75,
                DecelerationRatio = 0.25,
            };

            animation.Completed += (s, e) =>
            {
                this.Hide();

                // release lock
                Sliding.Exit();
            };

            this.BeginAnimation(Window.TopProperty, animation);
        }
    }

    private void GamepadWindow_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        switch (Visibility)
        {
            case Visibility.Collapsed:
            case Visibility.Hidden:
                InvokeLostGamepadWindowFocus();
                clockUpdateTimer.Stop();
                break;
            case Visibility.Visible:
                WPFUtils.SendMessage(hwndSource.Handle, WM_NCACTIVATE, WM_NCACTIVATE, 0);
                InvokeGotGamepadWindowFocus();
                clockUpdateTimer.Start();
                UpdateTime(sender, EventArgs.Empty);
                break;
        }
    }

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        // position and size settings
        SettingsManager.Set("QuickToolsWidth", ActualWidth);

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
        navView.SelectedItem = navView.MenuItems[1];

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
        // navHeader.Text = ((Page)((ContentControl)sender).Content).Title;
    }

    long lastRefresh;
    long lastBatteryRefresh;
    long lastRadioRefresh;

    private void UpdateTime(object? sender, EventArgs e)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            Time.Text = $"{DateTime.Now.ToString(CultureInfo.InstalledUICulture.DateTimeFormat.ShortTimePattern).ToLowerInvariant()}";
            ShowBattery();
            ShowPerformance();
            ShowRadios();
        });
    }

    private void ShowPerformance()
    {
        if (Math.Abs(DateTimeOffset.Now.ToUnixTimeMilliseconds() - lastRefresh) < 2000) return;
        lastRefresh = DateTimeOffset.Now.ToUnixTimeMilliseconds();

        if (PlatformManager.LibreHardwareMonitor.CPUPower != null)
            CPUPower.Text = $"{Math.Round((decimal)PlatformManager.LibreHardwareMonitor.CPUPower.Value)}W";

        if (PlatformManager.LibreHardwareMonitor.CPUTemp != null)
            CPUTemp.Text = $"{Math.Round((decimal)PlatformManager.LibreHardwareMonitor.CPUTemp.Value)}°C";

        if (PlatformManager.LibreHardwareMonitor.CPULoad != null)
        {
            CPULoad.Text = $"{Math.Round((decimal)PlatformManager.LibreHardwareMonitor.CPULoad.Value)}%";
            CPULoadRing.Value = (double)Math.Round((decimal)PlatformManager.LibreHardwareMonitor.CPULoad.Value, 1);
        }

        if (PlatformManager.LibreHardwareMonitor.MemoryUsage != null)
        {
            CPUMemory.Text = $"{Math.Round((decimal)PlatformManager.LibreHardwareMonitor.MemoryUsage.Value / 1024, 1)}GB";
        }

        if (PlatformManager.LibreHardwareMonitor.GPUPower != null)
            GPUPower.Text = $"{Math.Round((decimal)PlatformManager.LibreHardwareMonitor.GPUPower.Value)}W";

        if (PlatformManager.LibreHardwareMonitor.GPUTemp != null)
            GPUTemp.Text = $"{Math.Round((decimal)PlatformManager.LibreHardwareMonitor.GPUTemp.Value)}°C";

        if (PlatformManager.LibreHardwareMonitor.GPULoad != null)
        {
            GPULoad.Text = $"{Math.Round((decimal)PlatformManager.LibreHardwareMonitor.GPULoad.Value)}%";
            GPULoadRing.Value = (double)Math.Round((decimal)PlatformManager.LibreHardwareMonitor.GPULoad.Value, 1);
        }

        if (PlatformManager.LibreHardwareMonitor.GPUMemoryUsage != null)
        {
            GPUMemory.Text = $"{Math.Round((decimal)PlatformManager.LibreHardwareMonitor.GPUMemoryUsage.Value / 1024, 1)}GB";
        }
    }


    private IReadOnlyList<Radio> radios;

    private void ShowRadios()
    {
        if (Math.Abs(DateTimeOffset.Now.ToUnixTimeMilliseconds() - lastRadioRefresh) < 20_000) return;
        lastRadioRefresh = DateTimeOffset.Now.ToUnixTimeMilliseconds();

        Task.Run(async () =>
        {
            // Get the Bluetooth radio
            radios = await Radio.GetRadiosAsync();
            // UI thread
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (radios is null)
                {
                    return;
                }

                Radio? wifiRadio = radios.FirstOrDefault(radio => radio.Kind == RadioKind.WiFi);
                Radio? bluetoothRadio = radios.FirstOrDefault(radio => radio.Kind == RadioKind.Bluetooth);
                if (bluetoothRadio is not null)
                {
                    BluetoothIcon.Visibility = Visibility.Visible;
                    BluetoothIcon.Glyph = "\uec41";
                }
                else
                    BluetoothIcon.Visibility = Visibility.Hidden;
                if (wifiRadio is not null && wifiRadio.State == RadioState.On)
                {
                    var wifiAdapters = WiFiAdapter.FindAllAdaptersAsync().GetAwaiter().GetResult();
                    foreach (var adapter in wifiAdapters)
                    {
                        foreach (var network in adapter.NetworkReport.AvailableNetworks)
                        {
                            WifiIcon.Glyph = network.SignalBars switch
                            {
                                1 => "\uec3c",
                                2 => "\uec3d",
                                3 => "\uec3e",
                                4 or 5 => "\uec3f",
                                _ => "\uf384"
                            };
                            break;
                        }

                    }
                }
                else
                {
                    var isEthernet = false;
                    var networkInterfaces = NetworkInterface.GetAllNetworkInterfaces();
                    foreach (NetworkInterface networkInterface in networkInterfaces)
                    {
                        if (networkInterface.NetworkInterfaceType == NetworkInterfaceType.Ethernet &&
                            networkInterface.OperationalStatus == OperationalStatus.Up)
                        {
                            WifiIcon.Glyph = "\ue839";
                            isEthernet = true;
                            break;
                        }
                    }

                    if (!isEthernet)
                    {
                        WifiIcon.Glyph = "\uf384";
                    }


                }
            });
        });
    }

    private void ShowBattery()
    {
        if (Math.Abs(DateTimeOffset.Now.ToUnixTimeMilliseconds() - lastBatteryRefresh) < 5_000) return;
        lastBatteryRefresh = DateTimeOffset.Now.ToUnixTimeMilliseconds();
        if (PlatformManager.LibreHardwareMonitor.BatteryCapacity > 0)
        {
            BatteryIndicatorPercentage.Text = $"{Math.Round((decimal)PlatformManager.LibreHardwareMonitor.BatteryCapacity, 1)}%";
            // get status key
            var keyStatus = string.Empty;
            var powerStatus = System.Windows.Forms.SystemInformation.PowerStatus;
            switch (powerStatus.PowerLineStatus)
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
            var keyValue = (int)PlatformManager.LibreHardwareMonitor.BatteryCapacity / 10;

            // set key
            var key = $"Battery{keyStatus}{keyValue}";

            if (SystemManager.PowerStatusIcon.TryGetValue(key, out var glyph))
                BatteryIndicatorIcon.Glyph = glyph;
        }

        if (PlatformManager.LibreHardwareMonitor.BatteryPower != null &&
            PlatformManager.LibreHardwareMonitor.BatteryHealth != null &&
            PlatformManager.LibreHardwareMonitor.BatteryHealth != -1 &&
            PlatformManager.LibreHardwareMonitor.BatteryRemainingCapacity > 0)
        {
            if (SystemManager.PowerStatusIcon.TryGetValue($"VerticalBattery{(int)(PlatformManager.LibreHardwareMonitor.BatteryHealth / 10)}", out var glyphBatteryHealth))
                BatteryHealthIndicatorIcon.Glyph = glyphBatteryHealth;
            BatteryDesignCapacity.Text = $"{PlatformManager.LibreHardwareMonitor.BatteryRemainingCapacity}mWh";
            BatteryHealth.Text = $"{Math.Round((decimal)PlatformManager.LibreHardwareMonitor.BatteryHealth, 1)}%";
            BatteryHealthRing.Value = (double)Math.Round((decimal)PlatformManager.LibreHardwareMonitor.BatteryHealth, 1);
            BatteryPower.Text = Math.Round((decimal)PlatformManager.LibreHardwareMonitor.BatteryPower, 1).ToString() + "W";
        }

        if (PlatformManager.LibreHardwareMonitor.BatteryPower == 0)
        {
            BatteryIndicatorLifeRemaining.Visibility = Visibility.Collapsed;
            BatteryLifePanel.Visibility = Visibility.Collapsed;
        }
        else
        {
            BatteryIndicatorLifeRemaining.Visibility = Visibility.Visible;
            BatteryLifePanel.Visibility = Visibility.Visible;
            if (PlatformManager.LibreHardwareMonitor.BatteryPower > 0)
            {
                var time = PlatformManager.LibreHardwareMonitor.BatteryTimeSpan;
                string remaining;
                if (time.TotalSeconds >= 3600)
                    remaining = $"{time.Hours}h {time.Minutes}min";
                else
                    remaining = $"{time.Minutes}min";
                BatteryIndicatorLife.Text = $"{remaining}";
                BatteryIndicatorLifeRemaining.Text = " utill full";
            }
            else
            {
                var time = PlatformManager.LibreHardwareMonitor.BatteryTimeSpan;
                string remaining;
                if (time.TotalSeconds >= 3600)
                    remaining = $"{time.Hours}h {time.Minutes}min";
                else
                    remaining = $"{time.Minutes}min";
                BatteryIndicatorLife.Text = $"{remaining}";
                BatteryIndicatorLifeRemaining.Text = " remaining";
            }
        }
    }

    internal nint GetHandle()
    {
        return hwndSource.Handle;
    }

    #endregion

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