using HandheldCompanion.Extensions;
using HandheldCompanion.Functions;
using HandheldCompanion.Helpers;
using HandheldCompanion.Managers;
using HandheldCompanion.Platforms.Misc;
using HandheldCompanion.Shared;
using HandheldCompanion.Utils;
using HandheldCompanion.Views.Classes;
using Sentry.Protocol;
using System;
using System.DirectoryServices.ActiveDirectory;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Windows.Gaming.UI;
using static HandheldCompanion.WinAPI;
using static Microsoft.WindowsAPICodePack.Shell.PropertySystem.SystemProperties.System;

namespace HandheldCompanion.Views.Windows;

/// <summary>
/// Interaction logic for OverlayToast.xaml
/// </summary>
public partial class OverlayStatusBar : OverlayWindow
{
    public nint hWnd;
    public OverlayStatusBar()
    {
        InitializeComponent();
        this.Title = "Bar";
        this.WindowStyle = WindowStyle.None;
        this.AllowsTransparency = true;
        this.Topmost = true;
        this.ShowActivated = false;
        this.ShowInTaskbar = false;
        // WPF event sequence
        // https://memories3615.wordpress.com/2017/03/24/wpf-window-events-sequence/
        SourceInitialized += (s, e) =>
        {
            hWnd = new WindowInteropHelper(this).Handle;
            WindowInit(); // needs hWnd
            this.Hook(WndProc);
        };

        Loaded += OverlayStatusBar_Loaded;

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
    }

    private nint WndProc(nint hWnd, int msg, nint wparam, nint lparam, ref bool handled)
    {
        switch (msg)
        {
            case (int)WINDOWMESSAGE.WM_ACTIVATE:
                Shell32.SHAppBarMessage((int)APPBARMESSAGE.Activate, ref abd);
                break;
        }

        switch (wparam)
        {
            case (int)APPBARNOTIFY.ABN_POSCHANGED:
                AppbarSetPos();
                break;
            case (int)APPBARNOTIFY.ABN_FULLSCREENAPP:
                if (lparam > 0) // fullscreen app is opening
                {
                    this.Topmost = false;
                    User32.SetWindowPos(this.hWnd, (nint)SWPZORDER.HWND_BOTTOM, 0, 0, 0, 0, SETWINDOWPOS.SWP_NOMOVE | SETWINDOWPOS.SWP_NOSIZE | SETWINDOWPOS.SWP_NOACTIVATE);
                }
                else // revert back to topmost once fullscreen app closes
                {
                    User32.SetWindowPos(this.hWnd, (nint)SWPZORDER.HWND_TOPMOST, 0, 0, 0, 0, SETWINDOWPOS.SWP_NOMOVE | SETWINDOWPOS.SWP_NOSIZE | SETWINDOWPOS.SWP_NOACTIVATE);
                    this.Topmost = true;
                }
                break;
        }
        return 0;
    }

    private readonly object metricsLock = new object();
    private HardwareMetrics currentMetrics = new();

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
            cpuTextBlock.Text = $"CPU: {string.Join(" ",
                $"{OverlayEntryElement.FormatValue(currentMetrics.CpuLoad, "%")}%"
                , $"{OverlayEntryElement.FormatValue(currentMetrics.CpuPower, "W")}W"
                , $"{OverlayEntryElement.FormatValue(currentMetrics.CpuTemp, "°C")}°C")}";
            gpuTextBlock.Text = $"GPU: {string.Join(" ",
                $"{OverlayEntryElement.FormatValue(currentMetrics.GpuLoad, "%")}%"
                , $"{OverlayEntryElement.FormatValue(currentMetrics.GpuPower, "W")}W"
                , $"{OverlayEntryElement.FormatValue(currentMetrics.GpuTemp, "°C")}°C")}";
            memTextBlock.Text = $"MEM: {OverlayEntryElement.FormatValue(OSDManager.NormalizeBytes(currentMetrics.MemUsed * 1024 * 1024).Item1, OSDManager.NormalizeBytes(currentMetrics.MemUsed * 1024 * 1024).Item2)}{OSDManager.NormalizeBytes(currentMetrics.MemUsed * 1024 * 1024).Item2}";
        });
    }



    private void PlatformManager_Initialized()
    {
        QueryPlatforms();
    }

    public void ToggleVisibility()
    {

        UIHelper.TryInvoke(() =>
        {
            if (Visibility == Visibility.Visible)
            {
                Hide();
                UnregisterAsAppbar();
            }
            else
            {
                Show();
                RegisterAsAppbar();
            }
        });
    }

    public static double scale;
    bool barTransparent = false;
    public static int screenWidth;
    public static int screenHeight;
    public void WindowInit()
    {
        screenWidth = GetSystemMetrics(0);
        screenHeight = GetSystemMetrics(1);

        // get the scalefactor of the primary monitor
        scale = WPFUtils.GetDisplayScaling();
        screenWidth = (int)(screenWidth / scale);
        screenHeight = (int)(screenHeight / scale);

        // Make bar a toolwindow (appear always on top)
        // TODO: loses topmost to other windows when task manager is open
        var exStyles = User32.GetWindowLong(hWnd, GETWINDOWLONG.GWL_EXSTYLE);
        User32.SetWindowLong(hWnd, (int)GETWINDOWLONG.GWL_EXSTYLE, (int)(exStyles | (uint)WINDOWSTYLE.WS_EX_TOOLWINDOW));

        WPFUtils.HideWindowInAltTab(hWnd);

        Background = BrushFromHex("#28282866");
        BorderBrush = BrushFromHex("#ffffff");
        BorderThickness = new Thickness(0);

        Width = screenWidth;
        Height = 30;
        Left = 0;
        Top = 0;
    }

    // cleanup and exit
    public void Exit()
    {
        UnregisterAsAppbar();
    }

    /// <summary>
	/// Allows us to claim desktop real estate
	/// Does not work in SourceInitialized, needs at lest Loaded
	/// </summary>
	APPBARDATA abd = new();
    public void RegisterAsAppbar()
    {
        abd.cbSize = (uint)Marshal.SizeOf<APPBARDATA>();
        abd.hWnd = this.hWnd;
        abd.uCallbackMessage = User32.RegisterWindowMessage("HandheldCompanion");

        uint res = Shell32.SHAppBarMessage((uint)APPBARMESSAGE.New, ref abd);

        AppbarSetPos();
    }

    public void AppbarSetPos()
    {

        const int ABE_LEFT = 0;
        const int ABE_TOP = 1;
        const int ABE_RIGHT = 2;
        const int ABE_BOTTOM = 3;

        abd.uEdge = ABE_TOP;
        

        switch (abd.uEdge)
        {
            case ABE_LEFT or ABE_RIGHT:
                abd.rc = new() { Top = 0, Bottom = Convert.ToInt32(screenHeight * scale) };
                break;
            case ABE_TOP or ABE_BOTTOM:
                abd.rc = new() { Left = 0, Right = Convert.ToInt32(screenWidth * scale) };
                break;
        }

        uint res2 = Shell32.SHAppBarMessage((uint)APPBARMESSAGE.QueryPos, ref abd);
        
        // adjust
        switch (abd.uEdge)
        {
            case ABE_LEFT:
                //abd.rc.Right = abd.rc.Left + config.width;
                break;
            case ABE_TOP:
                abd.rc.Bottom = Convert.ToInt32((30 + 2 * 10) * scale);
                break;
            case ABE_RIGHT:
                //abd.rc.Left = abd.rc.Right - config.width;
                break;
            case ABE_BOTTOM:
                abd.rc.Top = Convert.ToInt32((screenHeight - 30 - 2 * 10) * scale);
                abd.rc.Bottom = Convert.ToInt32(screenHeight * scale);
                break;
        }

        uint res3 = Shell32.SHAppBarMessage((uint)APPBARMESSAGE.SetPos, ref abd); // rect must be in absolute pixels, i.e. scale has to be multiplied to both
                                                                                  // screen sizes and config sizes
        
        RectPrinter(abd.rc);
    }

    void RectPrinter(RECT rect)
    {
        LogManager.LogInformation($"Left: {rect.Left}, Top: {rect.Top}, Right: {rect.Right}, Bottom: {rect.Bottom}");
    }

    public void UnregisterAsAppbar()
    {
        uint res = Shell32.SHAppBarMessage((uint)APPBARMESSAGE.Remove, ref abd);
        LogManager.LogInformation($"UNREGISTERED AS APPBAR: {res}");
        firstShow = true;
    }

    bool firstShow = true;
    public Border cpuBorder = new();
    public TextBlock cpuTextBlock = new();

    public Border gpuBorder = new();
    public TextBlock gpuTextBlock = new();


    public Border memBorder = new();
    public TextBlock memTextBlock = new();


    public Border netBorder = new();
    public TextBlock netTextBlock = new();

    public DockPanel dockPanel = new();
    private void OverlayStatusBar_Loaded(object sender, RoutedEventArgs e)
    {
        if (firstShow)
        {
            RegisterAsAppbar();
            cpuBorder.VerticalAlignment =
                gpuBorder.VerticalAlignment =
                memBorder.VerticalAlignment =
                netBorder.VerticalAlignment = VerticalAlignment.Center;
            cpuBorder.Margin =
                gpuBorder.Margin =
                memBorder.Margin =
                netBorder.Margin = new Thickness(6, 0, 6, 0);
            cpuTextBlock.Foreground =
                gpuTextBlock.Foreground =
                memTextBlock.Foreground =
                netTextBlock.Foreground = BrushFromHex("#EBDBB2");
            cpuTextBlock.FontFamily =
                gpuTextBlock.FontFamily =
                memTextBlock.FontFamily =
                netTextBlock.FontFamily = new FontFamily("Unispace");
            cpuBorder.Child = cpuTextBlock;
            gpuBorder.Child = gpuTextBlock;
            memBorder.Child = memTextBlock;
            netBorder.Child = netTextBlock;
            dockPanel.Width = screenWidth;
            dockPanel.Children.Add(cpuBorder);
            dockPanel.Children.Add(gpuBorder);
            dockPanel.Children.Add(memBorder);
            dockPanel.Children.Add(netBorder);
            this.Content = dockPanel;
            firstShow = false;
        }
    }
}
