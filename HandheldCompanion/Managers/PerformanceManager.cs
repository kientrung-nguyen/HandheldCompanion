using HandheldCompanion.Misc;
using HandheldCompanion.Processors;
using HandheldCompanion.Views;
using RTSSSharedMemoryNET;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Timers;
using Windows.ApplicationModel.Store;
using static HandheldCompanion.Platforms.HWiNFO;
using Timer = System.Timers.Timer;

namespace HandheldCompanion.Managers;

public static class PowerMode
{
    //public static Guid PowerSaver = new("a1841308-3541-4fab-bc81-f71556f20b4a");
    public static Guid Default = new("381b4222-f694-41f0-9685-ff5bb260df2e");
    public static Guid PowerSaverEPP = new("311e8682-fe8a-4c19-b51c-7f70f059cc15");
    public static Guid BalancedEPP = new("5e3046c2-eb29-4606-9f58-32b9f32ed977");
    public static Guid PerformanceEPP = new("6c6968d9-d5b8-4e07-91a3-095efdda17ed");

    /// <summary>
    ///     Better Battery mode. (Efficient)
    ///     Better Battery Overlay
    /// </summary>
    public static Guid BetterBattery = new("961cc777-2547-4f9d-8174-7d86181b8a7a");

    /// <summary>
    ///     Better Performance mode. (Balanced)
    ///     High Performance Overlay
    /// </summary>
    //public static Guid BetterPerformance = new("3af9B8d9-7c97-431d-ad78-34a8bfea439f");
    public static Guid BetterPerformance = new();

    /// <summary>
    ///     Best Performance mode. (Performance)
    ///     Max Perfromance Overlay
    /// </summary>
    public static Guid BestPerformance = new("ded574b5-45a0-4f42-8737-46345c09c238");
}

public class PerformanceManager : Manager
{
    private const short INTERVAL_DEFAULT = 3000; // default interval between value scans
    private const short INTERVAL_AUTO = 1010; // default interval between value scans
    private const short INTERVAL_DEGRADED = 5000; // degraded interval between value scans
    public static int MaxDegreeOfParallelism = 4;

    //public static readonly Guid[] PowerModes = [PowerMode.BetterBattery, PowerMode.BetterPerformance, PowerMode.BestPerformance, PowerMode.CustomPerformance];
    public static readonly Guid[] PowerModes = new Guid[] { PowerMode.Default, PowerMode.PowerSaverEPP, PowerMode.BalancedEPP, PowerMode.PerformanceEPP };
    private static readonly Dictionary<Guid, uint[]> ProcThrottleMin = new Dictionary<Guid, uint[]>
    {
        [PowerMode.Default] = new uint[] { 80, 5 },
        [PowerMode.BalancedEPP] = new uint[] { 80, 5 },
        [PowerMode.PerformanceEPP] = new uint[] { 100, 5 },
        [PowerMode.PowerSaverEPP] = new uint[] { 5, 5 }
    };
    private readonly Timer autoWatchdog;
    private readonly Timer cpuWatchdog;
    private readonly Timer gfxWatchdog;
    private readonly Timer powerWatchdog;

    private bool autoLock;
    private bool cpuLock;
    private bool gfxLock;
    private bool powerLock;

    // AutoTDP
    private double AutoTDP;
    private double AutoTDPCPUClock;
    private double AutoTDPGPUClock;
    private double AutoTDPPrev;
    private bool AutoTDPFirstRun = true;
    private int AutoTDPFPSSetpointMetCounter;
    private int AutoTDPFPSSmallDipCounter;
    private double AutoTDPMax;
    private double AutoCPUClockMin;
    private double AutoCPUClockMax;
    private double AutoGPUClockMin;
    private double AutoGPUClockMax;
    private double AutoTDPTargetFPS;
    private int AutoTDPProcessId;

    private double TDPMax;
    private double TDPMin;
    private bool cpuWatchdogPendingStop;
    private uint currentEPP = 0x00000032;
    private int currentCoreCount;
    private double currentGfxClock;

    // powercfg
    private bool? currentPerfBoostMode = null;
    private Guid currentPowerMode = new("FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF");
    private readonly double[] currentTDP = new double[5]; // used to store current TDP

    // GPU limits
    private double fallbackGfxClock;
    private readonly double[] fpsHistory = new double[6];
    private bool gfxWatchdogPendingStop;

    private Processor processor = new();
    private double processValueFPSPrevious;
    private double storedGfxClock;

    // TDP limits
    private readonly double[] storedTDP = new double[3]; // used to store TDP

    public PerformanceManager()
    {
        // initialize timer(s)
        powerWatchdog = new Timer { Interval = INTERVAL_DEFAULT, AutoReset = true, Enabled = false };
        powerWatchdog.Elapsed += powerWatchdog_Elapsed;

        cpuWatchdog = new Timer { Interval = INTERVAL_DEFAULT, AutoReset = true, Enabled = false };
        cpuWatchdog.Elapsed += cpuWatchdog_Elapsed;

        gfxWatchdog = new Timer { Interval = INTERVAL_DEFAULT, AutoReset = true, Enabled = false };
        gfxWatchdog.Elapsed += gfxWatchdog_Elapsed;

        autoWatchdog = new Timer { Interval = INTERVAL_AUTO, AutoReset = true, Enabled = false };
        autoWatchdog.Elapsed += AutoTDPWatchdog_Elapsed;

        ProfileManager.Applied += ProfileManager_Applied;
        ProfileManager.Discarded += ProfileManager_Discarded;

        PowerProfileManager.Applied += PowerProfileManager_Applied;
        PowerProfileManager.Discarded += PowerProfileManager_Discarded;

        PlatformManager.HWiNFO.PowerLimitChanged += HWiNFO_PowerLimitChanged;
        PlatformManager.HWiNFO.GPUFrequencyChanged += HWiNFO_GPUFrequencyChanged;

        PlatformManager.RTSS.Hooked += RTSS_Hooked;
        PlatformManager.RTSS.Unhooked += RTSS_Unhooked;

        // initialize settings
        SettingsManager.SettingValueChanged += SettingsManagerOnSettingValueChanged;

        currentCoreCount = Environment.ProcessorCount;
        MaxDegreeOfParallelism = Convert.ToInt32(Environment.ProcessorCount / 2);
    }

    private void SettingsManagerOnSettingValueChanged(string name, object value)
    {
        switch (name)
        {
            case "ConfigurableTDPOverrideDown":
                {
                    TDPMin = Convert.ToDouble(value);
                    AutoTDP = (TDPMax + TDPMin) / 2.0d;
                }
                break;
            case "ConfigurableTDPOverrideUp":
                {
                    TDPMax = Convert.ToDouble(value);
                    if (AutoTDPMax == 0d) AutoTDPMax = TDPMax;
                    AutoTDP = (TDPMax + TDPMin) / 2.0d;
                }
                break;
        }
    }

    private void ProfileManager_Applied(Profile profile, UpdateSource source)
    {
        // apply profile define RSR
        try
        {
            if (profile.RSREnabled)
            {
                ADLXBackend.SetRSR(true);
                ADLXBackend.SetRSRSharpness(profile.RSRSharpness);
            }
            else if (ADLXBackend.GetRSRState() == 1)
            {
                ADLXBackend.SetRSR(false);
                ADLXBackend.SetRSRSharpness(20);
            }
        }
        catch { }
    }

    private void ProfileManager_Discarded(Profile profile)
    {
        try
        {
            // restore default RSR
            if (profile.RSREnabled)
            {
                ADLXBackend.SetRSR(false);
                ADLXBackend.SetRSRSharpness(20);
            }
        }
        catch { }
    }

    private void PowerProfileManager_Applied(PowerProfile profile, UpdateSource source)
    {
        // apply profile define CPU Boost
        RequestPerfBoostMode(profile.CPUBoostEnabled);

        // apply profile Power Mode
        RequestPowerMode(profile.OSPowerMode);

        // apply profile defined TDP
        if (profile.TDPOverrideEnabled && profile.TDPOverrideValues is not null)
        {
            RequestTDP(profile.TDPOverrideValues);
            if (!profile.AutoTDPEnabled)
            {
                // Manual TDP is set, use it and set max limit
                StartTDPWatchdog();
                AutoTDPMax = SettingsManager.GetInt("ConfigurableTDPOverrideUp");
            }
            else
            {
                // Both manual TDP and AutoTDP are on,
                // use manual slider as the max limit for AutoTDP
                AutoTDPMax = profile.TDPOverrideValues[0];
                StopTDPWatchdog(true);
            }
        }
        else if (cpuWatchdog.Enabled)
        {
            StopTDPWatchdog(true);

            if (!profile.AutoTDPEnabled)
            {
                // Neither manual TDP nor AutoTDP is enabled, restore default TDP
                RestoreTDP(true);
            }
            else
            {
                // AutoTDP is enabled but manual override is not, use the settings max limit
                AutoTDPMax = SettingsManager.GetInt("ConfigurableTDPOverrideUp");
            }
        }

        // apply profile defined CPU
        if (profile.CPUOverrideEnabled)
        {
            RequestCPUClock(Convert.ToUInt32(profile.CPUOverrideValue));

            AutoCPUClockMin = MotherboardInfo.ProcessorMaxClockSpeed / 2;
            AutoCPUClockMax = Convert.ToUInt32(profile.CPUOverrideValue);
        }
        else
        {
            // restore default CPU clock
            RestoreCPUClock(true);
            AutoCPUClockMin = MotherboardInfo.ProcessorMaxClockSpeed / 2;
            AutoCPUClockMax = MotherboardInfo.ProcessorMaxTurboSpeed;
        }

        // apply profile defined GPU
        if (profile.GPUOverrideEnabled)
        {
            if (!profile.AutoTDPEnabled)
            {
                RequestGPUClock(profile.GPUOverrideValue);
                StartGPUWatchdog();
                AutoGPUClockMin = MainWindow.CurrentDevice.GfxClock[0];
                AutoGPUClockMax = profile.GPUOverrideValue;
            }
            else
            {
                StopGPUWatchdog(true);
                AutoGPUClockMin = MainWindow.CurrentDevice.GfxClock[0];
                AutoGPUClockMax = profile.GPUOverrideValue;
            }
        }
        else
        {
            if (gfxWatchdog.Enabled)
                StopGPUWatchdog(true);

            // restore default GPU clock
            if (!profile.AutoTDPEnabled)
                RestoreGPUClock(true);
            else
            {
                AutoGPUClockMin = MainWindow.CurrentDevice.GfxClock[0];
                AutoGPUClockMax = MainWindow.CurrentDevice.GfxClock[1];
            }
        }

        // apply profile defined AutoTDP
        if (profile.AutoTDPEnabled)
        {
            AutoTDPTargetFPS = profile.AutoTDPRequestedFPS;
            AutoTDP = AutoTDPMax;
            AutoTDPCPUClock = MotherboardInfo.ProcessorMaxClockSpeed;
            AutoTDPGPUClock = MainWindow.CurrentDevice.GfxClock[1] / 2;

            RequestProcThrottleMin(new uint[] { 5, 5 });
            StartAutoTDPWatchdog();
        }
        else if (autoWatchdog.Enabled)
        {
            StopAutoTDPWatchdog(true);

            // restore default TDP (if not manual TDP is enabled)
            if (!profile.TDPOverrideEnabled)
                RestoreTDP(true);

            if (!profile.GPUOverrideEnabled)
                RestoreGPUClock(true);

            if (!profile.CPUOverrideEnabled)
                RestoreCPUClock(true);
        }

        // apply profile defined CPU Core Count
        if (profile.CPUCoreEnabled)
        {
            RequestCPUCoreCount(profile.CPUCoreCount);
        }
        else if (currentCoreCount != MotherboardInfo.NumberOfCores)
        {
            // restore default CPU Core Count
            RequestCPUCoreCount(MotherboardInfo.NumberOfCores);
        }

        if (!profile.AutoTDPEnabled)
        {
            if (ProcThrottleMin.TryGetValue(profile.OSPowerMode, out var procThrottleMinValues))
                RequestProcThrottleMin(procThrottleMinValues);
        }

        // apply profile defined EPP
        if (profile.EPPOverrideEnabled)
        {
            RequestEPP(profile.EPPOverrideValue);
        }
        else if (currentEPP != 0x00000032)
        {
            if (profile.OSPowerMode == PowerMode.Default)
                // restore default EPP
                RequestEPP(0x00000032);
        }
        LogManager.LogInformation("Power Profile {0} applied.", profile.Name);
    }

    private void PowerProfileManager_Discarded(PowerProfile profile)
    {
        // restore default TDP
        if (profile.TDPOverrideEnabled)
        {
            StopTDPWatchdog(true);
            RestoreTDP(true);
        }

        // restore default TDP
        if (profile.AutoTDPEnabled)
        {
            StopAutoTDPWatchdog(true);
            StopTDPWatchdog(true);
            RestoreTDP(true);

            if (!profile.CPUOverrideEnabled)
                RestoreCPUClock(true);

            if (!profile.GPUOverrideEnabled)
                RestoreGPUClock(true);
        }

        // restore default CPU frequency
        if (profile.CPUOverrideEnabled)
        {
            RestoreCPUClock(true);
        }

        // restore default GPU frequency
        if (profile.GPUOverrideEnabled)
        {
            StopGPUWatchdog(true);
            RestoreGPUClock(true);
        }

        // (un)apply profile defined EPP
        //if (profile.EPPOverrideEnabled)
        //{
        //    // restore default EPP
        //    RequestEPP(0x00000032);
        //}

        // unapply profile defined CPU Core Count
        if (profile.CPUCoreEnabled)
            RequestCPUCoreCount(MotherboardInfo.NumberOfCores);


        // (un)apply profile define CPU Boost
        //if (profile.CPUBoostEnabled)
        //{
        //    RequestPerfBoostMode(false);
        //}

        // restore PowerMode.BetterPerformance 
        //RequestPowerMode(PowerMode.BetterPerformance);
        //RequestPowerMode(PowerMode.Default);
        LogManager.LogInformation("Power Profile {0} discarded", profile.Name);
    }

    private void RestoreTDP(bool immediate)
    {
        for (PowerType pType = PowerType.Slow; pType <= PowerType.Fast; pType++)
            RequestTDP(pType, MainWindow.CurrentDevice.cTDP[1], immediate);
    }

    private void RestoreCPUClock(bool immediate)
    {
        //var maxClock = MotherboardInfo.ProcessorMaxTurboSpeed;
        //var baseClock = MotherboardInfo.ProcessorMaxClockSpeed;
        //LogManager.LogDebug("Restore CPU Clock {0}, {1}", maxClock, baseClock);
        //RequestCPUClock((baseClock + maxClock) / 2);
        RequestCPUClock(0x00000000);
    }

    private void RestoreGPUClock(bool immediate)
    {
        RequestGPUClock(MainWindow.CurrentDevice.BaseGfxClock, immediate);
    }

    private void RTSS_Hooked(AppEntry appEntry)
    {
        LogManager.LogDebug("RTSS_Hooked with processId {0}", appEntry.ProcessId);
        AutoTDPProcessId = appEntry.ProcessId;
    }

    private void RTSS_Unhooked(int processId)
    {
        AutoTDPProcessId = 0;
    }

    private void AutoTDPWatchdog_Elapsed(object? sender, ElapsedEventArgs e)
    {
        // We don't have any hooked process
        if (AutoTDPProcessId == 0)
            return;

        try
        {
            if (!autoLock)
            {
                // set lock
                autoLock = true;

                // todo: Store fps for data gathering from multiple points (OSD, Performance)
                var osdAppEntry = PlatformManager.RTSS.GetAppEntry(AutoTDPProcessId);
                double processValueFPS = PlatformManager.RTSS.GetFramerate(AutoTDPProcessId);

                LogManager.LogDebug("RTSS - InstFrameTime {0}", osdAppEntry.InstantaneousFrameTime);
                LogManager.LogDebug("RTSS - FrameTimeCount {0}", osdAppEntry.StatFrameTimeCount);
                LogManager.LogDebug("RTSS - StatFrames {0}", osdAppEntry.StatFrames);

                LogManager.LogDebug("AutoTDP ---- Framerate: {0:0.0000}", processValueFPS);
                // Ensure realistic process values, prevent divide by 0
                processValueFPS = Math.Clamp(processValueFPS, 5, 500);

                double autoTDPDipper = AutoTDPDipper(processValueFPS, AutoTDPTargetFPS);
                LogManager.LogDebug("AutoTDP ---- AutoTDPDipper: {0:0.0000}", autoTDPDipper);

                // Determine error amount, include target, actual and dipper modifier
                double controllerError = AutoTDPTargetFPS - processValueFPS - autoTDPDipper;

                // Clamp error amount corrected within a single cycle
                // Adjust clamp if actual FPS is 2.5x requested FPS
                double clampLowerLimit = processValueFPS >= 2.5 * AutoTDPTargetFPS ? -100 : -5;
                controllerError = Math.Clamp(controllerError, clampLowerLimit, 15);
                LogManager.LogDebug("AutoTDP ---- AutoTDPError: {0:0.0000}", controllerError);

                double TDPAdjustment = controllerError * AutoTDP / processValueFPS;
                double cpuAdjustment = controllerError * AutoTDPCPUClock / processValueFPS;
                double gpuAdjustment = controllerError * AutoTDPGPUClock / processValueFPS;
                TDPAdjustment *= 0.9; // Always have a little undershoot
                cpuAdjustment *= .9;
                gpuAdjustment *= .9;

                // Determine final setpoint
                double TDPDamping = 0.0;
                if (!AutoTDPFirstRun)
                {
                    TDPDamping = AutoTDPDamper(processValueFPS);
                    AutoTDP += TDPAdjustment + TDPDamping;
                    AutoTDPCPUClock += cpuAdjustment + TDPDamping;
                    AutoTDPGPUClock += gpuAdjustment + TDPDamping;
                }
                else
                    AutoTDPFirstRun = false;


                //var autoClockValues = AutoTDPMaxClock(AutoTDP);

                //LogManager.LogDebug("TDPAuto;;;;;{0:0.0};{1:0.000};{2:0.0000};{3:0.0000};{4:0.0000}", AutoTDPTargetFPS, AutoTDP, TDPAdjustment, processValueFPS, TDPDamping);


                //LogManager.LogDebug("AutoTDP ---- CPU / GPU: {0:0.0000} {1:0.0000}", autoClockValues[0], autoClockValues[1]);
                // Only update if we have a different TDP value to set
                AutoTDPMaxClock(AutoTDP);
                LogManager.LogDebug("AutoTDP - {0:0.0000} {1:0.0000} {2:0.0000} {3:0.0000} {4:0.0000}",
                    AutoTDP, AutoTDPCPUClock, AutoTDPGPUClock,
                    TDPDamping, TDPAdjustment);
                //if (AutoTDP != AutoTDPPrev)
                //{
                //    //var autoTDPValues = new double[3] { AutoTDP, AutoTDP, AutoTDP };
                //    //RequestTDP(autoTDPValues, true);
                //    //RequestCPUClock(Convert.ToUInt32(autoClockValues[0]));
                //    //RequestGPUClock(autoClockValues[1], true);
                //    RequestCPUClock(Convert.ToUInt32(AutoTDPCPUClock));
                //    RequestGPUClock(Convert.ToUInt32(AutoTDPGPUClock), true);
                //}
                //else
                //{
                //    AutoTDPCPUClock = Math.Clamp(AutoTDPCPUClock, AutoCPUClockMin, AutoCPUClockMax);
                //    AutoTDPGPUClock = Math.Clamp(AutoTDPGPUClock, AutoGPUClockMin, AutoGPUClockMax);

                //    RequestCPUClock(Convert.ToUInt32(AutoTDPCPUClock));
                //    RequestGPUClock(Convert.ToUInt32(AutoTDPGPUClock), true);
                //}
                AutoTDPPrev = AutoTDP;
                AutoTDP = Math.Clamp(AutoTDP, TDPMin, AutoTDPMax);
            }
        }
        finally
        {
            // release lock
            autoLock = false;
        }
    }

    private double[] AutoTDPMaxClock(double autoTDP)
    {
        var cpuClockMax = AutoCPUClockMax;
        var gpuClockMax = AutoGPUClockMax;
        switch (autoTDP)
        {
            case < 6: cpuClockMax = 1500; gpuClockMax = 400; AutoGPUClockMin = 200; break;
            case >= 6 and < 7: cpuClockMax = 1600; gpuClockMax = 400; break;
            case >= 7 and < 8: cpuClockMax = 1700; gpuClockMax = 500; break;
            case >= 8 and < 9: cpuClockMax = 1800; gpuClockMax = 600; break;
            case >= 9 and < 10: cpuClockMax = 1900; gpuClockMax = 700; break;
            case >= 10 and < 11: cpuClockMax = 2000; gpuClockMax = 800; break;
            case >= 11 and < 12: cpuClockMax = 2100; gpuClockMax = 900; break;
            case >= 12 and < 13: cpuClockMax = 2200; gpuClockMax = 975; break;
            case >= 13 and < 14: cpuClockMax = 2300; gpuClockMax = 1025; break;
            case >= 14 and < 15: cpuClockMax = 2400; gpuClockMax = 1175; break;
            case >= 15 and < 16: cpuClockMax = 2500; gpuClockMax = 1225; break;
            case >= 16 and < 17: cpuClockMax = 2600; gpuClockMax = 1450; break;
            case >= 17 and < 18: cpuClockMax = 2700; gpuClockMax = 1500; break;
            case >= 18 and < 19: cpuClockMax = 2800; gpuClockMax = 1550; break;
            case >= 19 and < 20: cpuClockMax = 2900; gpuClockMax = 1650; break;
            case >= 20 and < 21: cpuClockMax = 3000; gpuClockMax = 1700; break;
            case >= 21 and < 22: cpuClockMax = 3100; gpuClockMax = 1800; break;
            case >= 22 and < 23: cpuClockMax = 3200; gpuClockMax = 1850; break;
            case >= 23 and < 24: cpuClockMax = 3300; gpuClockMax = 1900; break;
            case >= 24 and < 25: cpuClockMax = 3400; gpuClockMax = 1950; break;
            case >= 25 and < 26: cpuClockMax = 3500; gpuClockMax = 2000; break;
            case >= 26 and < 27: cpuClockMax = 3600; gpuClockMax = 2050; break;
            case >= 27 and < 28: cpuClockMax = 3700; gpuClockMax = 2100; break;
            case >= 28 and < 29: cpuClockMax = 3800; gpuClockMax = 2150; break;
            case >= 29 and < 30: cpuClockMax = 3900; gpuClockMax = 2200; break;
            case >= 30 and < 31: cpuClockMax = 4000; gpuClockMax = 2250; break;
            case >= 31 and < 32: cpuClockMax = 4000; gpuClockMax = 2375; break;
            case >= 32 and < 33: cpuClockMax = 4000; gpuClockMax = 2400; break;
            case >= 33 and < 34: cpuClockMax = 4000; gpuClockMax = 2425; break;
            case >= 34 and < 35: cpuClockMax = 4000; gpuClockMax = 2450; break;
            case >= 35 and < 36: cpuClockMax = 4000; gpuClockMax = 2475; break;
            case >= 36 and < 37: cpuClockMax = 4100; gpuClockMax = 2500; break;
            case >= 37 and < 38: cpuClockMax = 4200; gpuClockMax = 2525; break;
            case >= 38 and < 39: cpuClockMax = 4300; gpuClockMax = 2530; break;
            case >= 39 and < 40: cpuClockMax = 4400; gpuClockMax = 2575; break;
            case >= 40 and < 41: cpuClockMax = 4500; gpuClockMax = 2575; break;
            case >= 41 and < 42: cpuClockMax = 4500; gpuClockMax = 2600; break;
            case >= 42 and < 43: cpuClockMax = 4500; gpuClockMax = 2625; break;
            case >= 43 and < 44: cpuClockMax = 4500; gpuClockMax = 2650; break;
            case >= 44 and < 45: cpuClockMax = 4500; gpuClockMax = 2675; break;
        }

        AutoCPUClockMax = cpuClockMax;
        AutoGPUClockMax = gpuClockMax;

        AutoTDPCPUClock = Math.Clamp(AutoTDPCPUClock, AutoCPUClockMin, AutoCPUClockMax);
        AutoTDPGPUClock = Math.Clamp(AutoTDPGPUClock, AutoGPUClockMin, AutoGPUClockMax);

        if (autoTDP < 20)
            AutoTDPGPUClock = gpuClockMax / 2;

        RequestCPUClock(Convert.ToUInt32(AutoTDPCPUClock));
        RequestGPUClock(Convert.ToUInt32(AutoTDPGPUClock), true);

        return new double[] { cpuClockMax, gpuClockMax };
    }

    private double AutoTDPDipper(double fpsActual, double fpsSetpoint)
    {
        // Dipper
        // Add small positive "error" if actual and target FPS are similar for a duration
        double modifier = 0.0d;

        // Track previous FPS values for average calculation using a rolling array
        Array.Copy(fpsHistory, 0, fpsHistory, 1, fpsHistory.Length - 1);
        fpsHistory[0] = fpsActual; // Add current FPS at the start

        // Activate around target range of 1 FPS as games can fluctuate
        if (fpsSetpoint - 1 <= fpsActual && fpsActual <= fpsSetpoint + 1)
        {
            AutoTDPFPSSetpointMetCounter++;

            // First wait for three seconds of stable FPS arount target, then perform small dip
            // Reduction only happens if average FPS is on target or slightly below
            //
            if (AutoTDPFPSSetpointMetCounter >= 3 && AutoTDPFPSSetpointMetCounter < 6 &&
                fpsSetpoint - 0.5 <= fpsHistory.Take(3).Average() && fpsHistory.Take(3).Average() <= fpsSetpoint + 0.1)
            {
                AutoTDPFPSSmallDipCounter++;
                modifier = fpsSetpoint + 0.5 - fpsActual;
            }
            // After three small dips, perform larger dip 
            // Reduction only happens if average FPS is on target or slightly below
            else if (AutoTDPFPSSmallDipCounter >= 3 &&
                     fpsSetpoint - 0.5 <= fpsHistory.Average() && fpsHistory.Average() <= fpsSetpoint + 0.1)
            {
                modifier = fpsSetpoint + 1.5 - fpsActual;
                AutoTDPFPSSetpointMetCounter = 6;
            }
        }
        // Perform dips until FPS is outside of limits around target
        else
        {
            modifier = 0.0;
            AutoTDPFPSSetpointMetCounter = 0;
            AutoTDPFPSSmallDipCounter = 0;
        }

        return modifier;
    }

    private double AutoTDPDamper(double fpsActual)
    {
        // (PI)D derivative control component to dampen FPS fluctuations
        if (double.IsNaN(processValueFPSPrevious)) processValueFPSPrevious = fpsActual;
        double dFactor = -0.1d;

        // Calculation
        double deltaError = fpsActual - processValueFPSPrevious;
        double dTerm = deltaError / (INTERVAL_AUTO / 1000.0);
        double tdpDamping = AutoTDP / fpsActual * dFactor * dTerm;

        processValueFPSPrevious = fpsActual;

        return tdpDamping;
    }

    private void powerWatchdog_Elapsed(object? sender, ElapsedEventArgs e)
    {
        try
        {

            if (!powerLock)
            {
                // set lock
                powerLock = true;
                var idx = -1;
                if (PowerScheme.GetActiveScheme(out var activeScheme) && activeScheme != currentPowerMode)
                {
                    currentPowerMode = activeScheme;
                    idx = Array.IndexOf(PowerModes, activeScheme);
                    if (idx != -1)
                        PowerModeChanged?.Invoke(idx);
                }

                // read perfboostmode
                var result = PowerScheme.ReadPowerCfg(PowerSubGroup.SUB_PROCESSOR, PowerSetting.PERFBOOSTMODE);
                var perfboostmode = result[(int)PowerIndexType.AC] == (uint)PerfBoostMode.Aggressive &&
                                    result[(int)PowerIndexType.DC] == (uint)PerfBoostMode.Aggressive;

                if (perfboostmode != currentPerfBoostMode)
                {
                    currentPerfBoostMode = perfboostmode;
                    PerfBoostModeChanged?.Invoke(perfboostmode);
                }

                // Checking if current EPP value has changed to reflect that
                var EPP = PowerScheme.ReadPowerCfg(PowerSubGroup.SUB_PROCESSOR, PowerSetting.PERFEPP);
                var DCvalue = EPP[(int)PowerIndexType.DC];

                if (DCvalue != currentEPP)
                {
                    currentEPP = DCvalue;
                    EPPChanged?.Invoke(DCvalue);
                }
            }
        }
        finally
        {
            // release lock
            powerLock = false;
        }
    }

    private async void cpuWatchdog_Elapsed(object? sender, ElapsedEventArgs e)
    {
        if (processor is null || !processor.IsInitialized)
            return;

        if (!cpuLock)
        {
            // set lock
            cpuLock = true;

            var TDPdone = false;
            var MSRdone = false;

            // read current values and (re)apply requested TDP if needed
            foreach (var type in (PowerType[])Enum.GetValues(typeof(PowerType)))
            {
                var idx = (int)type;

                // skip msr
                if (idx >= storedTDP.Length)
                    break;

                var TDP = storedTDP[idx];

                if (processor is AMDProcessor)
                {
                    // AMD reduces TDP by 10% when OS power mode is set to Best power efficiency
                    if (currentPowerMode == PowerMode.BetterBattery)
                        TDP = (int)Math.Truncate(TDP * 0.9);
                }
                else if (processor is IntelProcessor)
                {
                    // Intel doesn't have stapm
                    if (type == PowerType.Stapm)
                        continue;
                }

                var ReadTDP = currentTDP[idx];

                if (ReadTDP != 0)
                    cpuWatchdog.Interval = INTERVAL_DEFAULT;
                else
                    cpuWatchdog.Interval = INTERVAL_DEGRADED;

                // only request an update if current limit is different than stored
                if (ReadTDP != TDP)
                    RequestTDP(type, TDP, true);

                await Task.Delay(12);
            }

            // are we done ?
            TDPdone = currentTDP[(int)PowerType.Slow] == storedTDP[(int)PowerType.Slow] &&
                currentTDP[(int)PowerType.Stapm] == storedTDP[(int)PowerType.Stapm] &&
                currentTDP[(int)PowerType.Fast] == storedTDP[(int)PowerType.Fast];

            // processor specific
            if (processor is IntelProcessor)
            {
                var TDPslow = (int)storedTDP[(int)PowerType.Slow];
                var TDPfast = (int)storedTDP[(int)PowerType.Fast];

                // only request an update if current limit is different than stored
                if (currentTDP[(int)PowerType.MsrSlow] != TDPslow ||
                    currentTDP[(int)PowerType.MsrFast] != TDPfast)
                    ((IntelProcessor)processor).SetMSRLimit(TDPslow, TDPfast);
                else
                    MSRdone = true;
            }

            // user requested to halt cpu watchdog
            if (cpuWatchdogPendingStop)
            {
                if (cpuWatchdog.Interval == INTERVAL_DEFAULT)
                {
                    if (TDPdone && MSRdone)
                        cpuWatchdog.Stop();
                }
                else if (cpuWatchdog.Interval == INTERVAL_DEGRADED)
                {
                    cpuWatchdog.Stop();
                }
            }

            // release lock
            cpuLock = false;
        }
    }

    private void gfxWatchdog_Elapsed(object? sender, ElapsedEventArgs e)
    {
        if (processor is null || !processor.IsInitialized)
            return;

        if (!gfxLock)
        {
            // set lock
            gfxLock = true;

            var GPUdone = false;

            if (currentGfxClock != 0)
                gfxWatchdog.Interval = INTERVAL_DEFAULT;
            else
                gfxWatchdog.Interval = INTERVAL_DEGRADED;

            // not ready yet
            if (storedGfxClock == 0)
            {
                // release lock
                gfxLock = false;
                return;
            }

            // only request an update if current gfx clock is different than stored
            if (currentGfxClock != storedGfxClock)
            {
                // disabling
                if (storedGfxClock == 12750)
                    GPUdone = true;
                else
                    RequestGPUClock(storedGfxClock, true);
            }
            else
            {
                GPUdone = true;
            }

            // user requested to halt gpu watchdog
            if (gfxWatchdogPendingStop)
            {
                if (gfxWatchdog.Interval == INTERVAL_DEFAULT)
                {
                    if (GPUdone)
                        gfxWatchdog.Stop();
                }
                else if (gfxWatchdog.Interval == INTERVAL_DEGRADED)
                {
                    gfxWatchdog.Stop();
                }
            }

            // release lock
            gfxLock = false;
        }
    }

    internal void StartGPUWatchdog()
    {
        gfxWatchdogPendingStop = false;
        gfxWatchdog.Interval = INTERVAL_DEFAULT;
        gfxWatchdog.Start();
    }

    internal void StopGPUWatchdog(bool immediate = false)
    {
        gfxWatchdogPendingStop = true;
        if (immediate)
            gfxWatchdog.Stop();
    }

    internal void StartTDPWatchdog()
    {
        cpuWatchdogPendingStop = false;
        cpuWatchdog.Interval = INTERVAL_DEFAULT;
        cpuWatchdog.Start();
    }

    internal void StopTDPWatchdog(bool immediate = false)
    {
        cpuWatchdogPendingStop = true;
        if (immediate)
            cpuWatchdog.Stop();
    }

    internal void StartAutoTDPWatchdog()
    {
        autoWatchdog.Start();
        LogManager.LogDebug("AutoTDPWatchdog Started. Hooked processId {0}", AutoTDPProcessId);
    }

    internal void StopAutoTDPWatchdog(bool immediate = false)
    {
        autoWatchdog.Stop();
        LogManager.LogDebug("AutoTDPWatchdog Stopped");
    }

    public void RequestMaxPerformance(bool immediate = false)
    {
        if (processor is null || !processor.IsInitialized)
            return;

        if (immediate)
        {
            processor.SetMaxPerformance();
        }
    }

    public void RequestTDP(PowerType type, double value, bool immediate = false)
    {
        if (processor is null || !processor.IsInitialized)
            return;

        // make sure we're not trying to run below or above specs
        value = Math.Min(TDPMax, Math.Max(TDPMin, value));

        // update value read by timer
        var idx = (int)type;
        storedTDP[idx] = value;

        // immediately apply
        if (immediate)
        {
            processor.SetTDPLimit((PowerType)idx, value, immediate);
            currentTDP[idx] = value;
        }
    }

    public async void RequestTDP(double[] values, bool immediate = false)
    {
        if (processor is null || !processor.IsInitialized)
            return;

        for (var idx = (int)PowerType.Slow; idx <= (int)PowerType.Fast; idx++)
        {
            // make sure we're not trying to run below or above specs
            values[idx] = Math.Min(TDPMax, Math.Max(TDPMin, values[idx]));

            // update value read by timer
            storedTDP[idx] = values[idx];

            // immediately apply
            if (immediate)
            {
                processor.SetTDPLimit((PowerType)idx, values[idx], immediate);
                currentTDP[idx] = values[idx];
                await Task.Delay(12);
            }
        }
    }

    public async void RequestGPUClock(double value, bool immediate = false)
    {
        if (processor is null || !processor.IsInitialized)
            return;

        // update value read by timer
        storedGfxClock = value;
        // immediately apply
        if (immediate)
        {
            if (currentGfxClock != storedGfxClock)
            {
                processor.SetGPUClock(value);
                currentGfxClock = value;
            }
        }
    }

    public void RequestProcThrottleMin(uint[] requestedValues)
    {
        // Is the ProcThrottleMin value already correct?
        var values = PowerScheme.ReadPowerCfg(PowerSubGroup.SUB_PROCESSOR, PowerSetting.PROCTHROTTLEMIN);
        if (values[(int)PowerIndexType.AC] == requestedValues[(int)PowerIndexType.AC] &&
            values[(int)PowerIndexType.DC] == requestedValues[(int)PowerIndexType.DC])
            return;

        LogManager.LogDebug("User requested ProcThrottleMin AC: {0}, DC: {1}", requestedValues[0], requestedValues[1]);

        // Set ProcThrottleMin
        PowerScheme.WritePowerCfg(PowerSubGroup.SUB_PROCESSOR, PowerSetting.PROCTHROTTLEMIN, requestedValues[0], requestedValues[1]);


        // Has the ProcThrottleMin value been applied?
        values = PowerScheme.ReadPowerCfg(PowerSubGroup.SUB_PROCESSOR, PowerSetting.PROCTHROTTLEMIN);
        if (values[(int)PowerIndexType.AC] != requestedValues[(int)PowerIndexType.AC] ||
            values[(int)PowerIndexType.DC] != requestedValues[(int)PowerIndexType.DC])
            LogManager.LogWarning("Failed to set requested ProcThrottleMin");
    }

    public void RequestPowerMode(Guid guid)
    {
        //if (!PowerScheme.SetActiveOverlayScheme(currentPowerMode))
        if (currentPowerMode != guid)
        {
            if (!PowerScheme.SetActiveScheme(guid))
            {
                LogManager.LogWarning("Failed to set requested power scheme: {0}", guid);
                RequestPowerMode(PowerMode.Default);
            }
            else
            {
                LogManager.LogDebug("User requested power scheme: {0}, current {1}", guid, currentPowerMode);
                currentPowerMode = guid;
            }
        }
    }

    public void RequestEPP(uint EPPOverrideValue)
    {
        currentEPP = EPPOverrideValue;

        var requestedEPP = new uint[2]
        {
            (uint)Math.Max(0, (int)EPPOverrideValue - 17),
            (uint)Math.Max(0, (int)EPPOverrideValue)
        };

        // Is the EPP value already correct?
        uint[] EPP = PowerScheme.ReadPowerCfg(PowerSubGroup.SUB_PROCESSOR, PowerSetting.PERFEPP);
        if (EPP[0] == requestedEPP[0] && EPP[1] == requestedEPP[1])
            return;

        LogManager.LogDebug("User requested EPP AC: {0}, DC: {1}", requestedEPP[0], requestedEPP[1]);

        // Set profile EPP
        PowerScheme.WritePowerCfg(PowerSubGroup.SUB_PROCESSOR, PowerSetting.PERFEPP, requestedEPP[0], requestedEPP[1]);
        PowerScheme.WritePowerCfg(PowerSubGroup.SUB_PROCESSOR, PowerSetting.PERFEPP1, requestedEPP[0], requestedEPP[1]);

        // Has the EPP value been applied?
        EPP = PowerScheme.ReadPowerCfg(PowerSubGroup.SUB_PROCESSOR, PowerSetting.PERFEPP);
        if (EPP[0] != requestedEPP[0] || EPP[1] != requestedEPP[1])
            LogManager.LogWarning("Failed to set requested EPP");
    }

    public void RequestCPUCoreCount(int CoreCount)
    {
        currentCoreCount = CoreCount;

        uint currentCoreCountPercent = (uint)((100.0d / MotherboardInfo.NumberOfCores) * CoreCount);

        // Is the CPMINCORES value already correct?
        uint[] CPMINCORES = PowerScheme.ReadPowerCfg(PowerSubGroup.SUB_PROCESSOR, PowerSetting.CPMINCORES);
        bool CPMINCORESReady = (CPMINCORES[0] == currentCoreCountPercent && CPMINCORES[1] == currentCoreCountPercent);

        // Is the CPMAXCORES value already correct?
        uint[] CPMAXCORES = PowerScheme.ReadPowerCfg(PowerSubGroup.SUB_PROCESSOR, PowerSetting.CPMAXCORES);
        bool CPMAXCORESReady = (CPMAXCORES[0] == currentCoreCountPercent && CPMAXCORES[1] == currentCoreCountPercent);

        if (CPMINCORESReady && CPMAXCORESReady)
            return;

        // Set profile CPMINCORES and CPMAXCORES
        PowerScheme.WritePowerCfg(PowerSubGroup.SUB_PROCESSOR, PowerSetting.CPMINCORES, currentCoreCountPercent, currentCoreCountPercent);
        PowerScheme.WritePowerCfg(PowerSubGroup.SUB_PROCESSOR, PowerSetting.CPMAXCORES, currentCoreCountPercent, currentCoreCountPercent);

        LogManager.LogDebug("User requested CoreCount: {0} ({1}%)", CoreCount, currentCoreCountPercent);

        // Has the CPMINCORES value been applied?
        CPMINCORES = PowerScheme.ReadPowerCfg(PowerSubGroup.SUB_PROCESSOR, PowerSetting.CPMINCORES);
        if (CPMINCORES[(int)PowerIndexType.AC] != currentCoreCountPercent || CPMINCORES[(int)PowerIndexType.DC] != currentCoreCountPercent)
            LogManager.LogWarning("Failed to set requested CPMINCORES");

        // Has the CPMAXCORES value been applied?
        CPMAXCORES = PowerScheme.ReadPowerCfg(PowerSubGroup.SUB_PROCESSOR, PowerSetting.CPMAXCORES);
        if (CPMAXCORES[(int)PowerIndexType.AC] != currentCoreCountPercent || CPMAXCORES[(int)PowerIndexType.DC] != currentCoreCountPercent)
            LogManager.LogWarning("Failed to set requested CPMAXCORES");
    }

    public void RequestPerfBoostMode(bool value)
    {
        // read perfboostmode
        if (currentPerfBoostMode == null)
        {
            var result = PowerScheme.ReadPowerCfg(PowerSubGroup.SUB_PROCESSOR, PowerSetting.PERFBOOSTMODE);
            var resultPerfboostmode = result[(int)PowerIndexType.AC] == (uint)PerfBoostMode.Aggressive &&
                                result[(int)PowerIndexType.DC] == (uint)PerfBoostMode.Aggressive;
            currentPerfBoostMode = resultPerfboostmode;
        }

        if (currentPerfBoostMode != value)
        {
            currentPerfBoostMode = value;
            var perfboostmode = value ? (uint)PerfBoostMode.Aggressive : (uint)PerfBoostMode.Enabled;
            PowerScheme.WritePowerCfg(PowerSubGroup.SUB_PROCESSOR, PowerSetting.PERFBOOSTMODE, perfboostmode, perfboostmode);
            LogManager.LogDebug("User requested perfboostmode: {0}", value);
        }
    }

    private void RequestCPUClock(uint cpuClock)
    {
        double maxClock = MotherboardInfo.ProcessorMaxTurboSpeed;

        // Is the PROCFREQMAX value already correct?
        var currentClock = PowerScheme.ReadPowerCfg(PowerSubGroup.SUB_PROCESSOR, PowerSetting.PROCFREQMAX);
        var isReady = currentClock[0] == cpuClock && currentClock[1] == cpuClock;

        if (isReady)
        {
            LogManager.LogDebug("User requested PROCFREQMAX: {0}/{1} but already ({2})", cpuClock, maxClock, currentClock[0] + ", " + currentClock[1]);
            return;
        }

        PowerScheme.WritePowerCfg(PowerSubGroup.SUB_PROCESSOR, PowerSetting.PROCFREQMAX, cpuClock, cpuClock);

        double cpuPercentage = cpuClock / maxClock * 100.0d;
        LogManager.LogDebug("User requested PROCFREQMAX: {0}/{1} ({2}%)", cpuClock, maxClock, cpuPercentage);

        // Has the value been applied?
        currentClock = PowerScheme.ReadPowerCfg(PowerSubGroup.SUB_PROCESSOR, PowerSetting.PROCFREQMAX);
        if (currentClock[0] != cpuClock || currentClock[1] != cpuClock)
            LogManager.LogWarning("Failed to set requested PROCFREQMAX");
    }

    public override void Start()
    {
        // initialize watchdog(s)
        powerWatchdog.Start();

        // initialize processor
        processor = Processor.GetCurrent();

        if (processor.IsInitialized)
        {
            processor.StatusChanged += Processor_StatusChanged;
            processor.Initialize();
        }

        // deprecated
        /*
        processor.ValueChanged += Processor_ValueChanged;
        processor.LimitChanged += Processor_LimitChanged;
        processor.MiscChanged += Processor_MiscChanged;
        */

        base.Start();
    }

    public override void Stop()
    {
        if (!IsInitialized)
            return;

        processor.Stop();

        powerWatchdog.Stop();
        cpuWatchdog.Stop();
        gfxWatchdog.Stop();
        autoWatchdog.Stop();
        base.Stop();
    }

    public Processor GetProcessor()
    {
        return processor;
    }

    #region imports

    /// <summary>
    ///     Retrieves the active overlay power scheme and returns a GUID that identifies the scheme.
    /// </summary>
    /// <param name="EffectiveOverlayPolicyGuid">A pointer to a GUID structure.</param>
    /// <returns>Returns zero if the call was successful, and a nonzero value if the call failed.</returns>
    [DllImport("powrprof.dll", EntryPoint = "PowerGetEffectiveOverlayScheme")]
    private static extern uint PowerGetEffectiveOverlayScheme(out Guid EffectiveOverlayPolicyGuid);

    /// <summary>
    ///     Sets the active power overlay power scheme.
    /// </summary>
    /// <param name="OverlaySchemeGuid">The identifier of the overlay power scheme.</param>
    /// <returns>Returns zero if the call was successful, and a nonzero value if the call failed.</returns>
    [DllImport("powrprof.dll", EntryPoint = "PowerSetActiveOverlayScheme")]
    private static extern uint PowerSetActiveOverlayScheme(Guid OverlaySchemeGuid);

    #endregion

    #region events

    public event LimitChangedHandler PowerLimitChanged;

    public delegate void LimitChangedHandler(PowerType type, int limit);

    public event ValueChangedHandler PowerValueChanged;

    public delegate void ValueChangedHandler(PowerType type, float value);

    public event StatusChangedHandler ProcessorStatusChanged;

    public delegate void StatusChangedHandler(bool CanChangeTDP, bool CanChangeGPU);

    public event PowerModeChangedEventHandler PowerModeChanged;

    public delegate void PowerModeChangedEventHandler(int idx);

    public event PerfBoostModeChangedEventHandler PerfBoostModeChanged;

    public delegate void PerfBoostModeChangedEventHandler(bool value);

    public event EPPChangedEventHandler EPPChanged;

    public delegate void EPPChangedEventHandler(uint EPP);

    #endregion

    #region events

    private void HWiNFO_PowerLimitChanged(PowerType type, int limit)
    {
        var idx = (int)type;
        currentTDP[idx] = limit;

        // workaround, HWiNFO doesn't have the ability to report MSR
        switch (type)
        {
            case PowerType.Slow:
                currentTDP[(int)PowerType.Stapm] = limit;
                currentTDP[(int)PowerType.MsrSlow] = limit;
                break;
            case PowerType.Fast:
                currentTDP[(int)PowerType.MsrFast] = limit;
                break;
        }

        // raise event
        PowerLimitChanged?.Invoke(type, limit);

        LogManager.LogTrace("PowerLimitChanged: {0}\t{1} W", type, limit);
    }

    private void HWiNFO_GPUFrequencyChanged(double value)
    {
        currentGfxClock = value;

        LogManager.LogTrace("GPUFrequencyChanged: {0} Mhz", value);
    }

    private void Processor_StatusChanged(bool CanChangeTDP, bool CanChangeGPU)
    {
        ProcessorStatusChanged?.Invoke(CanChangeTDP, CanChangeGPU);
    }

    [Obsolete("Method is deprecated.")]
    private void Processor_ValueChanged(PowerType type, float value)
    {
        PowerValueChanged?.Invoke(type, value);
    }

    [Obsolete("Method is deprecated.")]
    private void Processor_LimitChanged(PowerType type, int limit)
    {
        var idx = (int)type;
        currentTDP[idx] = limit;

        // raise event
        PowerLimitChanged?.Invoke(type, limit);
    }

    [Obsolete("Method is deprecated.")]
    private void Processor_MiscChanged(string misc, float value)
    {
        switch (misc)
        {
            case "gfx_clk":
                {
                    currentGfxClock = value;
                }
                break;
        }
    }

    #endregion
}
