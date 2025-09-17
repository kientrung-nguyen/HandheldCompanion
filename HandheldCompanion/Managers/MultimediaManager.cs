using HandheldCompanion.Managers.Desktop;
using HandheldCompanion.Misc;
using HandheldCompanion.Shared;
using HandheldCompanion.Utils;
using Microsoft.Win32;
using System;
using System.IO;
using System.Linq;
using System.Management;
using System.Media;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using WindowsDisplayAPI;
using WindowsDisplayAPI.DisplayConfig;

namespace HandheldCompanion.Managers;

public class MultimediaManager : IManager
{

    private static ScreenRotation screenOrientation;

    private static readonly bool VolumeSupport;
    private static readonly bool MicrophoneSupport;
    private static readonly bool BrightnessSupport;

    private static readonly bool NightLightSupport;

    public static bool IsInitialized;

    static MultimediaManager()
    {
        VolumeSupport = SoundControl.AudioGet() != -1;
        MicrophoneSupport = SoundControl.MicrophoneGet() != -1;
        BrightnessSupport = ScreenBrightness.Get() != -1;
        NightLightSupport = NightLight.Get() != -1;
    }

    public override void Start()
    {
        if (Status.HasFlag(ManagerStatus.Initializing) || Status.HasFlag(ManagerStatus.Initialized))
            return;

        base.PrepareStart();

        // manage events
        SystemEvents.DisplaySettingsChanged += SystemEvents_DisplaySettingsChanged;
        ScreenControl.SubscribeToEvents();

        SystemEvents_DisplaySettingsChanged(null, EventArgs.Empty);

        // setup the multimedia device and get current volume value
        SoundControl.SubscribeToEvents(VolumeNotificationEventArrived);
        NightLight.SubscribeToEvents(NightLightNotificationEventArrived);
        ScreenBrightness.SubscribeToEvents(BrightnessWatcherEventArrived);


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

        LogManager.LogInformation("{0} has started", nameof(MultimediaManager));
        return;
    }


    private void SettingsManager_Initialized()
    {
        QuerySettings();
    }

    private void QuerySettings()
    {
        // manage events
        ManagerFactory.settingsManager.SettingValueChanged += SettingsManager_SettingValueChanged;

        // raise events
        // do something
    }

    public override void Stop()
    {
        if (!IsInitialized)
            return;

        ScreenBrightness.Unsubscribe();
        SoundControl.Unsubscribe();
        NightLight.Unsubscribe();
        ScreenControl.Unsubscribe();
        ManagerFactory.settingsManager.SettingValueChanged -= SettingsManager_SettingValueChanged;
        ManagerFactory.settingsManager.Initialized -= SettingsManager_Initialized;
        SystemEvents.DisplaySettingsChanged -= SystemEvents_DisplaySettingsChanged;

        IsInitialized = false;

        LogManager.LogInformation("{0} has stopped", "SystemManager");
    }


    private void SystemEvents_DisplaySettingsChanged(object? sender, EventArgs e)
    {
        var _allDisplays = Display.GetDisplays();
        var _primaryDisplay = _allDisplays.FirstOrDefault(v => v.DisplayScreen.IsPrimary);
        if (_primaryDisplay == null)
            return;


        LogManager.LogError($"Detect primary display {_primaryDisplay.ToPathDisplayTarget().FriendlyName}");
        if (ScreenControl.PrimaryDisplay is null || !ScreenControl.PrimaryDisplay.ToPathDisplayTarget().FriendlyName.Equals(_primaryDisplay.ToPathDisplayTarget().FriendlyName))
            PrimaryScreenChanged?.Invoke(_primaryDisplay);

        ScreenControl.PrimaryDisplay = _primaryDisplay;

        foreach (var _display in _allDisplays.Where(_v => !ScreenControl.AllDisplays.Any(v => v.DevicePath == _v.DevicePath)))
            ScreenConnected?.Invoke(_display);

        foreach (var _display in ScreenControl.AllDisplays.Where(v => !_allDisplays.Any(_v => _v.DevicePath == v.DevicePath)))
            ScreenDisconnected?.Invoke(_display);

        ScreenControl.AllDisplays = [];
        ScreenControl.AllDisplays = _allDisplays;

        if (ScreenControl.PrimaryDisplay is not null)
            DisplaySettingsChanged?.Invoke(_primaryDisplay);

    }

    private void SettingsManager_SettingValueChanged(string name, object value, bool temporary)
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

    private void NightLightNotificationEventArrived(object? sender, RegistryChangedEventArgs e)
    {
        NightLightNotification?.Invoke(NightLight.Get() == 1);
    }

    private void VolumeNotificationEventArrived(SoundDirections flow, float volume, bool muted)
    {
        VolumeNotification?.Invoke(flow, volume, muted);
    }

    private void BrightnessWatcherEventArrived(object sender, EventArrivedEventArgs e)
    {
        int brightness = Convert.ToInt32(e.NewEvent.Properties["Brightness"].Value);
        BrightnessNotification?.Invoke(brightness);
    }

    public string GetDisplayFriendlyName(string DeviceName)
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

    public static DisplayDevice GetDisplay(string DeviceName)
    {
        DisplayDevice dm = new DisplayDevice();
        dm.dmSize = (short)Marshal.SizeOf(typeof(DisplayDevice));
        EnumDisplaySettings(DeviceName, -1, ref dm);
        return dm;
    }

    public static void PlayWindowsMedia(string file)
    {
        bool Enabled = ManagerFactory.settingsManager.Get<bool>("UISounds");
        if (!Enabled)
            return;

        string path = Path.Combine(@"c:\Windows\Media\", file);
        if (File.Exists(path))
            new SoundPlayer(path).Play();
    }

    public bool HasVolumeSupport()
    {
        return VolumeSupport && MicrophoneSupport;
    }

    public float AdjustVolume(int delta)
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

    public void SetVolume(double volume)
    {
        if (!VolumeSupport) return;

        SoundControl.AudioSet((int)volume);
    }

    public bool HasBrightnessSupport()
    {
        return BrightnessSupport;
    }

    public bool HasNightLightSupport() => NightLightSupport;

    public int AdjustBrightness(int delta)
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

    public event DisplaySettingsChangedEventHandler DisplaySettingsChanged;
    public delegate void DisplaySettingsChangedEventHandler(Display screen);

    public event PrimaryScreenChangedEventHandler PrimaryScreenChanged;
    public delegate void PrimaryScreenChangedEventHandler(Display screen);

    public event ScreenConnectedEventHandler ScreenConnected;
    public delegate void ScreenConnectedEventHandler(Display screen);

    public event ScreenDisconnectedEventHandler ScreenDisconnected;
    public delegate void ScreenDisconnectedEventHandler(Display screen);

    public event DisplayOrientationChangedEventHandler DisplayOrientationChanged;
    public delegate void DisplayOrientationChangedEventHandler(ScreenRotation rotation);

    public event VolumeNotificationEventHandler VolumeNotification;
    public delegate void VolumeNotificationEventHandler(SoundDirections flow, float volume, bool isMute);

    public event BrightnessNotificationEventHandler BrightnessNotification;
    public delegate void BrightnessNotificationEventHandler(int brightness);

    public event NightLightNotificationEventHandler NightLightNotification;
    public delegate void NightLightNotificationEventHandler(bool enabled);

    #endregion
}