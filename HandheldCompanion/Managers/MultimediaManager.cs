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
using WindowsDisplayAPI;
using WindowsDisplayAPI.DisplayConfig;

namespace HandheldCompanion.Managers;

public static class MultimediaManager
{
    private static ScreenRotation screenOrientation;

    private static bool VolumeSupport;
    private static bool MicrophoneSupport;
    private static readonly bool BrightnessSupport;

    private static readonly bool NightLightSupport;


    public static bool IsInitialized;

    static MultimediaManager()
    {
        VolumeSupport = SoundControl.AudioGet() != -1;
        MicrophoneSupport = SoundControl.MicrophoneGet() != -1;
        BrightnessSupport = ScreenBrightness.Get() != -1;
        NightLightSupport = NightLight.Get() != -1;
        SettingsManager.SettingValueChanged += SettingsManager_SettingValueChanged;
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

    public static ScreenRotation GetScreenOrientation()
    {
        return screenOrientation;
    }

    public static void Start()
    {
        // manage events
        ScreenControl.SubscribeToEvents(new Dictionary<string, Action<Display>>
        {
            ["PrimaryScreenChanged"] = (display) => PrimaryScreenChanged?.Invoke(display),
            ["DisplaySettingsChanged"] = (display) => DisplaySettingsChanged?.Invoke(display),
            ["ScreenConnected"] = (display) => ScreenConnected?.Invoke(display),
            ["ScreenDisconnected"] = (display) => ScreenDisconnected?.Invoke(display)
        });

        // setup the multimedia device and get current volume value
        SoundControl.SubscribeToEvents(VolumeNotificationEventArrived);
        NightLight.SubscribeToEvents(NightLightNotificationEventArrived);
        ScreenBrightness.SubscribeToEvents(BrightnessWatcherEventArrived);
        /*
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
        */
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
        ScreenControl.Unsubscribe();

        IsInitialized = false;

        LogManager.LogInformation("{0} has stopped", "SystemManager");
    }

    public static DisplayDevice GetDisplay(string DeviceName)
    {
        DisplayDevice dm = new DisplayDevice();
        dm.dmSize = (short)Marshal.SizeOf(typeof(DisplayDevice));
        EnumDisplaySettings(DeviceName, -1, ref dm);
        return dm;
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
            ? (int)Math.Floor(volume / delta * 1d) * delta
            : (int)Math.Ceiling(volume / delta * 1d) * delta;
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
            ? (int)Math.Floor(brightness / delta * 1d) * delta
            : (int)Math.Ceiling(brightness / delta * 1d) * delta;
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
    public delegate void DisplaySettingsChangedEventHandler(Display screen);

    public static event PrimaryScreenChangedEventHandler PrimaryScreenChanged;
    public delegate void PrimaryScreenChangedEventHandler(Display screen);

    public static event ScreenConnectedEventHandler ScreenConnected;
    public delegate void ScreenConnectedEventHandler(Display screen);

    public static event ScreenDisconnectedEventHandler ScreenDisconnected;
    public delegate void ScreenDisconnectedEventHandler(Display screen);

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