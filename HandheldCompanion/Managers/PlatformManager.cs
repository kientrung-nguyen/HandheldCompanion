using GameLib.Core;
using HandheldCompanion.Misc;
using HandheldCompanion.Platforms;
using HandheldCompanion.Platforms.Games;
using HandheldCompanion.Platforms.Misc;
using HandheldCompanion.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Timers;
using System.Windows;

namespace HandheldCompanion.Managers;

public class PlatformManager : IManager
{
    public static List<IPlatform> GamingPlatforms;
    public static List<IPlatform> MiscPlatforms;
    public static List<IPlatform> AllPlatforms;

    // gaming platforms
    public static Steam Steam;
    public static GOGGalaxy GOGGalaxy;
    public static UbisoftConnect UbisoftConnect;
    public static BattleNet BattleNet;
    public static Origin Origin;
    public static Epic Epic;
    public static RiotGames RiotGames;
    public static Rockstar Rockstar;

    // misc platforms
    public static RTSS RTSS;
    public static LibreHardware HardwareMonitor;

    private const int UpdateInterval = 1000;
    private static Timer UpdateTimer;

    private static PlatformNeeds CurrentNeeds = PlatformNeeds.None;
    private static PlatformNeeds PreviousNeeds = PlatformNeeds.None;

    public PlatformManager()
    {

    }

    public override void Start()
    {
        if (Status.HasFlag(ManagerStatus.Initializing) || Status.HasFlag(ManagerStatus.Initialized))
            return;

        base.PrepareStart();


        UpdateTimer = new() { Interval = UpdateInterval, AutoReset = false };
        UpdateTimer.Elapsed += (sender, e) => MonitorPlatforms();

        // initialize gaming platforms
        Steam = new Steam();
        GOGGalaxy = new GOGGalaxy();
        UbisoftConnect = new UbisoftConnect();
        BattleNet = new BattleNet();
        Origin = new Origin();
        Epic = new Epic();
        RiotGames = new RiotGames();
        Rockstar = new Rockstar();

        // initialize misc platforms
        RTSS = new RTSS();
        HardwareMonitor = new LibreHardware();

        // populate lists
        GamingPlatforms = new() { Steam, GOGGalaxy, UbisoftConnect, BattleNet, Origin, Epic, RiotGames, Rockstar };
        MiscPlatforms = new() { RTSS, HardwareMonitor };
        AllPlatforms = new(GamingPlatforms.Concat(MiscPlatforms));

        // start platforms
        foreach (IPlatform platform in AllPlatforms)
        {
            if (platform.IsInstalled)
                platform.Start();
        }

        base.Start();
    }

    public override void Stop()
    {
        if (Status.HasFlag(ManagerStatus.Halting) || Status.HasFlag(ManagerStatus.Halted))
            return;

        base.PrepareStop();

        // stop platforms
        foreach (IPlatform platform in AllPlatforms)
        {
            if (platform.IsInstalled)
            {
                bool kill = true;

                if (platform is RTSS)
                    kill = ManagerFactory.settingsManager.Get<bool>("PlatformRTSSEnabled");
                else if (platform is LibreHardware)
                    kill = false;

                platform.Stop(kill);
            }
        }
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
        foreach (IPlatform platform in GamingPlatforms)
            if (platform.IsRelated(proc))
                return platform.PlatformType;

        return PlatformType.Windows;
    }

    public static IEnumerable<IGame> GetGames()
    {
        return GamingPlatforms.SelectMany(platform => platform.GetGames());
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