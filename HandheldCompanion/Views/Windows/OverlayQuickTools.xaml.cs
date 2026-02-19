using HandheldCompanion.Controllers;
using HandheldCompanion.Devices;
using HandheldCompanion.Functions;
using HandheldCompanion.Helpers;
using HandheldCompanion.Inputs;
using HandheldCompanion.Managers;
using HandheldCompanion.Misc;
using HandheldCompanion.Platforms.Misc;
using HandheldCompanion.Shared;
using HandheldCompanion.Utils;
using HandheldCompanion.ViewModels;
using HandheldCompanion.Views.Classes;
using HandheldCompanion.Views.QuickPages;
using iNKORE.UI.WPF.Modern.Controls;
using RTSSSharedMemoryNET;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Navigation;
using System.Windows.Threading;
using Windows.System.Power;
using WindowsDisplayAPI;
using WpfScreenHelper;
using WpfScreenHelper.Enum;
using static HandheldCompanion.WinAPI;
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
    // animation state
    private bool _isAnimating;
    private double _targetTop;   // on-screen Y
    private double _hiddenTop;   // off-screen Y (just below taskbar/work-area)

    // high-FPS animation state (keep your existing ones if present)
    private EventHandler _renderTick;
    private bool _animActive;
    private bool _animIsShowing;
    private TimeSpan _animDuration;
    private double _animFrom, _animTo;
    private readonly Stopwatch _animWatch = new();
    private Action? _animOnComplete;
    private double _animSpeed = 1.0; // 1.0 = normal, 1.25 = slower, 0.85 = faster

    private int QuickToolsLocation = 0;
    private bool HasAnimation = false;

    // page vars
    private readonly Dictionary<string, Page> _pages = [];

    private bool autoHide;
    private bool isClosing;

    private readonly DispatcherTimer clockUpdateTimer;

    public QuickHomePage homePage;
    public QuickDevicePage devicePage;
    public QuickPerformancePage performancePage;
    public QuickProfilesPage profilesPage;
    public QuickOverlayPage overlayPage;
    public QuickApplicationsPage applicationsPage;
    public QuickKeyboardPage keyboardPage;

    private static OverlayQuickTools currentWindow;

    // Cached hardware metrics (protected by metricsLock)
    private static readonly object metricsLock = new object();
    private static HardwareMetrics currentMetrics = new();

    public string prevNavItemTag;

    public OverlayQuickTools()
    {
        DataContext = new OverlayQuickToolsViewModel(this);
        InitializeComponent();

        currentWindow = this;

        // used by gamepad navigation
        Tag = "QuickTools";

        Width = (int)Math.Max(MinWidth, ManagerFactory.settingsManager.Get<double>("QuickToolsWidth"));
        Height = (int)Math.Max(MinHeight, ManagerFactory.settingsManager.Get<double>("QuickToolsHeight"));

        clockUpdateTimer = new(
            DispatcherPriority.Normal,
            Dispatcher.CurrentDispatcher
            )
        {
            IsEnabled = false,
            Interval = TimeSpan.FromMilliseconds(1000)
        };
        clockUpdateTimer.Tick += UpdateTime;

        // manage events
        SystemManager.PowerStatusChanged += PowerManager_PowerStatusChanged;
        ManagerFactory.multimediaManager.DisplaySettingsChanged += MultimediaManager_DisplaySettingsChanged;
        ControllerManager.ControllerSelected += ControllerManager_ControllerSelected;
        ManagerFactory.processManager.RawForeground += ProcessManager_RawForeground;

        // raise events
        switch (ManagerFactory.settingsManager.Status)
        {
            default:
            case ManagerStatus.Initializing:
                ManagerFactory.settingsManager.Initialized += SettingsManager_Initialized;
                break;
            case ManagerStatus.Initialized:
                QuerySettings();
                break;
        }



        switch (ManagerFactory.platformManager.Status)
        {
            default:
            case ManagerStatus.Initializing:
                ManagerFactory.platformManager.Initialized += PlatformManager_Initialized;
                break;
            case ManagerStatus.Initialized:
                QueryPlatforms();
                break;
        }

        // raise events
        if (ControllerManager.HasTargetController)
            ControllerManager_ControllerSelected(ControllerManager.GetTarget());

        // load gamepad navigation manager
        gamepadFocusManager = new(this, ContentFrame);
    }


    private void QueryPlatforms()
    {

        PlatformManager.LibreHardware.HardwareMetricsChanged += LibreHardware_HardwareMetricsChanged;
    }

    private void LibreHardware_HardwareMetricsChanged(HardwareMetrics metrics)
    {
        lock (metricsLock)
        {
            currentMetrics = metrics;
        }

        // UI thread
        UIHelper.TryInvoke(() =>
        {
            ShowBattery();
            ShowPerformance();
        });
    }

    private void PlatformManager_Initialized()
    {
        QueryPlatforms();
    }

    protected virtual void QuerySettings()
    {
        // manage events
        ManagerFactory.settingsManager.SettingValueChanged += SettingsManager_SettingValueChanged;

        // raise events
        SettingsManager_SettingValueChanged("QuickToolsLocation", ManagerFactory.settingsManager.Get<string>("QuickToolsLocation"), false);
        SettingsManager_SettingValueChanged("QuickToolsAutoHide", ManagerFactory.settingsManager.Get<string>("QuickToolsAutoHide"), false);
        SettingsManager_SettingValueChanged("QuickToolsDevicePath", ManagerFactory.settingsManager.Get<string>("QuickToolsDevicePath"), false);
        SettingsManager_SettingValueChanged("QuickToolsSlideAnimation", ManagerFactory.settingsManager.Get<string>("QuickToolsSlideAnimation"), false);
    }

    protected virtual void SettingsManager_Initialized()
    {
        QuerySettings();
    }

    private void ProcessManager_RawForeground(nint hWnd)
    {
        if (hWnd != hWndSource.Handle && autoHide)
        {
            // UI thread
            UIHelper.TryInvoke(() => SlideHide());
        }
    }

    public void loadPages()
    {
        // create pages
        homePage = new("quickhome");
        devicePage = new("quickdevice");
        profilesPage = new("quickprofiles");
        applicationsPage = new("quickapplications");
        keyboardPage = new("quickkeyboard");

        _pages.Add("QuickHomePage", homePage);
        _pages.Add("QuickDevicePage", devicePage);
        _pages.Add("QuickProfilesPage", profilesPage);
        _pages.Add("QuickApplicationsPage", applicationsPage);
        _pages.Add("QuickKeyboardPage", keyboardPage);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var exStyles = GetWindowLong(hWndSource.Handle, (int)GETWINDOWLONG.GWL_EXSTYLE);
        SetWindowLong(hWndSource.Handle, (int)GETWINDOWLONG.GWL_EXSTYLE, (int)(exStyles | (uint)WINDOWSTYLE.WS_EX_NOACTIVATE));
        SetWindowPos(hWndSource.Handle, (int)SWPZORDER.HWND_TOPMOST, 0, 0, 0, 0, (uint)SETWINDOWPOS.SWP_NOMOVE | (uint)SETWINDOWPOS.SWP_NOSIZE | (uint)WINDOWSTYLE.WS_EX_NOACTIVATE);

        UpdateLocation();
        if (HasAnimation && ShouldSlideFromBottom())
            Top = _hiddenTop;   // start off-screen only when animating
        else
            Top = _targetTop;   // otherwise start at the resting Y
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

    private void SettingsManager_SettingValueChanged(string name, object value, bool temporary)
    {
        // UI thread
        UIHelper.TryInvoke(() =>
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
                case "QuickToolsSlideAnimation":
                    HasAnimation = Convert.ToBoolean(value);
                    break;
            }
        });
    }

    private void ControllerManager_ControllerSelected(IController Controller)
    {
        // UI thread
        UIHelper.TryInvoke(() =>
        {
            QTLB.Glyph = Controller.GetGlyph(ButtonFlags.L1);
            QTRB.Glyph = Controller.GetGlyph(ButtonFlags.R1);
        });
    }

    private void MultimediaManager_DisplaySettingsChanged(Display desktopScreen)
    {
        UpdateLocation();
    }

    private const double _MaxHeight = 960;
    private const double _MaxWidth = 960;
    private const WindowStyle _Style = WindowStyle.ToolWindow;
    private double _Top = 0;
    private double _Left = 0;
    private Screen targetScreen;

    private void UpdateLocation()
    {
        // pull quicktools settings
        var QuickToolsLocation = ManagerFactory.settingsManager.Get<int>("QuickToolsLocation");
        string DevicePath = ManagerFactory.settingsManager.Get<string>("QuickToolsDevicePath");
        string DeviceName = ManagerFactory.settingsManager.Get<string>("QuickToolsDeviceName");

        // Attempt to find the screen with the specified friendly name
        var targetDisplay = ScreenControl.AllDisplays.FirstOrDefault(display =>
                                display.DevicePath.Equals(DevicePath) ||
                                display.ToPathDisplayTarget().FriendlyName.Equals(DeviceName)) ?? ScreenControl.PrimaryDisplay;

        // Find the corresponding Screen object
        targetScreen = targetDisplay is null
            ? Screen.PrimaryScreen
            : Screen.AllScreens.FirstOrDefault(screen => screen.DeviceName.Equals(targetDisplay.DeviceName, StringComparison.OrdinalIgnoreCase)) ?? Screen.PrimaryScreen;

        // UI thread
        UIHelper.TryInvoke(() =>
        {
            // Common settings across cases 0 and 1
            MaxWidth = (int)Math.Min(_MaxWidth, targetScreen.WpfBounds.Width);
            Width = 550; // (int)Math.Max(MinWidth, ManagerFactory.settingsManager.GetDouble("QuickToolsWidth"));
            MaxHeight = Math.Min(targetScreen.WpfBounds.Height - (Margin.Top + Margin.Bottom), _MaxHeight);
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

            switch (QuickToolsLocation)
            {
                case 0: // TopLeft
                    this.SetWindowPosition(WindowPositions.TopLeft, targetScreen);
                    break;

                case 1: // TopRight
                    this.SetWindowPosition(WindowPositions.TopRight, targetScreen);
                    break;

                case 2: // Maximized
                    this.SetWindowPosition(WindowPositions.Maximize, targetScreen);
                    break;

                case 3: // BottomLeft
                    this.SetWindowPosition(WindowPositions.BottomLeft, targetScreen);
                    break;

                case 4: // BottomRight
                    this.SetWindowPosition(WindowPositions.BottomRight, targetScreen);
                    break;

                case 5: // BottomCenter
                    this.SetWindowPosition(WindowPositions.Bottom, targetScreen);
                    Width = 640;
                    break;
            }

            switch (QuickToolsLocation)
            {
                case 0: // TopLeft
                    Top += Margin.Top;
                    Left += Margin.Left;
                    break;

                case 1: // TopRight
                    Top += Margin.Top;
                    Left -= Margin.Right;
                    break;

                case 3: // BottomLeft
                    Top -= Margin.Bottom;
                    Left += Margin.Left;
                    break;

                case 4: // BottomRight
                    Top -= Margin.Bottom;
                    Left -= Margin.Right;
                    break;

                case 5: // BottomCenter
                    Top -= Margin.Bottom;
                    Left = (targetScreen.WpfBounds.Width / 2) - (Width / 2);
                    break;
            }

            // used by SlideIn/SlideOut
            _Top = Top;
            _Left = Left;
        });

        // WpfBounds "bottom" = Top + Height
        double workTop = targetScreen.WpfBounds.Top;
        double workHeight = targetScreen.WpfBounds.Height;
        double workBottom = workTop + workHeight;

        // when sliding, we want to end at _Top and start just below the work area (to avoid flicker)
        _targetTop = _Top;
        _hiddenTop = workBottom + 2;    // +2 so it’s truly off-screen

        UpdateStyle();
    }

    private bool ShouldSlideFromBottom() => QuickToolsLocation is 3 or 4 or 5;

    private void StartHighFpsSlide(double from, double to, bool isShowing, int durationMs, Action? onCompleted = null)
    {
        if (_animActive) StopHighFpsSlide(); // cancel any previous anim

        _animFrom = from;
        _animTo = to;
        _animIsShowing = isShowing;
        _animDuration = TimeSpan.FromMilliseconds(durationMs * _animSpeed);
        _animOnComplete = onCompleted;

        _animActive = true;
        _animWatch.Restart();

        _renderTick ??= OnRenderTick;
        CompositionTarget.Rendering += _renderTick;
    }

    private void StopHighFpsSlide()
    {
        if (!_animActive) return;

        CompositionTarget.Rendering -= _renderTick;
        _animActive = false;
        _animWatch.Stop();

        // fire completion exactly once
        var cb = _animOnComplete;
        _animOnComplete = null;
        cb?.Invoke();
    }

    private void OnRenderTick(object? sender, EventArgs e)
    {
        // progress 0..1 (time-based, not frame-based)
        double p = Math.Clamp(_animWatch.Elapsed.TotalMilliseconds / _animDuration.TotalMilliseconds, 0.0, 1.0);

        // Start-menu vibe: ease-out on show, ease-in on hide
        p = _animIsShowing ? EaseOutExpo(p) : EaseInExpo(p);

        double y = _animFrom + (_animTo - _animFrom) * p;
        Top = Math.Round(y); // snap to device pixels

        if (p >= 1.0)
        {
            Top = Math.Round(_animTo);  // ensure final
            StopHighFpsSlide();         // <-- completion (may call Hide())
        }
    }

    // Easing
    private static double EaseOutExpo(double p) => (p >= 1.0) ? 1.0 : 1 - Math.Pow(2, -10 * p);
    private static double EaseInExpo(double p) => (p <= 0.0) ? 0.0 : Math.Pow(2, 10 * (p - 1));

    private void ShowInstant()
    {
        UpdateLocation();
        Left = _Left;
        Top = _targetTop;
        try { Show(); } catch { }
    }

    private void HideInstant()
    {
        try { Hide(); } catch { }
        // keep resting Y ready for next show
        Top = _targetTop;
    }

    public void SlideShow()
    {
        if (!HasAnimation || !ShouldSlideFromBottom())
        {
            ShowInstant();
            return;
        }

        if (_isAnimating || _animActive) return;

        UpdateLocation();
        Left = _Left;

        if (!IsVisible) try { Show(); } catch { }

        Top = _hiddenTop;
        StartHighFpsSlide(_hiddenTop, _targetTop, isShowing: true, durationMs: 300);
    }

    public void SlideHide()
    {
        if (!HasAnimation || !ShouldSlideFromBottom())
        {
            HideInstant();
            return;
        }

        if (_isAnimating || _animActive) return;

        StartHighFpsSlide(Top, _hiddenTop, isShowing: false, durationMs: 220, onCompleted: () =>
        {
            try { Hide(); } catch { }
            Top = _targetTop;
        });
    }


    private bool _bypassWmPaintThrottle;

    private void BeginAnimationBypassWmPaint(bool on)
    {
        _bypassWmPaintThrottle = on;
        if (on)
            RenderOptions.ProcessRenderMode = RenderMode.Default; // ensure HW path during anim
    }

    private void PowerManager_PowerStatusChanged(PowerStatus status)
    {
        // UI thread (async)
        //lastBatteryRefresh = 0;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        gamepadFocusManager.Loaded();
    }

    protected override nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        // prevent activation on mouse click
        if (msg == (int)WINDOWMESSAGE.WM_MOUSEACTIVATE)
        {
            handled = true;
            return new IntPtr((uint)MOUSEACTIVATE.MA_NOACTIVATE);
        }

        switch (msg)
        {
            case (int)WINDOWMESSAGE.WM_INPUTLANGCHANGE:
                break;

            case (int)WINDOWMESSAGE.WM_SYSCOMMAND:
                {
                    int command = wParam.ToInt32() & 0xfff0;
                    if (command == (int)SYSCOMMAND.SC_MOVE)
                        handled = true;
                }
                break;

            case (int)WINDOWMESSAGE.WM_ACTIVATE:
                {
                    handled = true;
                    UpdateStyle();
                }
                break;
        }

        return base.WndProc(hwnd, msg, wParam, lParam, ref handled);
    }

    public void SetVisibility(Visibility visibility)
    {
        // UI thread
        UIHelper.TryInvoke(() =>
        {
            this.Visibility = visibility;
        });
    }

    public void ToggleVisibility()
    {
        UIHelper.TryInvoke(() =>
        {
            bool canAnimate = HasAnimation && ShouldSlideFromBottom();

            if (!IsVisible || Visibility != Visibility.Visible)
            {
                if (canAnimate) SlideShow();
                else ShowInstant();
            }
            else
            {
                if (canAnimate) SlideHide();
                else HideInstant();
            }
        });
    }

    protected override void Window_VisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        switch (Visibility)
        {
            case Visibility.Collapsed:
            case Visibility.Hidden:
                InvokeLostGamepadWindowFocus();
                clockUpdateTimer.Stop();
                break;

            case Visibility.Visible:
                if (HasAnimation && ShouldSlideFromBottom())
                {
                    // make sure we’re not double‑starting an anim
                    if (!_animActive) SlideShow();
                }

                UpdateStyle();

                InvokeGotGamepadWindowFocus();
                clockUpdateTimer.Start();
                UpdateTime(sender, EventArgs.Empty);
                break;
        }

        base.Window_VisibleChanged(sender, e);
    }

    public void UpdateStyle()
    {
        SendMessage(hWndSource.Handle, (uint)WINDOWMESSAGE.WM_NCACTIVATE, (int)WINDOWMESSAGE.WM_NCACTIVATE, 0);
    }

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        // position and size settings
        ManagerFactory.settingsManager.Set("QuickToolsWidth", ActualWidth);

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

        homePage.Close();
        devicePage.Close();
        profilesPage.Close();
        applicationsPage.Close();
    }

    #region navView

    private void navView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.InvokedItemContainer is not null)
        {
            var navItem = (NavigationViewItem)args.InvokedItemContainer;
            var navItemTag = (string)navItem.Tag;

            // navigate
            NavView_Navigate(navItemTag);
        }
    }

    private void NavView_Navigate(string navItemTag)
    {
        // Find and select the matching menu item
        navView.SelectedItem = navView.MenuItems
            .OfType<NavigationViewItem>()
            .FirstOrDefault(item => item.Tag?.ToString() == navItemTag);

        // Give gamepad focus
        gamepadFocusManager.Focus((NavigationViewItem)navView.SelectedItem);

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

    public void NavView_Navigate(Page _page)
    {
        ContentFrame.Navigate(_page);
    }

    private void navView_Loaded(object sender, RoutedEventArgs e)
    {
        // Add handler for ContentFrame navigation.
        ContentFrame.Navigated += On_Navigated;

        // navigate
        NavigateToPage("QuickHomePage");
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
        if (ContentFrame.SourcePageType is not null)
        {
            // Update previous navigation item
            prevNavItemTag = ContentFrame.CurrentSourcePageType.Name;
        }
    }

    private void UpdateTime(object? sender, EventArgs e)
    {
        // UI thread
        UIHelper.TryInvoke(() =>
        {
            Time.Text = $"{DateTime.Now.ToString(CultureInfo.InstalledUICulture.DateTimeFormat.ShortTimePattern).ToLowerInvariant()}";
            //ShowRadios();
        });
    }

    private static string AddElementIfNotNull(float value, string unit)
    {
        if (!float.IsNaN(value))
            return $"{OverlayEntryElement.FormatValue(value, unit)}{unit}";
        return string.Empty;
    }


    private static string AddElementIfNotNull(float value, float available, string unit)
    {
        if (!float.IsNaN(value) && !float.IsNaN(available))
            return OverlayEntryElement.FormatValue(value, unit) + "/" + OverlayEntryElement.FormatValue(available, unit) + unit;
        return string.Empty;
    }

    private static string AddElementIfNotNull((float, string) value, (float, string) available)
    {
        if (!float.IsNaN(value.Item1) && !float.IsNaN(available.Item1))
            return
                $"{OverlayEntryElement.FormatValue(value.Item1, value.Item2)}" +
                $"{(value.Item2 == available.Item2 ? string.Empty : $"{value.Item2}")}" +
                $"/{OverlayEntryElement.FormatValue(available.Item1, available.Item2)}{available.Item2}";
        return string.Empty;
    }

    private const int MB_TO_BYTES = 1024 * 1024;


    private void ShowPerformance()
    {
        CPUName.Text = string.Join(" ",
            $"{AddElementIfNotNull(currentMetrics.CpuLoad, currentMetrics.CpuLoadMax, "%")}",
            $"{AddElementIfNotNull(OSDManager.NormalizeClock(currentMetrics.CpuClock), OSDManager.NormalizeClock(currentMetrics.CpuClockMax))}",
            //);
            //CPUPower.Text = string.Join(" ",
            $"{AddElementIfNotNull(currentMetrics.CpuTemp, "°C")}",
            $"{AddElementIfNotNull(currentMetrics.CpuPower, "W")}");
        GPUName.Text = string.Join(" ",
            $"{AddElementIfNotNull(currentMetrics.GpuLoad, "%")}",
            $"{AddElementIfNotNull(OSDManager.NormalizeClock(currentMetrics.GpuClock).Item1, OSDManager.NormalizeClock(currentMetrics.GpuClock).Item2)}",
            $"{AddElementIfNotNull(currentMetrics.GpuTemp, "°C")}"
            );

        Net.Text = string.Join(" ",
            $"{AddElementIfNotNull(OSDManager.NormalizeSpeed(currentMetrics.NetworkSpeedDown).Item1, OSDManager.NormalizeSpeed(currentMetrics.NetworkSpeedDown).Item2 + "↓")}",
            $"{AddElementIfNotNull(OSDManager.NormalizeSpeed(currentMetrics.NetworkSpeedUp).Item1, OSDManager.NormalizeSpeed(currentMetrics.NetworkSpeedUp).Item2 + "↑")}"
            );

        Memory.Text = string.Join(" ",
            $"{AddElementIfNotNull(OSDManager.NormalizeBytes(currentMetrics.MemUsed * MB_TO_BYTES), OSDManager.NormalizeBytes((currentMetrics.MemUsed + currentMetrics.MemAvailable) * MB_TO_BYTES))}");

        VRAM.Text = string.Join(" ",
            $"{AddElementIfNotNull(OSDManager.NormalizeBytes((currentMetrics.GpuMemDedicated + currentMetrics.GpuMemShared) * MB_TO_BYTES), OSDManager.NormalizeBytes((currentMetrics.GpuMemDedicated + currentMetrics.GpuMemDedicatedAvailable) * MB_TO_BYTES))}");

    }

    
    private void ShowBattery()
    {
        if (!float.IsNaN(currentMetrics.BattCapacity))
        {
            BatteryIndicatorPercentage.Text = $"{AddElementIfNotNull(currentMetrics.BattCapacity, "")}%";
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
            var keyValue = (int)currentMetrics.BattCapacity / 10;

            // set key
            var key = $"Battery{keyStatus}{keyValue}";

            if (SystemManager.PowerStatusIcon.TryGetValue(key, out var glyph))
                BatteryIndicatorIcon.Glyph = glyph;
        }

        if (float.IsNaN(currentMetrics.BattPower) && currentMetrics.BattCapacity == 100f)
        {
            BatteryIndicatorPercentage.Text += $" (fully charged [{currentMetrics.BattRemainingCapacity}mWh, {currentMetrics.BattHealth:00}%])";
            return;
        }

        BatteryIndicatorPercentage.Text += $" [{Math.Round(currentMetrics.BattPower, 1)}W]";

        var time = currentMetrics.BattTime;
        if (!float.IsNaN(time))
            BatteryIndicatorPercentage.Text += $" ({TimeSpan.FromMinutes(time):h\\h\\ m\\m} {(currentMetrics.BattPower > 0 ? "until full" : "remaining")})";

    }

    internal nint GetHandle()
    {
        return hWndSource.Handle;
    }

    #endregion

    private void QuicKeyboard_Click(object sender, RoutedEventArgs e)
    {
        NavView_Navigate("QuickKeyboardPage");
    }

    private void QuickTrackpad_Click(object sender, RoutedEventArgs e)
    {
        NavView_Navigate("QuickTrackpadPage");
    }

    private void QuickGoBack_Click(object sender, RoutedEventArgs e)
    {
        TryGoBack();
    }
}