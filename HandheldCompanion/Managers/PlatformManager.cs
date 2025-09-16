﻿using HandheldCompanion.Misc;
using HandheldCompanion.Platforms;
using HandheldCompanion.Shared;
using HandheldCompanion.Utils;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Timers;
using System.Windows;
using LibreHwdMonitor = HandheldCompanion.Platforms.LibreHardwareMonitor;

namespace HandheldCompanion.Managers;

public static class PlatformManager
{
    // gaming platforms
    public static readonly Steam Steam = new();
    public static readonly GOGGalaxy GOGGalaxy = new();
    public static readonly UbisoftConnect UbisoftConnect = new();

    // misc platforms
    public static RTSS RTSS = new();
    public static LibreHwdMonitor LibreHardwareMonitor = new();

    private const int UpdateInterval = 1000;
    private static Timer UpdateTimer;

    public static bool IsInitialized;

    private static PlatformNeeds CurrentNeeds = PlatformNeeds.None;
    private static PlatformNeeds PreviousNeeds = PlatformNeeds.None;

    public static event InitializedEventHandler Initialized;
    public delegate void InitializedEventHandler();

    static PlatformManager()
    {
        UpdateTimer = new() { Interval = UpdateInterval, AutoReset = false };
        UpdateTimer.Elapsed += (sender, e) => MonitorPlatforms();
    }

    public static async Task Start()
    {
        if (IsInitialized)
            return;

        if (Steam.IsInstalled)
            Steam.Start();

        if (GOGGalaxy.IsInstalled)
        {
            // do something
        }

        if (UbisoftConnect.IsInstalled)
        {
            // do something
        }

        if (RTSS.IsInstalled)
        {
            UpdateCurrentNeedsOnScreenDisplay(EnumUtils<OverlayDisplayLevel>.Parse(SettingsManager.Get<int>("OnScreenDisplayLevel")));
        }

        if (LibreHardwareMonitor.IsInstalled)
        {
            LibreHardwareMonitor.Start();
        }

        // manage update timer events
        UpdateTimer.Start();

        // manage events
        SettingsManager.SettingValueChanged += SettingsManager_SettingValueChanged;
        ProfileManager.Applied += ProfileManager_Applied;
        PowerProfileManager.Applied += PowerProfileManager_Applied;

        // raise events
        if (ProfileManager.IsInitialized)
        {
            ProfileManager_Applied(ProfileManager.GetCurrent(), UpdateSource.Background);
        }

        // raise events
        if (PowerProfileManager.IsInitialized)
        {
            PowerProfileManager_Applied(PowerProfileManager.GetCurrent(), UpdateSource.Background);
        }

        // raise events
        if (SettingsManager.IsInitialized)
        {
            SettingsManager_SettingValueChanged("OnScreenDisplayLevel", SettingsManager.Get<int>("OnScreenDisplayLevel"), false);
        }

        IsInitialized = true;
        Initialized?.Invoke();

        LogManager.LogInformation("{0} has started", "PlatformManager");
    }

    public static void Stop()
    {
        if (Steam.IsInstalled)
            Steam.Stop();

        if (GOGGalaxy.IsInstalled)
            GOGGalaxy.Stop();

        if (UbisoftConnect.IsInstalled)
            UbisoftConnect.Stop();

        if (RTSS.IsInstalled)
        {
            bool killRTSS = SettingsManager.Get<bool>("PlatformRTSSEnabled");
            RTSS.Stop(killRTSS);
        }

        if (LibreHardwareMonitor.IsInstalled)
        {
            LibreHardwareMonitor.Stop();
        }

        // manage update timer events
        UpdateTimer.Stop();

        // manage events
        SettingsManager.SettingValueChanged -= SettingsManager_SettingValueChanged;
        ProfileManager.Applied -= ProfileManager_Applied;
        PowerProfileManager.Applied -= PowerProfileManager_Applied;

        IsInitialized = false;

        PreviousNeeds = PlatformNeeds.None;
        CurrentNeeds = PlatformNeeds.None;
        LogManager.LogInformation("{0} has stopped", "PlatformManager");
    }

    private static void PowerProfileManager_Applied(PowerProfile profile, UpdateSource source)
    {
        // AutoTDP
        if (profile.AutoTDPEnabled)
            CurrentNeeds |= PlatformNeeds.AutoTDP;
        else
            CurrentNeeds &= ~PlatformNeeds.AutoTDP;

        UpdateTimer.Stop();
        UpdateTimer.Start();
    }

    private static void ProfileManager_Applied(Profile profile, UpdateSource source)
    {
        // Framerate limiter
        if (profile.FramerateValue != 0)
            CurrentNeeds |= PlatformNeeds.FramerateLimiter;
        else
            CurrentNeeds &= ~PlatformNeeds.FramerateLimiter;

        UpdateTimer.Stop();
        UpdateTimer.Start();
    }

    private static void SettingsManager_SettingValueChanged(string name, object value, bool temporary)
    {
        // UI thread
        Application.Current.Dispatcher.Invoke(() =>
        {
            switch (name)
            {
                case "OnScreenDisplayLevel":
                    {
                        UpdateCurrentNeedsOnScreenDisplay(EnumUtils<OverlayDisplayLevel>.Parse(Convert.ToInt16(value)));
                        UpdateTimer.Stop();
                        UpdateTimer.Start();
                    }
                    break;
            }
        });
    }

    private static void UpdateCurrentNeedsOnScreenDisplay(OverlayDisplayLevel level)
    {
        switch (level)
        {
            case OverlayDisplayLevel.Disabled: // Disabled
                CurrentNeeds &= ~PlatformNeeds.OnScreenDisplay;
                CurrentNeeds &= ~PlatformNeeds.OnScreenDisplayComplex;
                break;
            default:
            case OverlayDisplayLevel.Minimal: // Minimal
                CurrentNeeds |= PlatformNeeds.OnScreenDisplay;
                CurrentNeeds &= ~PlatformNeeds.OnScreenDisplayComplex;
                break;
            case OverlayDisplayLevel.Extended: // Extended
            case OverlayDisplayLevel.Full: // Full
            case OverlayDisplayLevel.Custom:
            case OverlayDisplayLevel.External: // External
                CurrentNeeds |= PlatformNeeds.OnScreenDisplay;
                CurrentNeeds |= PlatformNeeds.OnScreenDisplayComplex;
                break;
        }
    }

    private static void MonitorPlatforms()
    {
        /*
         * Dependencies:
         * RTSS: AutoTDP, framerate limiter, OSD
         */

        // Check if the current needs are the same as the previous needs
        if (CurrentNeeds == PreviousNeeds) return;

        // Start or stop LHM and RTSS based on the current and previous needs
        if (CurrentNeeds.HasFlag(PlatformNeeds.OnScreenDisplay))
        {
            // If OSD is needed, start RTSS and start LHM only if OnScreenDisplayComplex is true
            if (!PreviousNeeds.HasFlag(PlatformNeeds.OnScreenDisplay))
            {
                // Only start RTSS if it was not running before and if it is installed
                if (RTSS.IsInstalled)
                {
                    // Start RTSS
                    RTSS.Start();
                }
            }
        }
        else if (CurrentNeeds.HasFlag(PlatformNeeds.AutoTDP) || CurrentNeeds.HasFlag(PlatformNeeds.FramerateLimiter))
        {
            // If AutoTDP or framerate limiter is needed, start only RTSS and stop LHM
            if (!PreviousNeeds.HasFlag(PlatformNeeds.AutoTDP) && !PreviousNeeds.HasFlag(PlatformNeeds.FramerateLimiter))
                // Only start RTSS if it was not running before and if it is installed
                if (RTSS.IsInstalled)
                    RTSS.Start();
        }
        else
        {
            // If none of the needs are present, stop both LHM and RTSS
            if (PreviousNeeds.HasFlag(PlatformNeeds.OnScreenDisplay) || PreviousNeeds.HasFlag(PlatformNeeds.AutoTDP) ||
                PreviousNeeds.HasFlag(PlatformNeeds.FramerateLimiter))
            {
                // Only stop LHM and RTSS if they were running before and if they are installed
                if (RTSS.IsInstalled)
                {
                    // Stop RTSS
                    RTSS.Stop();
                }
            }
        }

        // Store the current needs in the previous needs variable
        PreviousNeeds = CurrentNeeds;
    }

    public static PlatformType GetPlatform(Process proc)
    {
        if (!IsInitialized)
            return PlatformType.Windows;

        // is this process part of a specific platform
        if (Steam.IsRelated(proc))
            return Steam.PlatformType;
        if (GOGGalaxy.IsRelated(proc))
            return GOGGalaxy.PlatformType;
        if (UbisoftConnect.IsRelated(proc))
            return UbisoftConnect.PlatformType;
        return PlatformType.Windows;
    }

    [Flags]
    private enum PlatformNeeds
    {
        None = 0,
        AutoTDP = 1,
        FramerateLimiter = 2,
        OnScreenDisplay = 4,
        OnScreenDisplayComplex = 8
    }
}