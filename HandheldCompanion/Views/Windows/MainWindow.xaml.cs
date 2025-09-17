using HandheldCompanion.Controllers;
using HandheldCompanion.Devices;
using HandheldCompanion.Helpers;
using HandheldCompanion.Inputs;
using HandheldCompanion.Managers;
using HandheldCompanion.Shared;
using HandheldCompanion.UI;
using HandheldCompanion.Utils;
using HandheldCompanion.Views.Classes;
using HandheldCompanion.Views.Pages;
using HandheldCompanion.Views.Windows;
using iNKORE.UI.WPF.Modern.Controls;
using Nefarius.Utilities.DeviceManagement.PnP;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Navigation;
using System.Windows.Shell;
using System.Windows.Threading;
using Windows.UI.ViewManagement;
using Application = System.Windows.Application;
using Control = System.Windows.Controls.Control;
using Frame = System.Windows.Controls.Frame;
using MessageBox = iNKORE.UI.WPF.Modern.Controls.MessageBox;
using MouseEventArgs = System.Windows.Forms.MouseEventArgs;
using Page = System.Windows.Controls.Page;
using RadioButton = System.Windows.Controls.RadioButton;

namespace HandheldCompanion.Views;

/// <summary>
///     Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : GamepadWindow
{
    // devices vars
    private static IDevice currentDevice;

    // page vars
    private static readonly Dictionary<string, Page> _pages = [];

    public static ControllerPage controllerPage;
    public static DevicePage devicePage;
    public static PerformancePage performancePage;
    public static ProfilesPage profilesPage;
    public static SettingsPage settingsPage;
    public static AboutPage aboutPage;
    public static OverlayPage overlayPage;
    public static HotkeysPage hotkeysPage;
    public static LayoutPage layoutPage;
    public static NotificationsPage notificationsPage;

    // overlay(s) vars
    public static OverlayModel overlayModel;
    public static OverlayTrackpad overlayTrackpad;
    public static OverlayToast overlayToast;
    public static OverlayQuickTools overlayquickTools;

    public static string CurrentExe, CurrentPath;

    private static MainWindow currentWindow;
    public static FileVersionInfo fileVersionInfo;

    public static string InstallPath = string.Empty;
    public static string SettingsPath = string.Empty;
    public static string CurrentPageName = string.Empty;

    private bool appClosing;
    private static NotifyIcon notifyIcon;
    private static ContextMenu notifyContextMenu;
    private bool notifyInTaskbar;
    private string preNavItemTag;
    private static DispatcherTimer sensorTimer = new(
        TimeSpan.FromMilliseconds(1000),
        DispatcherPriority.Normal,
        sensorTimer_Elapsed,
        Dispatcher.CurrentDispatcher)
    {
        IsEnabled = false
    };

    private WindowState prevWindowState;
    public static SplashScreen SplashScreen;

    public static UISettings uiSettings;

    private const int WM_QUERYENDSESSION = 0x0011;
    private const int WM_DISPLAYCHANGE = 0x007e;
    private const int WM_DEVICECHANGE = 0x0219;

    //TimeSpan.FromMilliseconds(SystemInformation.DoubleClickTime)
    private static readonly DispatcherTimer notifyIconWaitTimer = new(
        TimeSpan.FromMilliseconds(200),
        DispatcherPriority.Normal,
        notifyIconWaitTimerTicked,
        Dispatcher.CurrentDispatcher)
    {
        IsEnabled = false
    };

    private static void notifyIconWaitTimerTicked(object? sender, EventArgs e)
    {
        notifyIconWaitTimer.Stop();
        overlayquickTools.ToggleVisibility();
    }

    public MainWindow(FileVersionInfo _fileVersionInfo, Assembly CurrentAssembly)
    {
        // initialize splash screen
        SplashScreen = new SplashScreen();

        // get last version
        Version LastVersion = Version.Parse(ManagerFactory.settingsManager.Get<string>("LastVersion"));
        bool FirstStart = LastVersion == Version.Parse("0.0.0.0");
        if (FirstStart)
        {
#if !DEBUG
            SplashScreen.Show();
#endif
        }

        InitializeComponent();
        this.Tag = "MainWindow";

        fileVersionInfo = _fileVersionInfo;
        currentWindow = this;

        // used by system manager, controller manager
        uiSettings = new UISettings();

        // fix touch support
        TabletDeviceCollection tabletDevices = Tablet.TabletDevices;

        // define current directory
        InstallPath = AppDomain.CurrentDomain.BaseDirectory;
        SettingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "HandheldCompanion");

        // initialize path
        if (!Directory.Exists(SettingsPath))
            Directory.CreateDirectory(SettingsPath);

        Directory.SetCurrentDirectory(AppDomain.CurrentDomain.BaseDirectory);

        // initialize XInputWrapper
        XInputPlus.ExtractXInputPlusLibraries();

        // initialize notifyIcon
        notifyIcon = new NotifyIcon
        {
            Text = Title,
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location),
            Visible = false
        };

        notifyContextMenu = new ContextMenu
        {
            StaysOpen = true,
            IsOpen = false,
            Visibility = Visibility.Collapsed
        };
        AddNotifyIconItem(Properties.Resources.MainWindow_MainWindow, "MainWindow");
        AddNotifyIconItem(Properties.Resources.MainWindow_QuickTools, "QuickTools");

        AddNotifyIconSeparator();

        AddNotifyIconItem(Properties.Resources.MainWindow_Exit, "Quit");

        // initialize notifyIcon
        notifyIcon.DoubleClick += (sender, e) =>
        {
            // Stop the timer from ticking.
            notifyIconWaitTimer.Stop();
            if (overlayquickTools.Visibility == Visibility.Visible)
                overlayquickTools.ToggleVisibility();
            SwapWindowState();
        };

        notifyIcon.Click += (sender, e) =>
        {
            sensorTimer.Stop();
            if (e is MouseEventArgs me)
                switch (me.Button)
                {
                    case MouseButtons.Left:
                        notifyIconWaitTimer.Start();
                        break;

                    case MouseButtons.Right:
                        if (overlayquickTools.Visibility == Visibility.Visible)
                            overlayquickTools.ToggleVisibility();

                        Application.Current.Dispatcher.Invoke(async () =>
                        {
                            for (int i = 0; i < 2; i++)
                            {
                                await Task.Delay(100);
                                notifyContextMenu.IsOpen = true;
                                notifyContextMenu.Visibility = Visibility.Visible;
                                // Get context menu handle and bring it to the foreground
                                if (PresentationSource.FromVisual(notifyContextMenu) is HwndSource hwndSource)
                                    WinAPI.SetForegroundWindow(hwndSource.Handle);
                            }
                        });


                        break;
                }
        };

        notifyIcon.MouseMove += (sender, e) =>
        {
            RefreshSensors();

            if (!sensorTimer.IsEnabled)
                sensorTimer.Start();
        };

        // paths
        Process process = Process.GetCurrentProcess();
        CurrentExe = process.MainModule.FileName;
        CurrentPath = AppDomain.CurrentDomain.BaseDirectory;

        // initialize HidHide
        HidHide.RegisterApplication(CurrentExe);

        // collect details from MotherboardInfo
        MotherboardInfo.Collect();

        // initialize title
        Title += $" ({fileVersionInfo.FileVersion})";

        // initialize device
        currentDevice = IDevice.GetCurrent();
        currentDevice.PullSensors();

        // initialize title
        Title += $" {currentDevice.ProductName}";
        if (FirstStart)
        {
            if (currentDevice is SteamDeck steamDeck)
            {
                // do something
            }
            else if (currentDevice is AYANEOFlipDS flipDS)
            {
                // set Quicktools to Maximize on bottom screen
                ManagerFactory.settingsManager.Set("QuickToolsLocation", 2);
                ManagerFactory.settingsManager.Set("QuickToolsDeviceName", "AYANEOQHD");
            }

            ManagerFactory.settingsManager.Set("FirstStart", false);
        }

        // initialize UI sounds board
        UISounds uiSounds = new UISounds();

        // load window(s)
        loadWindows();

        // load page(s)
        overlayquickTools.loadPages();
        loadPages();

        // manage events
        SystemManager.SystemStatusChanged += OnSystemStatusChanged;
        ManagerFactory.deviceManager.UsbDeviceArrived += GenericDeviceUpdated;
        ManagerFactory.deviceManager.UsbDeviceRemoved += GenericDeviceUpdated;
        ControllerManager.ControllerSelected += ControllerManager_ControllerSelected;

        // prepare toast manager
        ToastManager.Start();
        ToastManager.IsEnabled = ManagerFactory.settingsManager.Get<bool>("ToastEnable");

        // start static managers
        GPUManager.Start();
        PowerProfileManager.Start();
        ProfileManager.Start();
        ControllerManager.Start();
        HotkeysManager.Start();
        DeviceManager.Start();
        OSDManager.Start();
        LayoutManager.Start();
        SystemManager.Start();
        DynamicLightingManager.Start();
        MultimediaManager.Start();
        VirtualManager.Start();
        SensorsManager.Start();
        TimerManager.Start();

        // non-STA threads
        List<Task> tasks = new List<Task>
        {
            Task.Run(() => PlatformManager.Start()),
            Task.Run(() => ProcessManager.Start()),
            Task.Run(() => TaskManager.Start(CurrentExe)),
            Task.Run(() => PerformanceManager.Start()),
            Task.Run(() => UpdateManager.Start())
        };

        // those managers can't be threaded
        InputsManager.Start();
        ManagerFactory.settingsManager.Start();

        // Load MVVM pages after the Models / data have been created.
        overlayquickTools.LoadPages_MVVM();
        LoadPages_MVVM();

        // update Position and Size
        Height = (int)Math.Max(MinHeight, ManagerFactory.settingsManager.Get<double>("MainWindowHeight"));
        Width = (int)Math.Max(MinWidth, ManagerFactory.settingsManager.Get<double>("MainWindowWidth"));
        Left = Math.Min(SystemParameters.PrimaryScreenWidth - MinWidth, ManagerFactory.settingsManager.Get<double>("MainWindowLeft"));
        Top = Math.Min(SystemParameters.PrimaryScreenHeight - MinHeight, ManagerFactory.settingsManager.Get<double>("MainWindowTop"));
        navView.IsPaneOpen = ManagerFactory.settingsManager.Get<bool>("MainWindowIsPaneOpen");

        // update LastVersion
        ManagerFactory.settingsManager.Set("LastVersion", fileVersionInfo.FileVersion);

    }

    private static void sensorTimer_Elapsed(object? sender, EventArgs e)
    {
        RefreshSensors();
    }

    static long lastRefresh;

    private static void RefreshSensors(bool force = false)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (!force && Math.Abs(DateTimeOffset.Now.ToUnixTimeMilliseconds() - lastRefresh) < 2000) return;
            lastRefresh = DateTimeOffset.Now.ToUnixTimeMilliseconds();

            string cpuTemp = "";
            string gpuTemp = "";
            string battery = "";
            string charge = "";

            if (PlatformManager.LibreHardwareMonitor.CPUPower != null)
                cpuTemp += $": {Math.Round((decimal)PlatformManager.LibreHardwareMonitor.CPUPower.Value, 1):0.0}W";

            if (PlatformManager.LibreHardwareMonitor.CPUTemp != null)
                cpuTemp += $" {Math.Round((decimal)PlatformManager.LibreHardwareMonitor.CPUTemp.Value)}°C";

            if (PlatformManager.LibreHardwareMonitor.CPULoad != null)
                cpuTemp += $" {Math.Round((decimal)PlatformManager.LibreHardwareMonitor.CPULoad.Value)}%";

            if (PlatformManager.LibreHardwareMonitor.MemoryUsage != null)
                cpuTemp += $" {Math.Round((decimal)PlatformManager.LibreHardwareMonitor.MemoryUsage.Value / 1024, 1)}GB";

            if (PlatformManager.LibreHardwareMonitor.GPUPower != null)
                gpuTemp += $": {Math.Round((decimal)PlatformManager.LibreHardwareMonitor.GPUPower.Value, 1):0.0}W";

            if (PlatformManager.LibreHardwareMonitor.GPUTemp != null)
                gpuTemp += $" {Math.Round((decimal)PlatformManager.LibreHardwareMonitor.GPUTemp.Value)}°C";

            if (PlatformManager.LibreHardwareMonitor.GPULoad != null)
                gpuTemp += $" {Math.Round((decimal)PlatformManager.LibreHardwareMonitor.GPULoad.Value)}%";

            if (PlatformManager.LibreHardwareMonitor.GPUMemoryUsage != null)
                gpuTemp += $" {Math.Round((decimal)PlatformManager.LibreHardwareMonitor.GPUMemoryUsage.Value / 1024, 1)}GB";

            if (PlatformManager.LibreHardwareMonitor.BatteryCapacity > 0)
                battery = $": {Math.Round((decimal)PlatformManager.LibreHardwareMonitor.BatteryCapacity)}%";

            if (PlatformManager.LibreHardwareMonitor.BatteryPower < 0)
                charge = $"{Properties.Resources.Discharging}: {Math.Round((decimal)PlatformManager.LibreHardwareMonitor.BatteryPower, 1)}W";
            else if (PlatformManager.LibreHardwareMonitor.BatteryPower > 0)
                charge = $"{Properties.Resources.Charging}: {Math.Round((decimal)PlatformManager.LibreHardwareMonitor.BatteryPower, 1)}W";

            if (PlatformManager.LibreHardwareMonitor.BatteryHealth > 0)
                battery += $"\nBattery Health: {Math.Round((decimal)PlatformManager.LibreHardwareMonitor.BatteryHealth, 1)}%";

            string trayTip = $"CPU{cpuTemp}";
            if (gpuTemp.Length > 0) trayTip += "\nGPU" + gpuTemp;
            //if (PlatformManager.LibreHardwareMonitor.CPUFanSpeed != null) trayTip += $"\nFan {PlatformManager.LibreHardwareMonitor.CPUFanSpeed}RPM";
            if (battery.Length > 0) trayTip += "\nBattery Remaining" + battery;
            if (charge.Length > 0) trayTip += "\n" + charge;

            notifyIcon.Text = trayTip;

        });
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case WM_DISPLAYCHANGE:
            case WM_DEVICECHANGE:
                ManagerFactory.deviceManager.RefreshDisplayAdapters();
                break;
            case WM_QUERYENDSESSION:
                break;
        }

        return IntPtr.Zero;
    }

    private void ControllerManager_ControllerSelected(IController Controller)
    {
        // UI thread
        UIHelper.TryInvoke(() =>
        {
            // update glyph(s)
            GamepadUISelectIcon.Glyph = Controller.GetGlyph(ButtonFlags.B1);
            GamepadUIBackIcon.Glyph = Controller.GetGlyph(ButtonFlags.B2);
            GamepadUIToggleIcon.Glyph = Controller.GetGlyph(ButtonFlags.B4);

            // update color(s)
            Color? color1 = Controller.GetGlyphColor(ButtonFlags.B1);
            if (color1.HasValue)
                GamepadUISelectIcon.Foreground = new SolidColorBrush(color1.Value);
            else
                GamepadUISelectIcon.SetResourceReference(ForegroundProperty, "SystemControlForegroundBaseHighBrush");

            Color? color2 = Controller.GetGlyphColor(ButtonFlags.B2);
            if (color2.HasValue)
                GamepadUIBackIcon.Foreground = new SolidColorBrush(color2.Value);
            else
                GamepadUIBackIcon.SetResourceReference(ForegroundProperty, "SystemControlForegroundBaseHighBrush");

            Color? color4 = Controller.GetGlyphColor(ButtonFlags.B4);
            if (color4.HasValue)
                GamepadUIToggleIcon.Foreground = new SolidColorBrush(color4.Value);
            else
                GamepadUIBackIcon.SetResourceReference(ForegroundProperty, "SystemControlForegroundBaseHighBrush");
        });
    }

    private void GamepadFocusManagerOnFocused(Control control)
    {
        // UI thread
        UIHelper.TryInvoke(() =>
        {
            // todo : localize me
            string controlType = control.GetType().Name;
            switch (controlType)
            {
                default:
                    {
                        GamepadUISelect.Visibility = Visibility.Visible;
                        GamepadUIBack.Visibility = Visibility.Visible;
                        GamepadUIToggle.Visibility = Visibility.Collapsed;

                        GamepadUISelectDesc.Text = Properties.Resources.MainWindow_Select;
                        GamepadUIBackDesc.Text = Properties.Resources.MainWindow_Back;
                    }
                    break;

                case "Button":
                    {
                        GamepadUISelect.Visibility = Visibility.Visible;
                        GamepadUIBack.Visibility = Visibility.Visible;

                        GamepadUISelectDesc.Text = Properties.Resources.MainWindow_Select;
                        GamepadUIBackDesc.Text = Properties.Resources.MainWindow_Back;

                        // To get the first RadioButton in the list, if any
                        RadioButton firstRadioButton = WPFUtils.FindChildren(control).FirstOrDefault(c => c is RadioButton) as RadioButton;
                        if (firstRadioButton is not null)
                        {
                            GamepadUIToggle.Visibility = Visibility.Visible;
                            GamepadUIToggleDesc.Text = Properties.Resources.MainWindow_Toggle;
                        }

                        if (control.Tag is ProfileViewModel profileViewModel)
                        {
                            Profile profile = profileViewModel.Profile;
                            if (!profile.ErrorCode.HasFlag(ProfileErrorCode.MissingExecutable))
                            {
                                GamepadUIToggle.Visibility = Visibility.Visible;
                                GamepadUIToggleDesc.Text = "Play";
                            }
                        }
                    }
                    break;

                case "Slider":
                    {
                        GamepadUISelect.Visibility = Visibility.Collapsed;
                        GamepadUIBack.Visibility = Visibility.Visible;
                        GamepadUIToggle.Visibility = Visibility.Collapsed;
                    }
                    break;

                case "NavigationViewItem":
                    {
                        GamepadUISelect.Visibility = Visibility.Visible;
                        GamepadUIBack.Visibility = Visibility.Collapsed;
                        GamepadUIToggle.Visibility = Visibility.Collapsed;

                        GamepadUISelectDesc.Text = Properties.Resources.MainWindow_Navigate;
                    }
                    break;
            }
        });
    }

    private void AddNotifyIconItem(string name, object tag = null)
    {
        if (notifyContextMenu is null)
            return;

        tag ??= string.Concat(name.Where(c => !char.IsWhiteSpace(c)));

        var menuItemMainWindow = new MenuItem()
        {
            Icon = new FontIcon
            {
                Glyph = tag switch
                {
                    "MainWindow" => "\uE7C4",
                    "QuickTools" => "\uEC7A",
                    "Quit" => "\uF3B1",
                    _ => "\uE713"
                }
            },
            Header = name,
            Tag = tag
        };
        menuItemMainWindow.Click += (sender, e) =>
        {

            Application.Current.Dispatcher.Invoke(() =>
            {
                if (sender is MenuItem menuItem)
                    switch (menuItem.Tag)
                    {
                        case "MainWindow":
                            SwapWindowState();
                            break;
                        case "QuickTools":
                            overlayquickTools.ToggleVisibility();
                            break;
                        case "Quit":
                            appClosing = true;
                            notifyContextMenu.IsOpen = false;
                            Close();
                            break;
                    }
            });
        };
        notifyContextMenu.Items.Add(menuItemMainWindow);
        //notifyIcon.ContextMenuStrip.Items.Add(menuItemMainWindow);
    }

    private void UnloadNotifyIcon()
    {
        notifyContextMenu.IsOpen = false;
        notifyIcon.Visible = false;
        notifyIcon.Dispose();
        notifyIcon = null;
    }

    private void AddNotifyIconSeparator()
    {
        if (notifyContextMenu is null)
            return;

        var separator = new Separator();
        notifyContextMenu.Items.Add(separator);
        //notifyIcon.ContextMenuStrip.Items.Add("-");
    }

    private void SettingsManager_SettingValueChanged(string name, object value, bool temporary)
    {
        switch (name)
        {
            case "ToastEnable":
                ToastManager.IsEnabled = Convert.ToBoolean(value);
                break;
            case "TelemetryApproved":

                // If the input is null or empty, return false or handle as needed
                if (string.IsNullOrEmpty(Convert.ToString(value)))
                    break;

                bool test = Convert.ToBoolean(value);
                break;
        }
    }

    public void SwapWindowState()
    {
        // UI thread
        Application.Current.Dispatcher.Invoke(() =>
        {
            switch (WindowState)
            {
                case WindowState.Normal:
                case WindowState.Maximized:
                    WindowState = WindowState.Minimized;
                    break;
                case WindowState.Minimized:
                    WindowState = prevWindowState;
                    break;
            }
        });
    }

    public void SetState(WindowState windowState)
    {
        UIHelper.TryInvoke(() =>
        {
            WindowState = windowState;
        });
    }

    public static MainWindow GetCurrent()
    {
        return currentWindow;
    }

    public void UpdateTaskbarState(TaskbarItemProgressState state)
    {
        // UI thread
        UIHelper.TryInvoke(() =>
        {
            this.TaskbarItem.ProgressState = state;
        });
    }

    public void UpdateTaskbarProgress(double value)
    {
        if (value < 0 || value > 1) return;

        // UI thread
        UIHelper.TryInvoke(() =>
        {
            this.TaskbarItem.ProgressValue = value;
        });
    }

    private void loadPages()
    {
        // initialize pages
        controllerPage = new ControllerPage("controller");
        devicePage = new DevicePage("device");
        profilesPage = new ProfilesPage("profiles");
        settingsPage = new SettingsPage("settings");
        overlayPage = new OverlayPage("overlay");
        hotkeysPage = new HotkeysPage("hotkeys");
        notificationsPage = new NotificationsPage("notifications");

        // store pages
        _pages.Add("ControllerPage", controllerPage);
        _pages.Add("DevicePage", devicePage);

        _pages.Add("ProfilesPage", profilesPage);

        _pages.Add("OverlayPage", overlayPage);
        _pages.Add("SettingsPage", settingsPage);
        _pages.Add("HotkeysPage", hotkeysPage);

        _pages.Add("NotificationsPage", notificationsPage);
    }

    private void LoadPages_MVVM()
    {
        layoutPage = new LayoutPage("layout", navView);
        performancePage = new PerformancePage();
        aboutPage = new AboutPage();

        layoutPage.Initialize();

        // storage pages
        _pages.Add("LayoutPage", layoutPage);
        _pages.Add("PerformancePage", performancePage);
        _pages.Add("AboutPage", aboutPage);
    }

    private void loadWindows()
    {
        // initialize overlay
        overlayModel = new OverlayModel();
        overlayTrackpad = new OverlayTrackpad();
        overlayquickTools = new OverlayQuickTools();
        overlayToast = new OverlayToast();
    }

    private void GenericDeviceUpdated(PnPDevice device, Guid IntefaceGuid)
    {
        // todo: improve me
        currentDevice.PullSensors();
    }

    private void MenuItem_Click(object? sender, EventArgs e)
    {
        if (sender is ToolStripMenuItem menuItem)
        {
            switch (menuItem.Tag)
            {
                case "MainWindow":
                    SwapWindowState();
                    break;
                case "QuickTools":
                    overlayquickTools.ToggleVisibility();
                    break;
                case "Exit":
                    appClosing = true;
                    Close();
                    break;
            }
        }
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        // hide splashscreen
        SplashScreen?.Close();

        // load gamepad navigation maanger
        gamepadFocusManager = new(this, ContentFrame);

        HwndSource source = PresentationSource.FromVisual(this) as HwndSource;
        source.AddHook(WndProc); // Hook into the window's message loop

        // restore window state
        WindowState = ManagerFactory.settingsManager.Get<bool>("StartMinimized") ? WindowState.Minimized : (WindowState)ManagerFactory.settingsManager.Get<int>("MainWindowState");
        prevWindowState = (WindowState)ManagerFactory.settingsManager.Get<int>("MainWindowPrevState");
    }

    private void ControllerPage_Loaded(object sender, RoutedEventArgs e)
    {
        // home page is ready, display main window
        this.Visibility = Visibility.Visible;

        string TelemetryApproved = ManagerFactory.settingsManager.Get<string>("TelemetryApproved");
        if (string.IsNullOrEmpty(TelemetryApproved))
        {
            string Title = Properties.Resources.MainWindow_TelemetryTitle;
            string Content = Properties.Resources.MainWindow_TelemetryText;

            MessageBoxResult result = MessageBox.Show(Content, Title, MessageBoxButton.YesNo);
            ManagerFactory.settingsManager.Set("TelemetryApproved", result == MessageBoxResult.Yes ? "True" : "False");
            ManagerFactory.settingsManager.Set("TelemetryEnabled", result == MessageBoxResult.Yes ? true : false);
        }
    }

    private void NotificationsPage_LayoutUpdated(int notifications)
    {
        // UI thread (async)
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            HasNotifications.Visibility = notifications != 0 ? Visibility.Visible : Visibility.Collapsed;
            HasNotifications.Value = notifications;
        });
    }

    private DateTime pendingTime = DateTime.Now;
    private DateTime resumeTime = DateTime.Now;
    private async void OnSystemStatusChanged(SystemManager.SystemStatus status, SystemManager.SystemStatus prevStatus)
    {
        if (status == prevStatus)
            return;

        switch (status)
        {
            case SystemManager.SystemStatus.SystemReady:
                {
                    if (prevStatus == SystemManager.SystemStatus.SystemPending)
                    {
                        // when device resumes from sleep
                        // use device-specific delay
                        await Task.Delay(currentDevice.ResumeDelay);

                        // resume manager(s)
                        InputsManager.Start();
                        TimerManager.Start();
                        SensorsManager.Resume(true);
                        GPUManager.Start();
                        OSDManager.Start();
                        PerformanceManager.Resume(true);

                        ManagerFactory.Resume();

                        // resume platform(s)
                        PlatformManager.LibreHardwareMonitor.Start();
                    }

                    // open device, when ready
                    new Task(async () =>
                    {
                        // wait for all HIDs to be ready
                        while (!currentDevice.IsReady())
                            await Task.Delay(250).ConfigureAwait(false);

                        // open current device (threaded to avoid device to hang)
                        if (currentDevice.Open())
                        {
                            // manage events
                            currentDevice.OpenEvents();
                        }
                    }).Start();
                }
                break;

            case SystemManager.SystemStatus.SystemPending:
                {
                    // when device goes to sleep
                    // suspend manager(s)
                    VirtualManager.Suspend(true);
                    await Task.Delay(currentDevice.ResumeDelay); // Captures synchronization context

                    TimerManager.Stop();
                    SensorsManager.Stop();
                    InputsManager.Stop();
                    GPUManager.Stop();

                    // suspend platform(s)
                    PlatformManager.LibreHardwareMonitor.Stop();

                    // close current device
                    currentDevice.Close();

                    // free memory
                    GC.Collect();

                    // Allow system to sleep
                    SystemManager.SetThreadExecutionState(SystemManager.ES_CONTINUOUS);
                    LogManager.LogDebug("Tasks completed. System can now suspend if needed.");
                }
                break;
        }
    }

    #region UI

    private void navView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.InvokedItemContainer is not null)
        {
            var navItem = (NavigationViewItem)args.InvokedItemContainer;
            var navItemTag = (string)navItem.Tag;

            NavView_Navigate(navItemTag);
        }
    }

    public void NavView_Navigate(string navItemTag)
    {
        if (navItemTag == "WindowsExit")
        {
            appClosing = true;
            Close();
            return;
        }
        var item = _pages.FirstOrDefault(p => p.Key.Equals(navItemTag));
        var _page = item.Value;

        // Get the page type before navigation so you can prevent duplicate
        // entries in the backstack.
        var preNavPageType = ContentFrame.CurrentSourcePageType;

        // Only navigate if the selected page isn't currently loaded.
        if (_page is not null && !Equals(preNavPageType, _page)) NavView_Navigate(_page);
    }

    public void NavigateToPage(string navItemTag)
    {
        if (prevNavItemTag == navItemTag)
            return;

        // Navigate to the specified page
        NavView_Navigate(navItemTag);
    }

    public static void NavView_Navigate(Page _page)
    {
        currentWindow.ContentFrame.Navigate(_page);
        currentWindow.scrollViewer.ScrollToTop();
    }

    private void navView_BackRequested(NavigationView sender, NavigationViewBackRequestedEventArgs args)
    {
        TryGoBack();
    }

    private void Window_Closed(object sender, EventArgs e)
    {
        currentDevice.Close();

        UnloadNotifyIcon();

        // stop windows
        overlayModel.Close();
        overlayTrackpad.Close();
        overlayquickTools.Close(true);

        // stop pages
        controllerPage.Page_Closed();
        profilesPage.Page_Closed();
        settingsPage.Page_Closed();
        overlayPage.Page_Closed();
        hotkeysPage.Page_Closed();
        layoutPage.Page_Closed();
        notificationsPage.Page_Closed();

        // remove all automation event handlers
        Automation.RemoveAllEventHandlers();

        // stop managers
        VirtualManager.Stop();
        MultimediaManager.Stop();
        GPUManager.Stop();
        MotionManager.Stop();
        SensorsManager.Stop();
        ControllerManager.Stop();
        InputsManager.Stop();
        DeviceManager.Stop();
        PlatformManager.Stop();
        OSDManager.Stop();
        PowerProfileManager.Stop();
        ProfileManager.Stop();
        LayoutManager.Stop();
        SystemManager.Stop();
        DynamicLightingManager.Stop();
        ProcessManager.Stop();
        ToastManager.Stop();
        TaskManager.Stop();
        PerformanceManager.Stop();
        UpdateManager.Stop();
    }

    private async void Window_Closing(object sender, CancelEventArgs e)
    {
        // position and size settings
        switch (WindowState)
        {
            case WindowState.Normal:
                ManagerFactory.settingsManager.Set("MainWindowLeft", Left);
                ManagerFactory.settingsManager.Set("MainWindowTop", Top);
                ManagerFactory.settingsManager.Set("MainWindowWidth", ActualWidth);
                ManagerFactory.settingsManager.Set("MainWindowHeight", ActualHeight);
                break;
            case WindowState.Maximized:
                ManagerFactory.settingsManager.Set("MainWindowLeft", 0);
                ManagerFactory.settingsManager.Set("MainWindowTop", 0);
                ManagerFactory.settingsManager.Set("MainWindowWidth", SystemParameters.MaximizedPrimaryScreenWidth);
                ManagerFactory.settingsManager.Set("MainWindowHeight", SystemParameters.MaximizedPrimaryScreenHeight);

                break;
        }

        ManagerFactory.settingsManager.Set("MainWindowState", (int)WindowState);
        ManagerFactory.settingsManager.Set("MainWindowPrevState", (int)prevWindowState);

        ManagerFactory.settingsManager.Set("MainWindowIsPaneOpen", navView.IsPaneOpen);

        if (ManagerFactory.settingsManager.Get<bool>("CloseMinimises") && !appClosing)
        {
            e.Cancel = true;
            WindowState = WindowState.Minimized;
            return;
        }
    }

    private void Window_StateChanged(object sender, EventArgs e)
    {
        switch (WindowState)
        {
            case WindowState.Minimized:
                notifyIcon.Visible = true;
                ShowInTaskbar = false;

                if (!notifyInTaskbar)
                {
                    ToastManager.SendToast(Title, "is running in the background");
                    notifyInTaskbar = true;
                }

                break;
            case WindowState.Normal:
            case WindowState.Maximized:
                notifyIcon.Visible = false;
                ShowInTaskbar = true;

                Activate();
                Topmost = true;  // important
                Topmost = false; // important
                Focus();

                prevWindowState = WindowState;
                break;
        }
    }

    private void navView_Loaded(object sender, RoutedEventArgs e)
    {
        // Add handler for ContentFrame navigation.
        ContentFrame.Navigated += On_Navigated;

        // NavView doesn't load any page by default, so load home page.
        navView.SelectedItem = navView.MenuItems[0];

        // If navigation occurs on SelectionChanged, this isn't needed.
        // Because we use ItemInvoked to navigate, we need to call Navigate
        // here to load the home page.
        preNavItemTag = "ControllerPage";
        NavView_Navigate(preNavItemTag);
    }

    private void GamepadWindow_PreviewGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (!e.NewFocus.GetType().IsSubclassOf(typeof(Control)))
            return;

        GamepadFocusManagerOnFocused((Control)e.NewFocus);
    }

    private void GamepadWindow_PreviewLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        // do something
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

    private void navView_PaneOpened(NavigationView sender, object args)
    {
        // todo: localize me
        PaneText.Text = "Close navigation";
    }

    private void navView_PaneClosed(NavigationView sender, object args)
    {
        // todo: localize me
        PaneText.Text = "Open navigation";
    }

    private void On_Navigated(object sender, NavigationEventArgs e)
    {
        navView.IsBackEnabled = ContentFrame.CanGoBack;

        if (ContentFrame.SourcePageType is not null)
        {
            CurrentPageName = ContentFrame.CurrentSourcePageType.Name;

            var NavViewItem = navView.MenuItems
                .OfType<NavigationViewItem>()
                .Where(n => n.Tag.Equals(CurrentPageName)).FirstOrDefault();

            if (!(NavViewItem is null))
                navView.SelectedItem = NavViewItem;

            navView.Header = new TextBlock() { Text = ((Page)e.Content).Title };
        }
    }

    #endregion
}
