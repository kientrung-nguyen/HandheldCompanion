using HandheldCompanion.Devices;
using HandheldCompanion.GraphicsProcessingUnit;
using HandheldCompanion.Misc;
using HandheldCompanion.Processors;
using HandheldCompanion.Properties;
using HandheldCompanion.Utils;
using PowerManagerAPI;
using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Timers;
using PowerSchemeAPI = PowerManagerAPI.PowerManager;
using Timer = System.Timers.Timer;

namespace HandheldCompanion.Managers;

public static class OSPowerMode
{
    /// <summary>
    ///     Better Battery mode.
    /// </summary>
    public static Guid BetterBattery = new("961cc777-2547-4f9d-8174-7d86181b8a7a");

    /// <summary>
    ///     Better Performance mode.
    /// </summary>
    // public static Guid BetterPerformance = new Guid("3af9B8d9-7c97-431d-ad78-34a8bfea439f");
    public static Guid BetterPerformance = new();

    /// <summary>
    ///     Best Performance mode.
    /// </summary>
    public static Guid BestPerformance = new("ded574b5-45a0-4f42-8737-46345c09c238");
}

public enum CPUBoostLevel
{
    Disabled = 0,
    Enabled = 1,
    Agressive = 2,
    EfficientEnabled = 3,
    EfficientAgressive = 4,
}

public static class PerformanceManager
{
    private const short INTERVAL_DEFAULT = 3000; // default interval between value scans
    private const short INTERVAL_AUTO = 1010; // default interval between value scans
    private const short INTERVAL_DEGRADED = 5000; // degraded interval between value scans

    public static readonly Guid[] PowerModes = [OSPowerMode.BetterBattery, OSPowerMode.BetterPerformance, OSPowerMode.BestPerformance];

    private static readonly Timer autoWatchdog;
    private static readonly Timer cpuWatchdog;
    private static readonly Timer gfxWatchdog;
    private static readonly Timer powerWatchdog;

    private static CrossThreadLock autoLock = new();
    private static CrossThreadLock cpuLock = new();
    private static CrossThreadLock gfxLock = new();
    private static CrossThreadLock powerLock = new();

    // AutoTDP
    private static double AutoTDP;
    private static double AutoTDPPrev;
    private static double AutoCPUClock;
    private static double AutoGPUClock;
    private static bool AutoTDPFirstRun = true;
    private static int AutoTDPFPSSetpointMetCounter;
    private static int AutoTDPFPSSmallDipCounter;
    private static double AutoTDPMax;
    private static double AutoCPUClockMin;
    private static double AutoCPUClockMax;
    private static double AutoGPUClockMin;
    private static double AutoGPUClockMax;
    private static double TDPMax;
    private static double TDPMin;
    private static double AutoTDPTargetFPS;
    private static double AutoTargetCPU;
    private static double AutoTargetGPU;
    private static bool cpuWatchdogPendingStop;

    private static uint currentEPP = 0x00000032;
    private static int currentCoreCount;
    private static uint currentGfxClock;

    // powercfg
    private static uint currentPerfBoostMode;
    private static Guid currentPowerMode = new("FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF");
    private static double[] currentTDP = new double[5]; // used to store current TDP

    public static float CurrentTDP => (float)currentTDP[(int)PowerType.Slow];

    // GPU limits
    private static double fallbackGfxClock;
    private static readonly double[] fpsHistory = new double[6];
    private static readonly double[] tdpHistory = new double[6];
    private static readonly double[] cpuHistory = new double[6];
    private static readonly double[] gpuHistory = new double[6];
    private static bool gfxWatchdogPendingStop;

    private static Processor processor = new();
    private static double processValueFPSPrevious;
    private static double storedGfxClock;

    // TDP limits
    private static readonly double[] storedTDP = new double[3]; // used to store TDP

    private static bool IsInitialized;

    public static event InitializedEventHandler Initialized;
    public delegate void InitializedEventHandler();

    static long lastAutoTdp;
    static long lastAutoCpu;

    static PerformanceManager()
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

        // manage events
        PowerProfileManager.Applied += PowerProfileManager_Applied;
        PowerProfileManager.Discarded += PowerProfileManager_Discarded;
        SettingsManager.SettingValueChanged += SettingsManager_SettingValueChanged;

        currentCoreCount = MotherboardInfo.NumberOfCores;
    }

    private static void SettingsManager_SettingValueChanged(string name, object value)
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

    private static void PowerProfileManager_Applied(PowerProfile profile, UpdateSource source)
    {
        // apply profile defined TDP
        if (profile.TDPOverrideEnabled && profile.TDPOverrideValues is not null)
        {
            // Manual TDP is set, use it and set max limit
            RequestTDP(profile.TDPOverrideValues, profile.AutoTDPEnabled);
            if (!profile.AutoTDPEnabled)
            {
                StartTDPWatchdog();
                AutoTDPMax = SettingsManager.Get<int>("ConfigurableTDPOverrideUp");
            }
            else
            {
                // Both manual TDP and AutoTDP are on,
                // use manual slider as the max limit for AutoTDP
                AutoTDPMax = profile.TDPOverrideValues[0];
                StopTDPWatchdog(true);
            }
        }
        else 
        {
            if (cpuWatchdog.Enabled)
                StopTDPWatchdog(true);

            if (!profile.AutoTDPEnabled)
            {
                // Neither manual TDP nor AutoTDP is enabled, restore default TDP
                RestoreTDP(true);
            }
            else
            {
                // AutoTDP is enabled but manual override is not, use the settings max limit
                AutoTDPMax = SettingsManager.Get<int>("ConfigurableTDPOverrideUp");
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
            // restore default GPU clock
            RestoreCPUClock(true);
            AutoCPUClockMin = MotherboardInfo.ProcessorMaxClockSpeed / 2;
            AutoCPUClockMax = MotherboardInfo.ProcessorMaxTurboSpeed;
        }

        // apply profile defined GPU
        if (profile.GPUOverrideEnabled)
        {
            AutoGPUClockMin = IDevice.GetCurrent().GfxClock[0] * 2;
            AutoGPUClockMax = profile.GPUOverrideValue;
            if (!profile.AutoTDPEnabled)
            {
                RequestGPUClock(profile.GPUOverrideValue);
                StartGPUWatchdog();
            }
            else
                StopGPUWatchdog(true);
        }
        else
        {
            AutoGPUClockMin = IDevice.GetCurrent().GfxClock[0] * 2;
            AutoGPUClockMax = IDevice.GetCurrent().GfxClock[1];

            if (gfxWatchdog.Enabled)
                StopGPUWatchdog(true);

            // restore default GPU clock
            if (!profile.AutoTDPEnabled)
                RestoreGPUClock(true);
        }

        // apply profile defined AutoTDP
        if (profile.AutoTDPEnabled)
        {
            AutoTDPTargetFPS = profile.AutoTDPRequestedFPS;
            AutoTargetCPU = 97;
            AutoTargetGPU = 97;

            //ClampAutoTDPClockMax();
            StartAutoTDPWatchdog();
        }
        else
        {
            if (autoWatchdog.Enabled)
            {
                StopAutoTDPWatchdog(true);
                RequestMaxPerformance(false);

                // restore default TDP (if not manual TDP is enabled)
                if (!profile.TDPOverrideEnabled)
                    RestoreTDP(true);
            }
        }


        // apply profile defined EPP
        if (profile.EPPOverrideEnabled)
        {
            RequestEPP(profile.EPPOverrideValue);
        }
        else if (currentEPP != 0x00000032)
        {
            // restore default EPP
            RequestEPP(0x00000032);
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

        // apply profile define CPU Boost
        RequestPerfBoostMode((uint)profile.CPUBoostLevel);

        // apply profile Power mode
        RequestPowerMode(profile.OSPowerMode);

        // apply profile Fan mode
        switch (profile.FanProfile.FanMode)
        {
            default:
            case FanMode.Hardware:
                IDevice.GetCurrent().SetFanControl(false, profile.OEMPowerMode);
                break;
            case FanMode.Software:
                IDevice.GetCurrent().SetFanControl(true);
                break;
        }
    }

    private static void ClampAutoTDPClockMax()
    {
        var cpuClockMax = AutoCPUClockMax;
        var cpuClockMin = AutoCPUClockMin;
        var gpuClockMax = AutoGPUClockMax;
        var gpuClockMin = AutoGPUClockMin;

        switch (TDPMin)
        {
            case < 6: cpuClockMin = 1500; break;
            case >= 6 and < 7: cpuClockMin = 1500; break;
        }

        switch (AutoTDPMax)
        {
            case < 6: cpuClockMax = 1500; gpuClockMax = 400; break;
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

        AutoCPUClockMax = Math.Clamp(cpuClockMax, AutoCPUClockMin, AutoCPUClockMax);
        AutoGPUClockMax = Math.Clamp(gpuClockMax, AutoGPUClockMin, AutoGPUClockMax);
        AutoCPUClockMin = Math.Clamp(cpuClockMin, Math.Min(cpuClockMin, AutoCPUClockMax), AutoCPUClockMax);
        AutoGPUClockMin = Math.Clamp(gpuClockMin, Math.Min(gpuClockMin, AutoGPUClockMax), AutoGPUClockMax);
    }

    private static void PowerProfileManager_Discarded(PowerProfile profile)
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
        if (profile.EPPOverrideEnabled)
        {
            // restore default EPP
            RequestEPP(0x00000032);
        }

        // unapply profile defined CPU Core Count
        if (profile.CPUCoreEnabled)
        {
            RequestCPUCoreCount(MotherboardInfo.NumberOfCores);
        }

        // restore profile define CPU Boost
        RequestPerfBoostMode((uint)PerfBoostMode.Disabled);

        // restore OSPowerMode.BetterPerformance 
        RequestPowerMode(OSPowerMode.BetterPerformance);

        // restore default Fan mode
        IDevice.GetCurrent().SetFanControl(false, profile.OEMPowerMode);
    }

    private static void RestoreTDP(bool immediate)
    {
        for (PowerType pType = PowerType.Slow; pType <= PowerType.Fast; pType++)
            RequestTDP(pType, IDevice.GetCurrent().cTDP[1], immediate);
    }

    private static void RestoreCPUClock(bool immediate)
    {
        RequestCPUClock(0x00000000/*IDevice.GetCurrent().CpuClock*/, immediate);
    }

    private static void RestoreGPUClock(bool immediate)
    {
        RequestGPUClock(255 * 50, immediate);
    }

    private static void AutoTDPWatchdog_Elapsed(object? sender, ElapsedEventArgs e)
    {
        if (!PlatformManager.RTSS.HasHook())
            return;

        if (autoLock.TryEnter())
        {
            // todo: Store fps for data gathering from multiple points (OSD, Performance)
            var processValueFPS = PlatformManager.RTSS.GetFramerate(true);
            if (processValueFPS == 0)
                goto Exit;

            // Ensure realistic process values, prevent divide by 0
            processValueFPS = Math.Clamp(processValueFPS, 5, 500);

            if (AutoTDPFirstRun)
            {
                RequestMaxPerformance(true);
                AutoTDPDipper(processValueFPS, AutoTDPTargetFPS);
                AutoTDPFirstRun = false;
                ToastManager.RunToast($"AutoTDP {Resources.On}");
            }
            else
            {
                AutoPerf(processValueFPS);
            }

        // release lock
        Exit:
            autoLock.Exit();
        }
    }

    private static void AutoPerf(double processValueFPS)
    {
        var processFPSTarget = AutoTDPTargetFPS;

        var fpsTarget = Math.Clamp(fpsHistory.Max(), 1, processFPSTarget);
        var fpsDipper = AutoTDPDipper(processValueFPS, fpsTarget);

        /*
        var processValueCPUUse = Math.Clamp(PlatformManager.LibreHardwareMonitor.CPULoad ?? 0d, .1d, 100d);
        var fpsCPU = Math.Max(processValueFPS * 2 - fpsTarget, Math.Max(fpsTarget / 2, 1));
        AutoCpu(fpsTarget / fpsCPU, processValueCPUUse, AutoTargetCPU);

        var processValueGPUUse = Math.Clamp((double)PlatformManager.LibreHardwareMonitor.GPULoad, .1d, 100d);
        var fpsGPU = Math.Max(processValueFPS, Math.Max(fpsTarget * .8, 1));
        AutoGpu(fpsDipper > 0 ? 1d : fpsTarget / fpsGPU, processValueGPUUse, AutoTargetGPU);
        */

        AutoTdp(fpsDipper, processValueFPS, fpsTarget);

        //LogManager.LogInformation($"AutoPerf: GPU {PlatformManager.LibreHardwareMonitor.GPUClock}mHz [{PlatformManager.LibreHardwareMonitor.GPUPower}W] | CPU {PlatformManager.LibreHardwareMonitor.CPUClock}mHz [{Math.Round(PlatformManager.LibreHardwareMonitor.CPUPower ?? 0d)}W] | TDP [{(uint)AutoTDP}W]({processValueFPS})");

    }

    private static void AutoTdp(double fpsDipper, double fpsActual, double fpsSetPoint)
    {
        Array.Copy(tdpHistory, 0, tdpHistory, 1, tdpHistory.Length - 1);
        tdpHistory[0] = AutoTDP;

        // Determine final setpoint
        if (!AutoTDPFirstRun)
        {
            // Determine error amount, include target, actual and dipper modifier
            // Clamp error amount corrected within a single cycle
            // Adjust clamp if actual FPS is 2.5x requested FPS
            //
            var controllerError = Math.Clamp(fpsSetPoint - fpsActual - fpsDipper, fpsActual >= 2.5 * fpsSetPoint ? -100d : -5d, 15);
            var tdpAdjustment = controllerError * AutoTDP / fpsActual * .9;// Always have a little undershoot
            var tdpDamper = AutoTDPDamper(fpsActual);

            LogManager.LogDebug($"AutoTDP ({fpsActual}FPS): {AutoTDP + tdpAdjustment + tdpDamper} = {AutoTDP} + {tdpAdjustment} + {tdpDamper}");

            if (tdpAdjustment == 0 && tdpDamper == 0)
            {
                return;
            }
            else
                AutoTDP += tdpAdjustment + tdpDamper;
        }
        else
            AutoTDPFirstRun = false;

        AutoTDP = Math.Clamp(AutoTDP, TDPMin, AutoTDPMax);

        // Only update if we have a different TDP value to set
        if ((uint)AutoTDP != (uint)AutoTDPPrev)
            RequestTDP([AutoTDP, AutoTDP, AutoTDP], true);

        AutoTDPPrev = AutoTDP;
    }

    private static void AutoCpu(double fpsDipper, double cpuActual, double cpuSetPoint)
    {
        Array.Copy(cpuHistory, 0, cpuHistory, 1, cpuHistory.Length - 1);
        cpuHistory[0] = cpuActual;

        var cpuAvgCounter = 2;
        var cpuAdjustment = 0.0d;
        var cpuOffset = 0.0d;
        var cpuAvg = cpuHistory.Take(cpuAvgCounter).Average() + cpuAdjustment;
        var cpuTarget = Math.Clamp(cpuAvg, 1, cpuSetPoint);
        var cpuCurrent = Math.Min(cpuActual, cpuAvg * 2);

        var cpuClock = (AutoCPUClock * fpsDipper * cpuCurrent / cpuTarget) + cpuOffset;
        LogManager.LogDebug($"AutoCPU: {(AutoCPUClock * fpsDipper * cpuCurrent / cpuTarget) + cpuOffset} = {AutoCPUClock} * {fpsDipper} * {cpuCurrent} / {cpuTarget}");

        AutoCPUClock = Math.Clamp(cpuClock, AutoCPUClockMin, AutoCPUClockMax);
        RequestCPUClock(AutoCPUClock, true);
    }

    private static void AutoGpu(double fpsDipper, double gpuActual, double gpuSetPoint)
    {
        Array.Copy(gpuHistory, 0, gpuHistory, 1, gpuHistory.Length - 1);
        gpuHistory[0] = gpuActual;

        var gpuAvgCounter = 2;
        var gpuAdjustment = 1.5d/* + Math.Clamp(fpsDipper, 0.0d, 2.0d)*/;
        var gpuOffset = 0.0d;
        var gpuAvg = gpuHistory.Take(gpuAvgCounter).Average() + gpuAdjustment;
        var gpuTarget = Math.Clamp(gpuAvg, 1, gpuSetPoint);
        var gpuCurrent = Math.Min(gpuActual, gpuAvg * 2);

        //LogManager.LogDebug($"AutoGPU - {(AutoGPUClock * fpsDipper * gpuCurrent / gpuTarget) + gpuOffset} = {AutoGPUClock} * {fpsDipper} * {gpuCurrent} / {gpuTarget} ({gpuAdjustment})");
        var gpuClock = (AutoGPUClock * fpsDipper * gpuCurrent / gpuTarget) + gpuOffset;

        AutoGPUClock = Math.Clamp(gpuClock, AutoGPUClockMin, AutoGPUClockMax);
        RequestGPUClock(AutoGPUClock, true);
    }

    private static double AutoTDPDipper(double fpsActual, double fpsSetPoint)
    {
        // Dipper
        // Add small positive "error" if actual and target FPS are similar for a duration
        double modifier = 0.0d;

        // Track previous FPS values for average calculation using a rolling array
        Array.Copy(fpsHistory, 0, fpsHistory, 1, fpsHistory.Length - 1);
        fpsHistory[0] = fpsActual; // Add current FPS at the start

        // Activate around target range of 1 FPS as games can fluctuate
        if (fpsSetPoint - 1 <= fpsActual && fpsActual <= fpsSetPoint + 1)
        {
            AutoTDPFPSSetpointMetCounter++;

            // First wait for three seconds of stable FPS arount target, then perform small dip
            // Reduction only happens if average FPS is on target or slightly below
            if (AutoTDPFPSSetpointMetCounter >= 3 && AutoTDPFPSSetpointMetCounter < 6 &&
                fpsSetPoint - 0.5 <= fpsHistory.Take(3).Average() && fpsHistory.Take(3).Average() <= fpsSetPoint + 0.1)
            {
                AutoTDPFPSSmallDipCounter++;
                modifier = fpsSetPoint + 0.5 - fpsActual;
            }
            // After three small dips, perform larger dip 
            // Reduction only happens if average FPS is on target or slightly below
            else if (AutoTDPFPSSmallDipCounter >= 3 &&
                     fpsSetPoint - 0.5 <= fpsHistory.Average() && fpsHistory.Average() <= fpsSetPoint + 0.1)
            {
                modifier = fpsSetPoint + 1.5 - fpsActual;
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

    private static double AutoTDPDamper(double fpsActual)
    {
        // (PI)D derivative control component to dampen FPS fluctuations
        if (double.IsNaN(processValueFPSPrevious)) processValueFPSPrevious = fpsActual;
        double DFactor = -0.1d;

        // Calculation
        double deltaError = fpsActual - processValueFPSPrevious;
        double DTerm = deltaError / (INTERVAL_AUTO / 1000.0);
        double TDPDamping = AutoTDP / fpsActual * DFactor * DTerm;

        processValueFPSPrevious = fpsActual;

        return TDPDamping;
    }

    // todo: update this function to force (re)apply profile settings
    private static void powerWatchdog_Elapsed(object? sender, ElapsedEventArgs e)
    {
        if (powerLock.TryEnter())
        {
            // Checking if active power shceme has changed to reflect that
            if (PowerManager.GetPowerMode() is Guid activeScheme)
            {
                if (activeScheme != currentPowerMode)
                {
                    currentPowerMode = activeScheme;
                    int idx = Array.IndexOf(PowerModes, activeScheme);
                    if (idx != -1)
                        PowerModeChanged?.Invoke(idx);
                }
            }

            // read perfboostmode
            var result = PowerSchemeAPI.GetActivePlanSetting(SettingSubgroup.PROCESSOR_SUBGROUP, Setting.PERFBOOSTMODE);
            var perfboostmode = result.ACValue;

            if (perfboostmode != currentPerfBoostMode)
            {
                currentPerfBoostMode = perfboostmode;
                PerfBoostModeChanged?.Invoke(perfboostmode);
            }

            // Checking if current EPP value has changed to reflect that
            var (ACValue, DCValue) = PowerSchemeAPI.GetActivePlanSetting(SettingSubgroup.PROCESSOR_SUBGROUP, Setting.PERFEPP);
            var DCvalue = DCValue;

            if (DCvalue != currentEPP)
            {
                currentEPP = DCvalue;
                EPPChanged?.Invoke(DCvalue);
            }

        // release lock
        Exit:
            powerLock.Exit();
        }
    }

    private static async void cpuWatchdog_Elapsed(object? sender, ElapsedEventArgs e)
    {
        if (processor is null || !processor.IsInitialized)
            return;

        if (cpuLock.TryEnter())
        {
            var TDPdone = false;
            var MSRdone = false;

            // read current values and (re)apply requested TDP if needed
            foreach (PowerType type in (PowerType[])Enum.GetValues(typeof(PowerType)))
            {
                var idx = (int)type;

                // skip msr
                if (idx >= storedTDP.Length)
                    break;

                var TDP = storedTDP[idx];
                if (TDP == 0.0d)
                    continue;

                if (processor is AMDProcessor)
                {
                    // AMD reduces TDP by 10% when OS power mode is set to Best power efficiency
                    if (currentPowerMode == OSPowerMode.BetterBattery)
                        TDP = (int)Math.Truncate(TDP * 0.9);

                    // AMD doesn't have MSR
                    if (type == PowerType.MsrSlow || type == PowerType.MsrFast)
                        continue;
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
                {
                    RequestTDP(type, TDP, true);
                }

                await Task.Delay(20);
            }

            // are we done ?
            TDPdone = currentTDP[(int)PowerType.Slow] == storedTDP[(int)PowerType.Slow] &&
                currentTDP[(int)PowerType.Stapm] == storedTDP[(int)PowerType.Stapm] &&
                currentTDP[(int)PowerType.Fast] == storedTDP[(int)PowerType.Fast];

            // processor specific
            if (processor is IntelProcessor)
            {
                int TDPslow = (int)storedTDP[(int)PowerType.Slow];
                int TDPfast = (int)storedTDP[(int)PowerType.Fast];

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
        Exit:
            cpuLock.Exit();
        }
    }

    private static void gfxWatchdog_Elapsed(object? sender, ElapsedEventArgs e)
    {
        if (processor is null || !processor.IsInitialized)
            return;

        if (gfxLock.TryEnter())
        {

            bool GPUdone = false;
            GPU gpu = GPUManager.GetCurrent();
            if (gpu is null)
            {
                // release lock
                goto Exit;
            }

            currentGfxClock = (uint)GPUManager.GetCurrent().GetClock();

            if (currentGfxClock != 0)
                gfxWatchdog.Interval = INTERVAL_DEFAULT;
            else
                gfxWatchdog.Interval = INTERVAL_DEGRADED;

            // not ready yet
            if (storedGfxClock == 0)
            {
                // release lock
                goto Exit;
            }

            // only request an update if current gfx clock is different than stored
            if (currentGfxClock != storedGfxClock)
            {
                // disabling
                if (storedGfxClock == 12750)
                    GPUdone = true;
                else
                    //processor.SetGPUClock(storedGfxClock, true);
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
        Exit:
            gfxLock.Exit();
        }
    }

    private static void StartGPUWatchdog()
    {
        gfxWatchdogPendingStop = false;
        gfxWatchdog.Interval = INTERVAL_DEFAULT;
        gfxWatchdog.Start();
    }

    private static void StopGPUWatchdog(bool immediate = false)
    {
        gfxWatchdogPendingStop = true;
        if (immediate)
            gfxWatchdog.Stop();
    }

    private static void StartTDPWatchdog()
    {
        cpuWatchdogPendingStop = false;
        cpuWatchdog.Interval = INTERVAL_DEFAULT;
        cpuWatchdog.Start();
    }

    private static void StopTDPWatchdog(bool immediate = false)
    {
        cpuWatchdogPendingStop = true;
        if (immediate)
            cpuWatchdog.Stop();
    }

    private static void StartAutoTDPWatchdog()
    {
        autoWatchdog.Start();
        AutoTDPFirstRun = true;
    }

    private static void StopAutoTDPWatchdog(bool immediate = false)
    {
        autoWatchdog.Stop();
        if (!AutoTDPFirstRun)
            ToastManager.RunToast($"AutoTDP {Resources.Off}");
    }

    private static void RequestTDP(PowerType type, double value, bool immediate = false)
    {
        if (processor is null || !processor.IsInitialized)
            return;

        // make sure we're not trying to run below or above specs
        value = Math.Min(TDPMax, Math.Max(TDPMin, value));

        // update value read by timer
        int idx = (int)type;
        storedTDP[idx] = value;

        // immediately apply
        if (immediate)
        {
            processor.SetTDPLimit((PowerType)idx, value, immediate);
            currentTDP[idx] = value;
        }
    }

    private static async void RequestTDP(double[] values, bool immediate = false)
    {
        for (int idx = (int)PowerType.Slow; idx <= (int)PowerType.Fast; idx++)
        {
            RequestTDP((PowerType)idx, values[idx], immediate);
            await Task.Delay(20);
        }
    }

    private static void RequestGPUClock(double value, bool immediate = false)
    {
        if (processor is null || !processor.IsInitialized)
            return;

        // update value read by timer
        storedGfxClock = (uint)value;

        if (currentGfxClock == storedGfxClock)
            return;

        // immediately apply
        if (immediate)
        {
            processor.SetGPUClock(value, immediate);
            currentGfxClock = (uint)value;
        }
    }

    private static void RequestPowerMode(Guid guid)
    {
        if (currentPowerMode != guid)
        {
            PowerSchemeAPI.SetPowerMode(guid);
            LogManager.LogInformation("User requested power mode: {0}", guid);

            if (PowerSchemeAPI.GetPowerMode() is Guid curGuid && curGuid != guid)
                LogManager.LogWarning("Failed to set requested power mode: {0}", curGuid);
            else
                currentPowerMode = guid;
        }
    }

    private static void RequestEPP(uint EPPOverrideValue)
    {
        currentEPP = EPPOverrideValue;

        var requestedEPP = (ACValue: (uint)Math.Max(0, (int)EPPOverrideValue - 10), DCValue: (uint)Math.Max(0, (int)EPPOverrideValue));

        // Is the EPP value already correct?
        var EPP = PowerSchemeAPI.GetActivePlanSetting(SettingSubgroup.PROCESSOR_SUBGROUP, Setting.PERFEPP);
        if (EPP.ACValue == requestedEPP.ACValue && EPP.DCValue == requestedEPP.DCValue)
            return;

        LogManager.LogDebug("User requested EPP AC: {0}, DC: {1}", requestedEPP.ACValue, requestedEPP.DCValue);

        // Set profile EPP
        PowerSchemeAPI.SetActivePlanSetting(SettingSubgroup.PROCESSOR_SUBGROUP, [Setting.PERFEPP, Setting.PERFEPP1], requestedEPP.ACValue, requestedEPP.DCValue);

        // Has the EPP value been applied?
        EPP = PowerSchemeAPI.GetActivePlanSetting(SettingSubgroup.PROCESSOR_SUBGROUP, Setting.PERFEPP);
        if (EPP.ACValue != requestedEPP.ACValue || EPP.DCValue != requestedEPP.DCValue)
            LogManager.LogWarning("Failed to set requested EPP");
    }

    private static void RequestCPUCoreCount(int CoreCount)
    {
        currentCoreCount = CoreCount;

        uint currentCoreCountPercent = (uint)(100.0d / MotherboardInfo.NumberOfCores * CoreCount);

        // Is the CPMINCORES value already correct?
        var CPMINCORES = PowerSchemeAPI.GetActivePlanSetting(SettingSubgroup.PROCESSOR_SUBGROUP, Setting.CPMINCORES);
        bool CPMINCORESReady = CPMINCORES.ACValue == currentCoreCountPercent && CPMINCORES.DCValue == currentCoreCountPercent;

        // Is the CPMAXCORES value already correct?
        var CPMAXCORES = PowerSchemeAPI.GetActivePlanSetting(SettingSubgroup.PROCESSOR_SUBGROUP, Setting.CPMAXCORES);
        bool CPMAXCORESReady = CPMAXCORES.ACValue == currentCoreCountPercent && CPMAXCORES.DCValue == currentCoreCountPercent;

        if (CPMINCORESReady && CPMAXCORESReady)
            return;

        // Set profile CPMINCORES and CPMAXCORES
        PowerSchemeAPI.SetActivePlanSetting(SettingSubgroup.PROCESSOR_SUBGROUP, [Setting.CPMINCORES, Setting.CPMINCORES1], currentCoreCountPercent, currentCoreCountPercent);
        PowerSchemeAPI.SetActivePlanSetting(SettingSubgroup.PROCESSOR_SUBGROUP, [Setting.CPMAXCORES, Setting.CPMAXCORES1], currentCoreCountPercent, currentCoreCountPercent);

        LogManager.LogDebug("User requested CoreCount: {0} ({1}%)", CoreCount, currentCoreCountPercent);

        // Has the CPMINCORES value been applied?
        CPMINCORES = PowerSchemeAPI.GetActivePlanSetting(SettingSubgroup.PROCESSOR_SUBGROUP, Setting.CPMINCORES);
        if (CPMINCORES.ACValue != currentCoreCountPercent || CPMINCORES.DCValue != currentCoreCountPercent)
            LogManager.LogWarning("Failed to set requested CPMINCORES");

        // Has the CPMAXCORES value been applied?
        CPMAXCORES = PowerSchemeAPI.GetActivePlanSetting(SettingSubgroup.PROCESSOR_SUBGROUP, Setting.CPMAXCORES);
        if (CPMAXCORES.ACValue != currentCoreCountPercent || CPMAXCORES.DCValue != currentCoreCountPercent)
            LogManager.LogWarning("Failed to set requested CPMAXCORES");
    }

    private static void RequestPerfBoostMode(uint value)
    {
        // read perfboostmode
        if (currentPerfBoostMode == default)
        {
            var (ACValue, DCValue) = PowerSchemeAPI.GetActivePlanSetting(SettingSubgroup.PROCESSOR_SUBGROUP, Setting.PERFBOOSTMODE);
            var resultPerfboostmode = ACValue;
            currentPerfBoostMode = resultPerfboostmode;
        }

        if (currentPerfBoostMode != value)
        {
            currentPerfBoostMode = value;
            var perfboostmode = value;
            PowerSchemeAPI.SetActivePlanSetting(SettingSubgroup.PROCESSOR_SUBGROUP, Setting.PERFBOOSTMODE, perfboostmode, perfboostmode);
            LogManager.LogDebug("User requested perfboostmode: {0}", value);
        }
    }

    private static void RequestCPUClock(double cpuClock, bool immediate = false)
    {
        double maxClock = MotherboardInfo.ProcessorMaxTurboSpeed;

        // Is the PROCFREQMAX value already correct?
        var currentClock = PowerSchemeAPI.GetActivePlanSetting(SettingSubgroup.PROCESSOR_SUBGROUP, Setting.PROCFREQMAX);
        var isReady = currentClock.ACValue == (uint)cpuClock && currentClock.DCValue == (uint)cpuClock;

        if (isReady)
            return;

        PowerSchemeAPI.SetActivePlanSetting(SettingSubgroup.PROCESSOR_SUBGROUP, [Setting.PROCFREQMAX, Setting.PROCFREQMAX1], (uint)cpuClock, (uint)cpuClock);

        if (!immediate)
        {
            double cpuPercentage = cpuClock / maxClock * 100.0d;
            LogManager.LogDebug("User requested PROCFREQMAX: {0} ({1}%)", cpuClock, cpuPercentage);
        }

        // Has the value been applied?
        currentClock = PowerSchemeAPI.GetActivePlanSetting(SettingSubgroup.PROCESSOR_SUBGROUP, Setting.PROCFREQMAX);
        if (currentClock.ACValue != (uint)cpuClock || currentClock.DCValue != (uint)cpuClock)
            LogManager.LogWarning("Failed to set requested PROCFREQMAX");
    }

    private static void RequestMaxPerformance(bool immediate = false)
    {
        if (processor is null || !processor.IsInitialized)
            return;

        var (ACValue, DCValue) = (100u, 5u);
        if (immediate)
        {
            ACValue = 5u;
            processor.SetMaxPerformance();
        }

        var values = PowerSchemeAPI.GetActivePlanSetting(SettingSubgroup.PROCESSOR_SUBGROUP, Setting.PROCTHROTTLEMIN);
        // Is the ProcThrottleMin value already correct?
        if (values.ACValue == ACValue &&
            values.DCValue == DCValue)
            return;

        if (!immediate)
            LogManager.LogDebug("User requested ProcThrottleMin AC: {0}, DC: {1}", ACValue, DCValue);

        // Set ProcThrottleMin
        PowerSchemeAPI.SetActivePlanSetting(SettingSubgroup.PROCESSOR_SUBGROUP, [Setting.PROCTHROTTLEMIN, Setting.PROCTHROTTLEMIN1], ACValue, DCValue);

        // Has the ProcThrottleMin value been applied?
        values = PowerSchemeAPI.GetActivePlanSetting(SettingSubgroup.PROCESSOR_SUBGROUP, Setting.PROCTHROTTLEMIN);
        if (values.ACValue != ACValue ||
            values.DCValue != DCValue)
            LogManager.LogWarning("Failed to set requested ProcThrottleMin");
    }

    public static void Start()
    {
        // initialize watchdog(s)
        powerWatchdog.Start();

        // initialize processor
        processor = Processor.GetCurrent();

        if (processor is not null && processor.IsInitialized)
        {
            processor.StatusChanged += Processor_StatusChanged;
            processor.Initialize();
        }
        else
        {
            ProcessorStatusChanged?.Invoke(false, false);
        }

        IsInitialized = true;
        Initialized?.Invoke();

        LogManager.LogInformation("{0} has started", "PerformanceManager");
    }

    public static void Stop()
    {
        if (!IsInitialized)
            return;

        if (processor is not null && processor.IsInitialized)
            processor.Stop();

        powerWatchdog.Stop();
        cpuWatchdog.Stop();
        gfxWatchdog.Stop();
        autoWatchdog.Stop();

        IsInitialized = false;

        LogManager.LogInformation("{0} has stopped", "PerformanceManager");
    }

    public static void Resume(bool OS)
    {
        foreach (PowerType type in (PowerType[])Enum.GetValues(typeof(PowerType)))
        {
            int idx = (int)type;
            currentTDP[idx] = 0;
        }
    }
    public static Processor GetProcessor() => processor;

    #region imports

    /// <summary>
    ///     Retrieves the active overlay power scheme and returns a GUID that identifies the scheme.
    /// </summary>
    /// <param name="EffectiveOverlayGuid">A pointer to a GUID structure.</param>
    /// <returns>Returns zero if the call was successful, and a nonzero value if the call failed.</returns>
    [DllImportAttribute("powrprof.dll", EntryPoint = "PowerGetEffectiveOverlayScheme")]
    private static extern uint PowerGetEffectiveOverlayScheme(out Guid EffectiveOverlayGuid);

    /// <summary>
    ///     Sets the active power overlay power scheme.
    /// </summary>
    /// <param name="OverlaySchemeGuid">The identifier of the overlay power scheme.</param>
    /// <returns>Returns zero if the call was successful, and a nonzero value if the call failed.</returns>
    [DllImportAttribute("powrprof.dll", EntryPoint = "PowerSetActiveOverlayScheme")]
    private static extern uint PowerSetActiveOverlayScheme(Guid OverlaySchemeGuid);

    #endregion

    #region events

    public static event LimitChangedHandler PowerLimitChanged;

    public delegate void LimitChangedHandler(PowerType type, int limit);

    public static event ValueChangedHandler PowerValueChanged;

    public delegate void ValueChangedHandler(PowerType type, float value);

    public static event StatusChangedHandler ProcessorStatusChanged;

    public delegate void StatusChangedHandler(bool CanChangeTDP, bool CanChangeGPU);

    public static event PowerModeChangedEventHandler PowerModeChanged;

    public delegate void PowerModeChangedEventHandler(int idx);

    public static event PerfBoostModeChangedEventHandler PerfBoostModeChanged;

    public delegate void PerfBoostModeChangedEventHandler(uint value);

    public static event EPPChangedEventHandler EPPChanged;

    public delegate void EPPChangedEventHandler(uint EPP);

    #endregion

    #region events
    private static void Processor_StatusChanged(bool CanChangeTDP, bool CanChangeGPU)
    {
        ProcessorStatusChanged?.Invoke(CanChangeTDP, CanChangeGPU);
    }

    #endregion
}
