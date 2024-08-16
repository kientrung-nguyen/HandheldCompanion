using HandheldCompanion.Managers.Desktop;
using HandheldCompanion.Misc;
using HandheldCompanion.Utils;
using HandheldCompanion.Views.Windows;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.Media;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using WindowsDisplayAPI;
using WindowsDisplayAPI.DisplayConfig;

namespace HandheldCompanion.Managers;

public static class MultimediaManager
{

    public static Dictionary<string, DesktopScreen> AllScreens = new();
    public static DesktopScreen PrimaryDesktop;

    private static ScreenRotation screenOrientation;

    private static bool VolumeSupport;
    private static bool MicrophoneSupport;
    private static readonly bool BrightnessSupport;

    private static readonly bool NightLightSupport;


    public static bool IsInitialized;

    static MultimediaManager()
    {
        // setup the multimedia device and get current volume value
        SoundControl.SubscribeToEvents(VolumeNotificationEventArrived);
        VolumeSupport = SoundControl.AudioGet() != -1;
        MicrophoneSupport = SoundControl.MicrophoneGet() != -1;

        // check if we have control over brightness
        ScreenBrightness.SubscribeToEvents(BrightnessWatcherEventArrived);
        BrightnessSupport = ScreenBrightness.Get() != -1;

        NightLight.SubscribeToEvents(NightLightNotificationEventArrived);
        NightLightSupport = NightLight.Get() != -1;

        // manage events
        SystemEvents.DisplaySettingsChanged += SystemEvents_DisplaySettingsChanged;
        SettingsManager.SettingValueChanged += SettingsManager_SettingValueChanged;
        HotkeysManager.CommandExecuted += HotkeysManager_CommandExecuted;
    }

    private static void SettingsManager_SettingValueChanged(string name, object value)
    {
        switch (name)
        {
            case "NativeDisplayOrientation":
                {
                    ScreenRotation.Rotations nativeOrientation = (ScreenRotation.Rotations)Convert.ToInt32(value);

                    if (!IsInitialized)
                        return;

                    ScreenRotation.Rotations oldOrientation = screenOrientation.rotation;
                    screenOrientation = new ScreenRotation(screenOrientation.rotationUnnormalized, nativeOrientation);

                    // Though the real orientation didn't change, raise event because the interpretation of it changed
                    if (oldOrientation != screenOrientation.rotation)
                        DisplayOrientationChanged?.Invoke(screenOrientation);
                }
                break;
        }
    }

    private static void HotkeysManager_CommandExecuted(string listener)
    {
        switch (listener)
        {
            case "increaseBrightness":
                ToastManager.RunToast($"{AdjustBrightness(5)}%", ToastIcons.BrightnessUp);
                break;
            case "decreaseBrightness":
                ToastManager.RunToast($"{AdjustBrightness(-5)}%", ToastIcons.BrightnessDown);
                break;
            case "increaseVolume":
                ToastManager.RunToast($"{AdjustVolume(5)}%", ToastIcons.VolumeUp);
                break;
            case "decreaseVolume":
                ToastManager.RunToast($"{AdjustVolume(-5)}%", ToastIcons.VolumeDown);
                break;
        }
    }

    private static void NightLightNotificationEventArrived(object? sender, RegistryChangedEventArgs e)
    {
        NightLightNotification?.Invoke(NightLight.Get() == 1);
    }

    private static void VolumeNotificationEventArrived(SoundDirections flow, float volume, bool muted)
    {
        VolumeNotification?.Invoke(flow, volume, muted);
    }

    private static void BrightnessWatcherEventArrived(object sender, EventArrivedEventArgs e)
    {
        int brightness = Convert.ToInt32(e.NewEvent.Properties["Brightness"].Value);
        BrightnessNotification?.Invoke(brightness);
    }

    public static string GetDisplayFriendlyName(string DeviceName)
    {
        string friendlyName = string.Empty;

        Display? PrimaryDisplay = Display.GetDisplays().Where(display => display.DisplayName.Equals(DeviceName)).FirstOrDefault();
        if (PrimaryDisplay is not null)
        {
            string DevicePath = PrimaryDisplay.DevicePath;
            PathDisplayTarget? PrimaryTarget = GetDisplayTarget(DevicePath);
            if (PrimaryTarget is not null)
                friendlyName = PrimaryTarget.FriendlyName;
        }

        return friendlyName;
    }

    public static string GetDisplayPath(string DeviceName)
    {
        string DevicePath = string.Empty;

        Display? PrimaryDisplay = Display.GetDisplays().Where(display => display.DisplayName.Equals(DeviceName)).FirstOrDefault();
        if (PrimaryDisplay is not null)
            DevicePath = PrimaryDisplay.DevicePath;

        return DevicePath;
    }

    private static PathDisplayTarget? GetDisplayTarget(string DevicePath)
    {
        PathDisplayTarget PrimaryTarget;
        PrimaryTarget = PathDisplayTarget.GetDisplayTargets().Where(target => target.DevicePath.Equals(DevicePath)).FirstOrDefault();
        return PrimaryTarget;
    }

    private static void SystemEvents_DisplaySettingsChanged(object? sender, EventArgs e)
    {
        // temporary array to store all current screens
        Dictionary<string, DesktopScreen> desktopScreens = new();

        foreach (Screen screen in Screen.AllScreens)
        {
            DesktopScreen desktopScreen = new(screen);

            // pull resolutions details
            List<DisplayDevice> resolutions = GetResolutions(desktopScreen.Screen.DeviceName);
            foreach (DisplayDevice mode in resolutions)
            {
                ScreenResolution res = new ScreenResolution(mode.dmPelsWidth, mode.dmPelsHeight, mode.dmBitsPerPel);

                List<int> frequencies = resolutions
                    .Where(a => a.dmPelsWidth == mode.dmPelsWidth && a.dmPelsHeight == mode.dmPelsHeight)
                    .Select(b => b.dmDisplayFrequency).Distinct().ToList();

                foreach (int frequency in frequencies)
                    res.Frequencies.Add(frequency, frequency);

                if (!desktopScreen.HasResolution(res))
                    desktopScreen.screenResolutions.Add(res);
            }

            // sort resolutions
            desktopScreen.SortResolutions();

            // add to temporary array
            desktopScreens.Add(desktopScreen.FriendlyName, desktopScreen);
        }

        // get refreshed primary screen (can't be null)
        DesktopScreen newPrimary = desktopScreens.Values.Where(a => a.IsPrimary).FirstOrDefault();

        // looks like we have a new primary screen
        if (PrimaryDesktop is null || !PrimaryDesktop.FriendlyName.Equals(newPrimary.FriendlyName))
        {
            // raise event (New primary display)
            PrimaryScreenChanged?.Invoke(newPrimary);
        }

        // set or update current primary
        PrimaryDesktop = newPrimary;

        // raise event (New screen detected)
        foreach (DesktopScreen desktop in desktopScreens.Values.Where(a => !AllScreens.ContainsKey(a.FriendlyName)))
            ScreenConnected?.Invoke(desktop);

        // raise event (New screen detected)
        foreach (DesktopScreen desktop in AllScreens.Values.Where(a => !desktopScreens.ContainsKey(a.FriendlyName)))
            ScreenDisconnected?.Invoke(desktop);

        // clear array and transfer screens
        AllScreens.Clear();
        foreach (DesktopScreen desktop in desktopScreens.Values)
            AllScreens.Add(desktop.FriendlyName, desktop);

        // raise event (Display settings were updated)
        if (PrimaryDesktop is not null)
        {
            ScreenResolution screenResolution = PrimaryDesktop.GetResolution();
            if (screenResolution is not null)
                DisplaySettingsChanged?.Invoke(PrimaryDesktop, screenResolution);
        }

        /*
        ScreenRotation.Rotations oldOrientation = screenOrientation.rotation;

        if (!IsInitialized)
        {
            ScreenRotation.Rotations nativeScreenRotation = (ScreenRotation.Rotations)SettingsManager.GetInt("NativeDisplayOrientation");
            screenOrientation = new ScreenRotation((ScreenRotation.Rotations)desktopScreen.devMode.dmDisplayOrientation, nativeScreenRotation);
            oldOrientation = ScreenRotation.Rotations.UNSET;

            if (nativeScreenRotation == ScreenRotation.Rotations.UNSET)
                SettingsManager.SetProperty("NativeDisplayOrientation", (int)screenOrientation.rotationNativeBase, true);
        }
        else
        {
            screenOrientation = new ScreenRotation((ScreenRotation.Rotations)desktopScreen.devMode.dmDisplayOrientation, screenOrientation.rotationNativeBase);
        }

        // raise event
        if (oldOrientation != screenOrientation.rotation)
            DisplayOrientationChanged?.Invoke(screenOrientation);
        */
    }

    public static ScreenRotation GetScreenOrientation()
    {
        return screenOrientation;
    }

    public static void Start()
    {
        // force trigger events
        SystemEvents_DisplaySettingsChanged(null, EventArgs.Empty);

        // get native resolution
        ScreenResolution nativeResolution = PrimaryDesktop.screenResolutions.First();

        // get integer scaling dividers
        int idx = 1;

        while (true)
        {
            int height = nativeResolution.Height / idx;
            ScreenResolution? dividedRes = PrimaryDesktop.screenResolutions.FirstOrDefault(r => r.Height == height);
            if (dividedRes is null)
                break;

            PrimaryDesktop.screenDividers.Add(new(idx, dividedRes));
            idx++;
        }

        IsInitialized = true;
        Initialized?.Invoke();

        LogManager.LogInformation("{0} has started", nameof(MultimediaManager));
    }

    public static void Stop()
    {
        if (!IsInitialized)
            return;

        ScreenBrightness.Unsubscribe();
        SoundControl.Unsubscribe();
        NightLight.Unsubscribe();

        IsInitialized = false;

        LogManager.LogInformation("{0} has stopped", "SystemManager");
    }

    public static bool SetResolution(int width, int height, int displayFrequency)
    {
        if (!IsInitialized)
            return false;

        bool ret = false;
        DisplayDevice dm = new DisplayDevice();
        dm.dmSize = (short)Marshal.SizeOf(typeof(DisplayDevice));
        dm.dmPelsWidth = width;
        dm.dmPelsHeight = height;
        dm.dmDisplayFrequency = displayFrequency;
        dm.dmFields = DisplayDevice.DM_PELSWIDTH | DisplayDevice.DM_PELSHEIGHT | DisplayDevice.DM_DISPLAYFREQUENCY;

        long RetVal = ChangeDisplaySettings(ref dm, CDS_TEST);
        if (RetVal == 0)
        {
            RetVal = ChangeDisplaySettings(ref dm, 0);
            ret = true;
        }

        return ret;
    }

    public static bool SetResolution(int width, int height, int displayFrequency, int bitsPerPel)
    {
        if (!IsInitialized)
            return false;

        bool ret = false;
        DisplayDevice dm = new DisplayDevice();
        dm.dmSize = (short)Marshal.SizeOf(typeof(DisplayDevice));
        dm.dmPelsWidth = width;
        dm.dmPelsHeight = height;
        dm.dmDisplayFrequency = displayFrequency;
        dm.dmBitsPerPel = bitsPerPel;
        dm.dmFields = DisplayDevice.DM_PELSWIDTH | DisplayDevice.DM_PELSHEIGHT | DisplayDevice.DM_DISPLAYFREQUENCY;

        long RetVal = ChangeDisplaySettings(ref dm, CDS_TEST);
        if (RetVal == 0)
        {
            _ = ChangeDisplaySettings(ref dm, 0);
            ret = true;
        }

        return ret;
    }

    public static DisplayDevice GetDisplay(string DeviceName)
    {
        DisplayDevice dm = new DisplayDevice();
        dm.dmSize = (short)Marshal.SizeOf(typeof(DisplayDevice));
        EnumDisplaySettings(DeviceName, -1, ref dm);
        return dm;
    }

    public static List<DisplayDevice> GetResolutions(string DeviceName)
    {
        List<DisplayDevice> allMode = new List<DisplayDevice>();
        DisplayDevice dm = new DisplayDevice();
        dm.dmSize = (short)Marshal.SizeOf(typeof(DisplayDevice));
        int index = 0;
        while (EnumDisplaySettings(DeviceName, index, ref dm))
        {
            allMode.Add(dm);
            index++;
        }

        return allMode;
    }

    public static void PlayWindowsMedia(string file)
    {
        bool Enabled = SettingsManager.Get<bool>("UISounds");
        if (!Enabled)
            return;

        string path = Path.Combine(@"c:\Windows\Media\", file);
        if (File.Exists(path))
            new SoundPlayer(path).Play();
    }

    public static bool HasVolumeSupport()
    {
        return VolumeSupport && MicrophoneSupport;
    }

    public static float AdjustVolume(int delta)
    {
        if (!VolumeSupport) return -1;

        var volume = SoundControl.AudioGet();
        volume = delta > 0
            ? (int)Math.Floor(volume / 5.0d) * 5
            : (int)Math.Ceiling(volume / 5.0d) * 5;
        volume = Math.Min(100, Math.Max(0, volume + delta));
        SoundControl.AudioSet(volume);
        return volume;
    }

    public static void SetVolume(double volume)
    {
        if (!VolumeSupport) return;

        SoundControl.AudioSet((int)volume);
    }

    public static bool HasBrightnessSupport()
    {
        return BrightnessSupport;
    }

    public static bool HasNightLightSupport() => NightLightSupport;

    public static int AdjustBrightness(int delta)
    {
        if (!BrightnessSupport) return -1;

        var brightness = ScreenBrightness.Get();
        brightness = delta > 0
            ? (int)Math.Floor(brightness / 5.0d) * 5
            : (int)Math.Ceiling(brightness / 5.0d) * 5;
        brightness = Math.Min(100, Math.Max(0, brightness + delta));
        ScreenBrightness.Set(brightness);
        return brightness;
    }

    public static void SetBrightness(double brightness)
    {
        if (!BrightnessSupport)
            return;

        ScreenBrightness.Set((int)brightness);
    }

    #region imports

    public enum DMDO
    {
        DEFAULT = 0,
        D90 = 1,
        D180 = 2,
        D270 = 3
    }

    public const int CDS_UPDATEREGISTRY = 0x01;
    public const int CDS_TEST = 0x02;
    public const int DISP_CHANGE_SUCCESSFUL = 0;
    public const int DISP_CHANGE_RESTART = 1;
    public const int DISP_CHANGE_FAILED = -1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public struct DisplayDevice
    {
        public const int DM_DISPLAYFREQUENCY = 0x400000;
        public const int DM_PELSWIDTH = 0x80000;
        public const int DM_PELSHEIGHT = 0x100000;
        private const int CCHDEVICENAME = 32;
        private const int CCHFORMNAME = 32;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCHDEVICENAME)]
        public string dmDeviceName;

        public short dmSpecVersion;
        public short dmDriverVersion;
        public short dmSize;
        public short dmDriverExtra;
        public int dmFields;

        public int dmPositionX;
        public int dmPositionY;
        public DMDO dmDisplayOrientation;
        public int dmDisplayFixedOutput;

        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCHFORMNAME)]
        public string dmFormName;

        public short dmLogPixels;
        public int dmBitsPerPel;
        public int dmPelsWidth;
        public int dmPelsHeight;
        public int dmDisplayFlags;
        public int dmDisplayFrequency;
        public int dmICMMethod;
        public int dmICMIntent;
        public int dmMediaType;
        public int dmDitherType;
        public int dmReserved1;
        public int dmReserved2;
        public int dmPanningWidth;
        public int dmPanningHeight;

        public override string ToString()
        {
            return $"{dmPelsWidth}x{dmPelsHeight}, {dmDisplayFrequency}, {dmBitsPerPel}";
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int ChangeDisplaySettings([In] ref DisplayDevice lpDevMode, int dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool EnumDisplaySettings(string lpszDeviceName, int iModeNum, ref DisplayDevice lpDevMode);

    #endregion

    #region events

    public static event DisplaySettingsChangedEventHandler DisplaySettingsChanged;
    public delegate void DisplaySettingsChangedEventHandler(DesktopScreen screen, ScreenResolution resolution);

    public static event PrimaryScreenChangedEventHandler PrimaryScreenChanged;
    public delegate void PrimaryScreenChangedEventHandler(DesktopScreen screen);

    public static event ScreenConnectedEventHandler ScreenConnected;
    public delegate void ScreenConnectedEventHandler(DesktopScreen screen);

    public static event ScreenDisconnectedEventHandler ScreenDisconnected;
    public delegate void ScreenDisconnectedEventHandler(DesktopScreen screen);

    public static event DisplayOrientationChangedEventHandler DisplayOrientationChanged;
    public delegate void DisplayOrientationChangedEventHandler(ScreenRotation rotation);

    public static event VolumeNotificationEventHandler VolumeNotification;
    public delegate void VolumeNotificationEventHandler(SoundDirections flow, float volume, bool isMute);

    public static event BrightnessNotificationEventHandler BrightnessNotification;
    public delegate void BrightnessNotificationEventHandler(int brightness);

    public static event NightLightNotificationEventHandler NightLightNotification;
    public delegate void NightLightNotificationEventHandler(bool enabled);

    public static event InitializedEventHandler Initialized;
    public delegate void InitializedEventHandler();

    #endregion
}