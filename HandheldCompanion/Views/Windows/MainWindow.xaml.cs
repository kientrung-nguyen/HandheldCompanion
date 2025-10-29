using HandheldCompanion.Controllers;
using HandheldCompanion.Devices;
using HandheldCompanion.Helpers;
using HandheldCompanion.Inputs;
using HandheldCompanion.Managers;
using HandheldCompanion.Notifications;
using HandheldCompanion.Shared;
using HandheldCompanion.UI;
using HandheldCompanion.Utils;
using HandheldCompanion.ViewModels;
using HandheldCompanion.Views.Classes;
using HandheldCompanion.Views.Pages;
using HandheldCompanion.Views.Windows;
using iNKORE.UI.WPF.Modern;
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
    public static LibraryPage libraryPage;

    // overlay(s) vars
    public static OverlayModel overlayModel;
    public static OverlayTrackpad overlayTrackpad;
    public static OverlayToast overlayToast;
    public static OverlayStatusBar overlayStatusBar;
    public static OverlayQuickTools overlayquickTools;

    public static string CurrentExe, CurrentPath;

    private static MainWindow currentWindow;
    private static FileVersionInfo fileVersionInfo;

    public static string CurrentPageName = string.Empty;

    private bool appClosing;
    private readonly NotifyIcon notifyIcon;
    private readonly ContextMenu notifyContextMenu;
    private bool notifyInTaskbar;
    private string prevNavItemTag;
    private DispatcherTimer sensorTimer;
    private DispatcherTimer notifyIconWaitTimer;

    private WindowState prevWindowState;
    public static SplashScreen SplashScreen;

    public static UISettings uiSettings;

    private const int WM_QUERYENDSESSION = 0x0011;
    private const int WM_DISPLAYCHANGE = 0x007e;
    private const int WM_DEVICECHANGE = 0x0219;

    public static Version LastVersion => Version.Parse(ManagerFactory.settingsManager.Get<string>("LastVersion"));
    public static Version CurrentVersion => Version.Parse(fileVersionInfo.FileVersion);
    private static bool StartMinimized => ManagerFactory.settingsManager.Get<bool>("StartMinimized");

    private static bool PreloadPages => ManagerFactory.settingsManager.Get<bool>("PreloadPages");


    private void notifyIconWaitTimerTicked(object? sender, EventArgs e)
    {
        notifyIconWaitTimer.Stop();
        overlayquickTools.ToggleVisibility();
    }

    public MainWindow(FileVersionInfo _fileVersionInfo, Assembly CurrentAssembly)
    {
        // initialize splash screen
        SplashScreen = new SplashScreen();
        DataContext = new MainWindowViewModel();

#if !DEBUG
        SplashScreen.Show();
#endif

        // set theme
        var currentTheme = (ElementTheme)ManagerFactory.settingsManager.Get<int>("MainWindowTheme");
        ThemeManager.SetRequestedTheme(this, currentTheme);

        InitializeComponent();
        this.Tag = "MainWindow";

        fileVersionInfo = _fileVersionInfo;
        currentWindow = this;

        // get last version
        bool FirstStart = LastVersion == Version.Parse("0.0.0.0");
        bool NewUpdate = LastVersion != CurrentVersion;

        // used by system manager, controller manager
        uiSettings = new UISettings();

        // define current directory
        Directory.SetCurrentDirectory(AppDomain.CurrentDomain.BaseDirectory);

        // initialize XInputWrapper
        XInputPlus.ExtractXInputPlusLibraries();

        sensorTimer = new(
            TimeSpan.FromMilliseconds(1000),
            DispatcherPriority.Normal,
            sensorTimer_Elapsed,
            Dispatcher.CurrentDispatcher)
        {
            IsEnabled = false
        };

        notifyIconWaitTimer = new(
            TimeSpan.FromMilliseconds(200),
            DispatcherPriority.Normal,
            notifyIconWaitTimerTicked,
            Dispatcher.CurrentDispatcher)
        {
            IsEnabled = false
        };
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

        AddNotifyIconItem("Sambar", "Sambar");

        AddNotifyIconSeparator();

        AddNotifyIconItem(Properties.Resources.MainWindow_Exit, "Quit");

        // initialize notifyIcon
        notifyIcon.DoubleClick += (sender, e) =>
        {
            // Stop the timer from ticking.
            notifyIconWaitTimer.Stop();
            if (overlayquickTools.Visibility == Visibility.Visible)
                overlayquickTools.ToggleVisibility();
            ToggleState();
        };

        notifyIcon.Click += (sender, e) =>
        {
            sensorTimer.Stop();
            if (e is MouseEventArgs me)
                switch (me.Button)
                {
                    case MouseButtons.Left:
                        if (WindowState == WindowState.Minimized)
                            notifyIconWaitTimer.Start();
                        break;

                    case MouseButtons.Right:
                        if (overlayquickTools.Visibility == Visibility.Visible)
                            overlayquickTools.ToggleVisibility();

                        UIHelper.TryInvoke(async () =>
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
        var process = Process.GetCurrentProcess();
        CurrentExe = process.MainModule.FileName;
        CurrentPath = AppDomain.CurrentDomain.BaseDirectory;

        // initialize HidHide
        HidHide.RegisterApplication(CurrentExe);

        // collect details from MotherboardInfo
        MotherboardInfo.Collect();

        // initialize title
        Title += $" ({fileVersionInfo.FileVersion})";

        // initialize UI sounds board
        UISounds uiSounds = new UISounds();

        // load window(s)
        loadWindows();

        // load page(s)
        overlayquickTools.loadPages();
        loadPages();


        // initialize device
        currentDevice = IDevice.GetCurrent();
        currentDevice.PullSensors();
        currentDevice.Initialize(FirstStart, NewUpdate);

        // initialize title
        Title += $" {currentDevice.ProductName}";

        // manage events
        SystemManager.SystemStatusChanged += OnSystemStatusChanged;
        ManagerFactory.deviceManager.UsbDeviceArrived += GenericDeviceUpdated;
        ManagerFactory.deviceManager.UsbDeviceRemoved += GenericDeviceUpdated;
        ManagerFactory.notificationManager.Added += NotificationManagerUpdated;
        ManagerFactory.notificationManager.Discarded += NotificationManagerUpdated;
        ControllerManager.ControllerSelected += ControllerManager_ControllerSelected;

        // prepare toast manager
        ToastManager.Start();

        // start non-static managers
        foreach (IManager manager in ManagerFactory.Managers)
            Task.Run(() => manager.Start());

        // start static managers
        // todo: make them non-static
        List<Task> tasks = new List<Task>
        {
            Task.Run(() => OSDManager.Start()),
            Task.Run(() => SystemManager.Start()),
            Task.Run(() => DynamicLightingManager.Start()),
            Task.Run(() => VirtualManager.Start()),
            Task.Run(() => SensorsManager.Start()),
            Task.Run(() => ControllerManager.Start()),
            Task.Run(() => TaskManager.Start(CurrentExe)),
            Task.Run(() => PerformanceManager.Start()),
            Task.Run(() => UpdateManager.Start())
        };

        // start non-threaded managers
        InputsManager.Start();
        TimerManager.Start();
        MotionManager.Start();
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

        // load gamepad navigation manager
        gamepadFocusManager = new(this, ContentFrame);
    }

    private void sensorTimer_Elapsed(object? sender, EventArgs e)
    {
        RefreshSensors();
    }

    static long lastRefresh;

    private void RefreshSensors(bool force = false)
    {
        UIHelper.TryInvoke(() =>
        {
            if (notifyIcon == null)
                return;

            if (!force && Math.Abs(DateTimeOffset.Now.ToUnixTimeMilliseconds() - lastRefresh) < 2000) return;
            lastRefresh = DateTimeOffset.Now.ToUnixTimeMilliseconds();

            string cpuTemp = "";
            string memStr = "";
            string gpuTemp = "";
            string mem2Str = "";
            string netStr = "";
            string battery = "";
            string charge = "";

            if (PlatformManager.LibreHardware != null)
            {
                if (!float.IsNaN(PlatformManager.LibreHardware.GetCPULoad()))
                    cpuTemp += $": {Math.Round((decimal)PlatformManager.LibreHardware.GetCPULoad()):00}%";

                if (!float.IsNaN(PlatformManager.LibreHardware.GetCPUPower()))
                    cpuTemp += $" {Math.Round((decimal)PlatformManager.LibreHardware.GetCPUPower(), 1):00.0}W";

                if (!float.IsNaN(PlatformManager.LibreHardware.GetCPUTemperature()))
                    cpuTemp += $" {Math.Round((decimal)PlatformManager.LibreHardware.GetCPUTemperature()):00}°C";

                if (!float.IsNaN(PlatformManager.LibreHardware.GetMemoryUsage()))
                    memStr += $": {Math.Round((decimal)PlatformManager.LibreHardware.GetMemoryUsage() / 1024, 1):00.0}GB";

                if (!float.IsNaN(PlatformManager.LibreHardware.GetGPULoad()))
                    gpuTemp += $": {Math.Round((decimal)PlatformManager.LibreHardware.GetGPULoad()):00}%";

                if (!float.IsNaN(PlatformManager.LibreHardware.GetGPUPower()))
                    gpuTemp += $" {Math.Round((decimal)PlatformManager.LibreHardware.GetGPUPower(), 1):00.0}W";

                if (!float.IsNaN(PlatformManager.LibreHardware.GetGPUTemperature()))
                    gpuTemp += $" {Math.Round((decimal)PlatformManager.LibreHardware.GetGPUTemperature()):00}°C";

                if (!float.IsNaN(PlatformManager.LibreHardware.GetGPUMemoryDedicated()))
                    mem2Str += $": {Math.Round((decimal)PlatformManager.LibreHardware.GetGPUMemoryDedicated() / 1024, 1):00.0}GB";

                if (!float.IsNaN(PlatformManager.LibreHardware.GetBatteryLevel()))
                    battery = $": {Math.Round((decimal)PlatformManager.LibreHardware.GetBatteryLevel())}%";

                if (!float.IsNaN(PlatformManager.LibreHardware.GetBatteryPower()))
                    charge = $"{Properties.Resources.Discharging}: {Math.Round((decimal)PlatformManager.LibreHardware.GetBatteryPower(), 1)}W";
                else if (PlatformManager.LibreHardware.GetBatteryPower() > 0)
                    charge = $"{Properties.Resources.Charging}: {Math.Round((decimal)PlatformManager.LibreHardware.GetBatteryPower(), 1)}W";

                if (!float.IsNaN(PlatformManager.LibreHardware.GetBatteryHealth()))
                    battery += $"\nBattery Health: {Math.Round((decimal)PlatformManager.LibreHardware.GetBatteryHealth(), 1)}%";

                if (!float.IsNaN(PlatformManager.LibreHardware.GetNetworkSpeedDown()))
                {
                    (float _down, string _down_flag) = NormalizeSpeed(PlatformManager.LibreHardware.GetNetworkSpeedDown());
                    netStr += $": {_down:00.0} ↓{_down_flag}";
                }
                if (!float.IsNaN(PlatformManager.LibreHardware.GetNetworkSpeedUp()))
                {
                    (float _up, string _up_flag) = NormalizeSpeed(PlatformManager.LibreHardware.GetNetworkSpeedUp());
                    netStr += $" {_up:00.0} ↑{_up_flag}";
                }
            }
            string trayTip = $"CPU{cpuTemp}";

            if (memStr.Length > 0) trayTip += "\nRAM" + memStr;
            if (gpuTemp.Length > 0) trayTip += "\nGPU" + gpuTemp;
            if (mem2Str.Length > 0) trayTip += "\nVRAM" + mem2Str;
            if (netStr.Length > 0) trayTip += "\nNET" + netStr;
            //if (PlatformManager.LibreHardware.CPUFanSpeed != null) trayTip += $"\nFan {PlatformManager.LibreHardware.CPUFanSpeed}RPM";
            //if (battery.Length > 0) trayTip += "\nBattery Remaining" + battery;
            //if (charge.Length > 0) trayTip += "\n" + charge;

            notifyIcon.Text = trayTip;

        });
    }

    /// <summary>
	/// Normalize speed in Kbps to 00.0 format with appropriate flags
	/// </summary>
	private static (float, string) NormalizeSpeed(float speed)
    {
        speed /= 1024f;
        string unit = "Kb/s";
        if (speed > 100) unit = "Mb/s";
        if (speed > 100000) unit = "Gb/s";
        while (speed.ToString().Split(".")[0].Length > 2) { speed /= 1024; }
        return (speed, unit);
    }

    protected override IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
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

        return base.WndProc(hwnd, msg, wParam, lParam, ref handled);
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
                        var firstRadioButton = WPFUtils.FindChildren(control).FirstOrDefault(c => c is RadioButton) as RadioButton;
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

            UIHelper.TryInvoke(() =>
            {
                if (sender is MenuItem menuItem)
                    switch (menuItem.Tag)
                    {
                        case "Sambar":
                            overlayStatusBar.ToggleVisibility();
                            break;
                        case "MainWindow":
                            ToggleState();
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
    }

    private void AddNotifyIconSeparator()
    {
        if (notifyContextMenu is null)
            return;

        var separator = new Separator();
        notifyContextMenu.Items.Add(separator);
    }

    public void ToggleState()
    {
        // UI thread
        UIHelper.TryInvoke(() =>
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
        libraryPage = new LibraryPage("library");

        // store pages
        _pages.Add("ControllerPage", controllerPage);
        _pages.Add("DevicePage", devicePage);
        _pages.Add("ProfilesPage", profilesPage);
        _pages.Add("OverlayPage", overlayPage);
        _pages.Add("SettingsPage", settingsPage);
        _pages.Add("HotkeysPage", hotkeysPage);
        _pages.Add("NotificationsPage", notificationsPage);
        _pages.Add("LibraryPage", libraryPage);
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
        overlayStatusBar = new OverlayStatusBar();
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
                    ToggleState();
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

        // load gamepad navigation manager
        gamepadFocusManager?.Loaded();

        var source = PresentationSource.FromVisual(this) as HwndSource;
        source?.AddHook(WndProc); // Hook into the window's message loop

        // restore window state
        WindowState = StartMinimized ? WindowState.Minimized : (WindowState)ManagerFactory.settingsManager.Get<int>("MainWindowState");
        prevWindowState = (WindowState)ManagerFactory.settingsManager.Get<int>("MainWindowPrevState");
    }

    private bool Homepage_Loaded = false;
    private void HomePage_Loaded()
    {
        // set status
        Homepage_Loaded = true;

        // home page is ready, display main window
        this.Visibility = Visibility.Visible;
        notifyIcon.Visible = true;

        string TelemetryApproved = ManagerFactory.settingsManager.Get<string>("TelemetryApproved");
        if (string.IsNullOrEmpty(TelemetryApproved))
        {
            string Title = Properties.Resources.MainWindow_TelemetryTitle;
            string Content = Properties.Resources.MainWindow_TelemetryText;

            MessageBoxResult result = MessageBox.Show(Content, Title, MessageBoxButton.YesNo);
            ManagerFactory.settingsManager.Set("TelemetryApproved", result == MessageBoxResult.Yes ? "True" : "False");
            ManagerFactory.settingsManager.Set("TelemetryEnabled", result == MessageBoxResult.Yes);
        }
    }

    private void NotificationManagerUpdated(Notification notification)
    {
        // UI thread (async)
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            HasNotifications.Visibility = ManagerFactory.notificationManager.Any ? Visibility.Visible : Visibility.Collapsed;
            HasNotifications.Value = ManagerFactory.notificationManager.Count;
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
                        resumeTime = DateTime.Now;

                        // use device-specific delay
                        await Task.Delay(currentDevice.ResumeDelay); // Captures synchronization context

                        // resume manager(s)
                        InputsManager.Start();
                        TimerManager.Start();
                        SensorsManager.Resume(true);
                        PerformanceManager.Resume(true);

                        ManagerFactory.Resume();

                        // resume platform(s)
                        PlatformManager.LibreHardware.Start();

                        // wait a bit more if device went to sleep for at least 30 minutes (arbitrary)
                        TimeSpan sleepDuration = resumeTime - pendingTime;
                        if (sleepDuration.TotalMinutes > 30)
                            await Task.Delay(currentDevice.ResumeDelay); // Captures synchronization context

                        VirtualManager.Resume(true);
                        ControllerManager.Resume(true);
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
                    if (prevStatus == SystemManager.SystemStatus.SystemReady)
                    {
                        SystemManager.SetThreadExecutionState(SystemManager.ES_CONTINUOUS | SystemManager.ES_SYSTEM_REQUIRED);
                        LogManager.LogInformation("System is about to suspend. Performing tasks.");

                        // when device goes to sleep
                        pendingTime = DateTime.Now;

                        // hide subwindow(s)
                        overlayModel.SetVisibility(Visibility.Collapsed);
                        overlayTrackpad.SetVisibility(Visibility.Collapsed);
                        overlayquickTools.SetVisibility(Visibility.Collapsed);

                        // suspend manager(s)
                        ManagerFactory.Suspend();

                        VirtualManager.Suspend(true);
                        ControllerManager.Suspend(true);
                        TimerManager.Stop();
                        SensorsManager.Stop();
                        InputsManager.Stop(false);

                        // suspend platform(s)
                        PlatformManager.LibreHardware.Stop();

                        // close current device
                        currentDevice.Close();

                        // Allow system to sleep
                        _ = SystemManager.SetThreadExecutionState(SystemManager.ES_CONTINUOUS);
                        LogManager.LogInformation("Tasks completed. System can now suspend.");
                    }
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

    private void NavView_Navigate(string navItemTag)
    {
        if (navItemTag == "WindowsExit")
        {
            appClosing = true;
            Close();
            return;
        }

        NavigationViewItem? selectedItem = navView.MenuItems
            .OfType<NavigationViewItem>()
            .FirstOrDefault(item => item.Tag?.ToString() == navItemTag);

        // is it a footer item ?
        selectedItem ??= navView.FooterMenuItems
            .OfType<NavigationViewItem>()
            .FirstOrDefault(item => item.Tag?.ToString() == navItemTag);

        // Find and select the matching menu item
        navView.SelectedItem = selectedItem;

        // Give gamepad focus
        gamepadFocusManager?.Focus((NavigationViewItem)selectedItem);

        var item = _pages.FirstOrDefault(p => p.Key.Equals(navItemTag));
        var _page = item.Value;

        // Get the page type before navigation so you can prevent duplicate
        // entries in the backstack.
        var preNavPageType = ContentFrame.CurrentSourcePageType;

        // Only navigate if the selected page isn't currently loaded.
        if (_page is not null && !Equals(preNavPageType, _page))
            NavView_Navigate(_page);
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

    private async void Window_Closed(object sender, EventArgs e)
    {
        // wait until all managers have initialized
        if (ManagerFactory.Managers.Any(manager => manager.Status.HasFlag(ManagerStatus.Initializing)))
        {
            LogManager.LogWarning("Waiting for all managers to be fully initialized before halting them");

            while (ManagerFactory.Managers.Any(manager => manager.Status.HasFlag(ManagerStatus.Initializing)))
                await Task.Delay(250).ConfigureAwait(false);
        }

        currentDevice.Close();

        UnloadNotifyIcon();

        sensorTimer?.Stop();
        sensorTimer = null;

        notifyIconWaitTimer?.Stop();
        notifyIconWaitTimer = null;

        // manage events
        SystemManager.SystemStatusChanged -= OnSystemStatusChanged;
        ManagerFactory.deviceManager.UsbDeviceArrived -= GenericDeviceUpdated;
        ManagerFactory.deviceManager.UsbDeviceRemoved -= GenericDeviceUpdated;
        ManagerFactory.notificationManager.Added -= NotificationManagerUpdated;
        ManagerFactory.notificationManager.Discarded -= NotificationManagerUpdated;
        ControllerManager.ControllerSelected -= ControllerManager_ControllerSelected;

        // UI thread
        UIHelper.TryInvoke(() =>
        {
            // stop windows
            overlayModel.Close(true);
            overlayTrackpad.Close();
            overlayquickTools.Close(true);
            overlayStatusBar.Exit();

            // stop pages
            controllerPage.Page_Closed();
            profilesPage.Page_Closed();
            settingsPage.Page_Closed();
            overlayPage.Page_Closed();
            hotkeysPage.Page_Closed();
            layoutPage.Page_Closed();
            notificationsPage.Page_Closed();
            libraryPage.Page_Closed();
        });

        // remove all automation event handlers
        ProcessUtils.TaskWithTimeout(() => Automation.RemoveAllEventHandlers(), TimeSpan.FromSeconds(3));

        foreach (IManager manager in ManagerFactory.Managers)
            manager.Stop();

        // stop managers
        VirtualManager.Stop();
        MotionManager.Stop();
        SensorsManager.Stop();
        ControllerManager.Stop();
        InputsManager.Stop(true);
        TimerManager.Stop();
        OSDManager.Stop();
        SystemManager.Stop();
        DynamicLightingManager.Stop();
        ToastManager.Stop();
        TaskManager.Stop();
        PerformanceManager.Stop();
        UpdateManager.Stop();

        Application.Current.Shutdown();
        await Task.Delay(2500).ConfigureAwait(false);
        Environment.Exit(0);
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
                //ManagerFactory.settingsManager.Set("MainWindowWidth", SystemParameters.MaximizedPrimaryScreenWidth);
                //ManagerFactory.settingsManager.Set("MainWindowHeight", SystemParameters.MaximizedPrimaryScreenHeight);

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
                notifyIcon.Visible = Homepage_Loaded;
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

    private const string HomeKey = "LibraryPage";
    private readonly CancellationTokenSource _preloadCts = new();

    private async void navView_Loaded(object sender, RoutedEventArgs e)
    {
        ContentFrame.Navigated += On_Navigated;

        // Preload HOME first (invisible, in PreloadFrame)
        if (_pages.TryGetValue(HomeKey, out var homePage))
        {
            await PreloadInHiddenFrameAsync(PreloadFrame, homePage);

            // IMPORTANT: detach from hidden frame before showing it in the visible one
            if (ReferenceEquals(PreloadFrame.Content, homePage))
                PreloadFrame.Content = null;

            // Show Home in the visible frame
            NavigateToPage(HomeKey);

            // Now that Home is really ready, reveal window & run your callback
            HomePage_Loaded();
        }

        // Warm the rest in the background (non-blocking)
        if (PreloadPages)
            _ = PreloadRemainingAsync(_preloadCts.Token);
    }

    private async Task PreloadRemainingAsync(CancellationToken ct)
    {
        foreach (var kvp in _pages)
        {
            if (ct.IsCancellationRequested) break;
            if (kvp.Key == HomeKey) continue;

            var page = kvp.Value;

            // Already warmed once? skip.
            if (page.IsLoaded && !ReferenceEquals(PreloadFrame.Content, page))
                continue;

            try
            {
                await PreloadInHiddenFrameAsync(PreloadFrame, page);
            }
            catch (Exception ex)
            {
                LogManager.LogError("Background preload failed for {0}", page.GetType().Name);
            }
        }

        // Optional: release the last preloaded page
        PreloadFrame.Content = null;
    }

    private static Task PreloadInHiddenFrameAsync(Frame hiddenFrame, Page page)
    {
        // If this page has already been loaded once in the app lifetime,
        // we're warmed; no need to wait again.
        if (page.IsLoaded && !ReferenceEquals(hiddenFrame.Content, page))
            return Task.CompletedTask;

        var tcs = new TaskCompletionSource<object?>();

        RoutedEventHandler? onLoaded = null;
        onLoaded = (s, e) =>
        {
            page.Loaded -= onLoaded;
            LogManager.LogInformation("Preloaded: {0}", page.GetType().Name);
            tcs.TrySetResult(null);
        };

        // Subscribe before navigating
        page.Loaded += onLoaded;

        // Navigate the hidden frame to the *existing* instance
        hiddenFrame.Navigate(page);

        return tcs.Task;
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
        PaneText.Text = Properties.Resources.MainWindow_CloseNavigation;
    }

    private void navView_PaneClosed(NavigationView sender, object args)
    {
        // todo: localize me
        PaneText.Text = Properties.Resources.MainWindow_OpenNavigation;
    }

    private void On_Navigated(object sender, NavigationEventArgs e)
    {
        if (ContentFrame.SourcePageType is not null)
        {
            CurrentPageName = ContentFrame.CurrentSourcePageType.Name;

            // Update previous navigation item
            prevNavItemTag = CurrentPageName;

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
