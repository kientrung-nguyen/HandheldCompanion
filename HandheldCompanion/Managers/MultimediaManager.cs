﻿using HandheldCompanion.Managers.Desktop;
using HandheldCompanion.Shared;
using Microsoft.Win32;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.Media;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using WindowsDisplayAPI;
using WindowsDisplayAPI.DisplayConfig;

namespace HandheldCompanion.Managers;

public static class MultimediaManager
{
    public static Dictionary<string, DesktopScreen> AllScreens = [];
    public static DesktopScreen PrimaryDesktop;

    private static ScreenRotation screenOrientation;

    private static readonly MMDeviceEnumerator DevEnum;
    private static MMDevice multimediaDevice;
    private static readonly MMDeviceNotificationClient notificationClient;

    private static readonly ManagementEventWatcher BrightnessWatcher;
    private static readonly ManagementScope Scope;

    private static bool VolumeSupport;
    private static readonly bool BrightnessSupport;

    public static bool IsInitialized;

    static MultimediaManager()
    {
        // setup the multimedia device and get current volume value
        notificationClient = new MMDeviceNotificationClient();
        DevEnum = new MMDeviceEnumerator();
        DevEnum.RegisterEndpointNotificationCallback(notificationClient);
        SetDefaultAudioEndPoint();

        // get current brightness value
        Scope = new ManagementScope(@"\\.\root\wmi");
        Scope.Connect();

        // creating the watcher
        BrightnessWatcher = new ManagementEventWatcher(Scope, new EventQuery("Select * From WmiMonitorBrightnessEvent"));

        // check if we have control over brightness
        BrightnessSupport = GetBrightness() != -1;
    }

    public static async Task Start()
    {
        if (IsInitialized)
            return;

        // manage brightness watcher events
        BrightnessWatcher.EventArrived += onWMIEvent;
        BrightnessWatcher.Start();

        // manage events
        SystemEvents.DisplaySettingsChanged += SystemEvents_DisplaySettingsChanged;
        SettingsManager.SettingValueChanged += SettingsManager_SettingValueChanged;

        // raise events
        SystemEvents_DisplaySettingsChanged(null, null);

        IsInitialized = true;
        Initialized?.Invoke();

        LogManager.LogInformation("{0} has started", "SystemManager");
        return;
    }

    public static void Stop()
    {
        if (!IsInitialized)
            return;

        DevEnum.UnregisterEndpointNotificationCallback(notificationClient);

        // stop brightness watcher
        BrightnessWatcher.EventArrived -= onWMIEvent;
        BrightnessWatcher.Stop();

        // manage events
        SystemEvents.DisplaySettingsChanged -= SystemEvents_DisplaySettingsChanged;
        SettingsManager.SettingValueChanged -= SettingsManager_SettingValueChanged;

        IsInitialized = false;

        LogManager.LogInformation("{0} has stopped", "SystemManager");
    }

    private static void AudioEndpointVolume_OnVolumeNotification(AudioVolumeNotificationData data)
    {
        VolumeNotification?.Invoke(data.MasterVolume * 100.0f);
    }

    private static void SetDefaultAudioEndPoint()
    {
        try
        {
            if (multimediaDevice is not null && multimediaDevice.AudioEndpointVolume is not null)
            {
                VolumeSupport = false;
                multimediaDevice.AudioEndpointVolume.OnVolumeNotification -= AudioEndpointVolume_OnVolumeNotification;
            }

            multimediaDevice = DevEnum.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

            if (multimediaDevice is not null && multimediaDevice.AudioEndpointVolume is not null)
            {
                VolumeSupport = true;
                multimediaDevice.AudioEndpointVolume.OnVolumeNotification += AudioEndpointVolume_OnVolumeNotification;
            }

            // do this even when no device found, to set to 0
            VolumeNotification?.Invoke((float)GetVolume());
        }
        catch (Exception)
        {
            LogManager.LogError("No AudioEndpoint available");
        }
    }

    private static void SettingsManager_SettingValueChanged(string name, object value, bool temporary)
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

    private static void onWMIEvent(object sender, EventArrivedEventArgs e)
    {
        int brightness = Convert.ToInt32(e.NewEvent.Properties["Brightness"].Value);
        BrightnessNotification?.Invoke(brightness);
    }

    public static string GetDisplayName(string input)
    {
        int index = input.LastIndexOf('\\');
        if (index >= 0 && index < input.Length - 1)
        {
            return input.Substring(index + 1);
        }
        return string.Empty; // Or handle this case as needed
    }

    public static string GetDisplayFriendlyName(string DeviceName)
    {
        string friendlyName = GetDisplayName(DeviceName);

        Display? PrimaryDisplay = Display.GetDisplays().Where(display => display.DisplayName.Equals(DeviceName)).FirstOrDefault();
        if (PrimaryDisplay is not null && !string.IsNullOrEmpty(PrimaryDisplay.DeviceName))
            friendlyName = PrimaryDisplay.DeviceName;

        return friendlyName;
    }

    public static string GetAdapterFriendlyName(string DeviceName)
    {
        string friendlyName = string.Empty;

        Display? PrimaryDisplay = Display.GetDisplays().Where(display => display.DisplayName.Equals(DeviceName)).FirstOrDefault();
        if (PrimaryDisplay is not null)
        {
            if (!string.IsNullOrEmpty(PrimaryDisplay.DeviceName))
                friendlyName = PrimaryDisplay.DeviceName;

            PathDisplayTarget? PrimaryTarget = GetDisplayTarget(PrimaryDisplay.DevicePath);
            if (PrimaryTarget is not null && !string.IsNullOrEmpty(PrimaryTarget.FriendlyName))
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
        PathDisplayTarget PrimaryTarget = PathDisplayTarget.GetDisplayTargets().Where(target => target.DevicePath.Equals(DevicePath)).FirstOrDefault();
        return PrimaryTarget;
    }

    private static void SystemEvents_DisplaySettingsChanged(object? sender, EventArgs e)
    {
        // temporary array to store all current screens
        Dictionary<string, DesktopScreen> desktopScreens = [];

        foreach (Screen screen in Screen.AllScreens)
        {
            if (string.IsNullOrEmpty(screen.DeviceName))
                continue;

            DesktopScreen desktopScreen = new(screen);

            // pull resolutions details
            List<DisplayDevice> resolutions = GetResolutions(desktopScreen.screen.DeviceName);
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
            desktopScreen.screenResolutions.Sort();

            // get native resolution
            ScreenResolution nativeResolution = desktopScreen.screenResolutions.First();

            // get integer scaling dividers
            int idx = 1;

            while (true)
            {
                int height = nativeResolution.Height / idx;
                ScreenResolution? dividedRes = desktopScreen.screenResolutions.FirstOrDefault(r => r.Height == height);
                if (dividedRes is null)
                    break;

                desktopScreen.screenDividers.Add(new(idx, dividedRes));
                idx++;
            }

            // add to temporary array
            desktopScreens.Add(desktopScreen.DevicePath, desktopScreen);
        }

        // get refreshed primary screen (can't be null)
        DesktopScreen newPrimary = desktopScreens.Values.Where(a => a.IsPrimary).FirstOrDefault();

        // looks like we have a new primary screen
        if (PrimaryDesktop is null || !PrimaryDesktop.DevicePath.Equals(newPrimary.DevicePath))
        {
            // raise event (New primary display)
            PrimaryScreenChanged?.Invoke(newPrimary);
        }

        // set or update current primary
        PrimaryDesktop = newPrimary;

        // raise event (New screen detected)
        foreach (DesktopScreen desktop in desktopScreens.Values.Where(a => !AllScreens.ContainsKey(a.DevicePath)))
            ScreenConnected?.Invoke(desktop);

        // raise event (New screen detected)
        foreach (DesktopScreen desktop in AllScreens.Values.Where(a => !desktopScreens.ContainsKey(a.DevicePath)))
            ScreenDisconnected?.Invoke(desktop);

        // clear array and transfer screens
        AllScreens.Clear();
        foreach (DesktopScreen desktop in desktopScreens.Values)
            AllScreens.Add(desktop.DevicePath, desktop);

        // raise event (Display settings were updated)
        ScreenResolution screenResolution = PrimaryDesktop.GetResolution();
        if (screenResolution is not null)
            DisplaySettingsChanged?.Invoke(PrimaryDesktop, screenResolution);

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

    public static bool SetResolution(int width, int height, int displayFrequency)
    {
        if (!IsInitialized)
            return false;

        bool ret = false;
        DisplayDevice dm = new DisplayDevice
        {
            dmSize = (short)Marshal.SizeOf(typeof(DisplayDevice)),
            dmPelsWidth = width,
            dmPelsHeight = height,
            dmDisplayFrequency = displayFrequency,
            dmFields = DisplayDevice.DM_PELSWIDTH | DisplayDevice.DM_PELSHEIGHT | DisplayDevice.DM_DISPLAYFREQUENCY
        };

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
        DisplayDevice dm = new DisplayDevice
        {
            dmSize = (short)Marshal.SizeOf(typeof(DisplayDevice)),
            dmPelsWidth = width,
            dmPelsHeight = height,
            dmDisplayFrequency = displayFrequency,
            dmBitsPerPel = bitsPerPel,
            dmFields = DisplayDevice.DM_PELSWIDTH | DisplayDevice.DM_PELSHEIGHT | DisplayDevice.DM_DISPLAYFREQUENCY
        };

        long RetVal = ChangeDisplaySettings(ref dm, CDS_TEST);
        if (RetVal == 0)
        {
            RetVal = ChangeDisplaySettings(ref dm, 0);
            ret = true;
        }

        return ret;
    }

    public static DisplayDevice GetDisplay(string DeviceName)
    {
        DisplayDevice dm = new DisplayDevice
        {
            dmSize = (short)Marshal.SizeOf(typeof(DisplayDevice))
        };
        EnumDisplaySettings(DeviceName, -1, ref dm);
        return dm;
    }

    public static List<DisplayDevice> GetResolutions(string DeviceName)
    {
        List<DisplayDevice> allMode = [];
        DisplayDevice dm = new DisplayDevice
        {
            dmSize = (short)Marshal.SizeOf(typeof(DisplayDevice))
        };
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
        string path = Path.Combine(@"c:\Windows\Media\", file);
        if (File.Exists(path))
            new SoundPlayer(path).Play();
    }

    public static bool HasVolumeSupport()
    {
        return VolumeSupport;
    }

    public static void SetVolume(double volume)
    {
        if (!VolumeSupport)
            return;

        multimediaDevice.AudioEndpointVolume.MasterVolumeLevelScalar = (float)(volume / 100.0d);
    }

    public static double GetVolume()
    {
        if (!VolumeSupport)
            return 0.0d;

        return multimediaDevice.AudioEndpointVolume.MasterVolumeLevelScalar * 100.0d;
    }

    public static bool HasBrightnessSupport()
    {
        return BrightnessSupport;
    }

    public static void SetBrightness(double brightness)
    {
        if (!BrightnessSupport)
            return;

        try
        {
            using (ManagementClass mclass = new ManagementClass("WmiMonitorBrightnessMethods"))
            {
                mclass.Scope = new ManagementScope(@"\\.\root\wmi");
                using (ManagementObjectCollection instances = mclass.GetInstances())
                {
                    foreach (ManagementObject instance in instances)
                    {
                        object[] args = { 1, brightness };
                        instance.InvokeMethod("WmiSetBrightness", args);
                    }
                }
            }
        }
        catch
        {
        }
    }

    public static void IncreaseBrightness()
    {
        int stepRoundDn = (int)Math.Floor(GetBrightness() / 2.0d);
        int brightness = stepRoundDn * 2 + 2;
        SetBrightness(brightness);
    }

    public static void DecreaseBrightness()
    {
        int stepRoundUp = (int)Math.Ceiling(GetBrightness() / 2.0d);
        int brightness = stepRoundUp * 2 - 2;
        SetBrightness(brightness);
    }

    public static void IncreaseVolume()
    {
        int stepRoundDn = (int)Math.Ceiling(Math.Round(GetVolume() / 2.0d));
        int volume = stepRoundDn * 2 + 2;
        SetVolume(volume);
    }

    public static void DecreaseVolume()
    {
        int stepRoundUp = (int)Math.Ceiling(Math.Round(GetVolume() / 2.0d));
        int volume = stepRoundUp * 2 - 2;
        SetVolume(volume);
    }

    public static void Mute()
    {
        if (!VolumeSupport)
            return;

        multimediaDevice.AudioEndpointVolume.Mute = true;
    }

    public static void Unmute()
    {
        if (!VolumeSupport)
            return;

        multimediaDevice.AudioEndpointVolume.Mute = false;
    }

    public static void ToggleMute()
    {
        if (!VolumeSupport)
            return;

        multimediaDevice.AudioEndpointVolume.Mute = !multimediaDevice.AudioEndpointVolume.Mute;
    }

    public static bool IsMuted()
    {
        if (!VolumeSupport)
            return true;

        return multimediaDevice.AudioEndpointVolume.Mute;
    }

    public static short GetBrightness()
    {
        try
        {
            using (ManagementClass mclass = new ManagementClass("WmiMonitorBrightness"))
            {
                mclass.Scope = new ManagementScope(@"\\.\root\wmi");
                using (ManagementObjectCollection instances = mclass.GetInstances())
                {
                    foreach (ManagementObject instance in instances)
                        return (byte)instance.GetPropertyValue("CurrentBrightness");
                }
            }

            return 0;
        }
        catch
        {
        }

        return -1;
    }

    private class MMDeviceNotificationClient : IMMNotificationClient
    {
        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
        {
            SetDefaultAudioEndPoint();
        }

        public void OnDeviceAdded(string deviceId)
        {
        }

        public void OnDeviceRemoved(string deviceId)
        {
        }

        public void OnDeviceStateChanged(string deviceId, DeviceState newState)
        {
        }

        public void OnPropertyValueChanged(string deviceId, PropertyKey key)
        {
        }
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
    public delegate void VolumeNotificationEventHandler(float volume);

    public static event BrightnessNotificationEventHandler BrightnessNotification;
    public delegate void BrightnessNotificationEventHandler(int brightness);

    public static event InitializedEventHandler Initialized;
    public delegate void InitializedEventHandler();

    #endregion
}