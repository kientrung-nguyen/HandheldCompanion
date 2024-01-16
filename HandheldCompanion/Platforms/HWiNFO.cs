using HandheldCompanion.Managers;
using HandheldCompanion.Processors;
using HandheldCompanion.Utils;
using Hwinfo.SharedMemory;
using Microsoft.WindowsAPICodePack.Sensors;
using RTSSSharedMemoryNET;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Timers;
using System.Windows;
using Timer = System.Timers.Timer;

namespace HandheldCompanion.Platforms;

public class HWiNFO : IPlatform
{
    public enum SensorElementType
    {
        CPUTemperature,
        CPUFrequency,
        CPUFrequencyEffective,
        CPUPower,
        CPUUsage,
        CPUCoreRatio,
        CPUBusClock,


        GPUTemperature,
        GPUFrequency,
        GPUFrequencyEffective,
        GPUPower,
        GPUUsage,
        GPUMemoryUsage,

        PL1,
        PL2,
        PL3,

        BatteryChargeRate,
        BatteryChargeLevel,
        BatteryRemainingCapacity,
        BatteryRemainingTime,

        PhysicalMemoryUsage,
        VirtualMemoryUsage,

    }

    private int MemoryInterval = SettingsManager.GetInt("OnScreenDisplayRefreshRate");
    private const int PlatformInterval = 3000;
    private readonly Timer HWiNFOWatchdog;
    private SharedMemoryReader HWiNFOReader;
    public readonly ConcurrentDictionary<SensorElementType, SensorReading> MonitoredSensors = new();

    public HWiNFO()
    {
        PlatformType = PlatformType.HWiNFO;
        ExpectedVersion = new Version(7, 42, 5030);
        Url = "https://www.hwinfo.com/files/hwi_742.exe";

        Name = "HWiNFO64";
        ExecutableName = RunningName = "HWiNFO64.exe";

        // check if platform is installed
        InstallPath = RegistryUtils.GetString(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\HWiNFO64_is1", "InstallLocation");
        if (Path.Exists(InstallPath))
        {
            // update paths
            SettingsPath = Path.Combine(InstallPath, "HWiNFO64.ini");
            ExecutablePath = Path.Combine(InstallPath, ExecutableName);

            // check executable
            if (File.Exists(ExecutablePath))
            {
                // check executable version
                var versionInfo = FileVersionInfo.GetVersionInfo(ExecutablePath);
                var CurrentVersion = new Version(versionInfo.ProductMajorPart, versionInfo.ProductMinorPart,
                    versionInfo.ProductBuildPart);

                if (CurrentVersion < ExpectedVersion)
                {
                    LogManager.LogWarning("HWiNFO is outdated. Please get it from: {0}", Url);
                    return;
                }

                IsInstalled = true;
            }
        }

        if (!IsInstalled)
        {
            LogManager.LogWarning("HWiNFO is missing. Please get it from: {0}", Url);
            return;
        }

        // those are used for computes
        MonitoredSensors[SensorElementType.PL1] = new();
        MonitoredSensors[SensorElementType.PL2] = new();
        MonitoredSensors[SensorElementType.PL3] = new();


        // file watcher
        if (File.Exists(SettingsPath))
        {
            systemWatcher = new(Path.GetDirectoryName(SettingsPath))
            {
                Filter = "*.ini",
                EnableRaisingEvents = true
            };
            systemWatcher.Changed += SystemWatcher_Changed;
        }

        // our main watchdog to (re)apply requested settings
        PlatformWatchdog = new Timer(PlatformInterval) { Enabled = false };
        PlatformWatchdog.Elapsed += (sender, e) => PlatformWatchdogElapsed();

        // secondary watchdog to (re)populate sensors
        HWiNFOWatchdog = new Timer(MemoryInterval) { Enabled = false };
        HWiNFOWatchdog.Elapsed += (sender, e) => PopulateSensors();
    }

    private void SystemWatcher_Changed(object sender, FileSystemEventArgs e)
    {
        bool SensorsSM = GetProperty("SensorsSM");
        SystemWatcher_Changed("SensorsSM", SensorsSM);
    }

    public override bool Start()
    {
        // start HWiNFO if not running or Shared Memory is disabled
        var hasSensorsSM = GetProperty("SensorsSM");
        if (!IsRunning || !hasSensorsSM)
        {
            StopProcess();
            StartProcess();
        }
        else
        {
            // hook into current process
            Process.Exited += Process_Exited;
        }

        SettingsManager.SettingValueChanged += SettingsManager_SettingValueChanged;

        HWiNFOReader = new();

        // our main watchdog to (re)apply requested settings
        PlatformWatchdog = new Timer(PlatformInterval) { Enabled = false };
        PlatformWatchdog.Elapsed += (sender, e) => PlatformWatchdogElapsed();

        return base.Start();
    }

    public override bool Stop(bool kill = false)
    {
        HWiNFOWatchdog?.Stop();
        HWiNFOReader?.Dispose();
        SettingsManager.SettingValueChanged -= SettingsManager_SettingValueChanged;
        return base.Stop(kill);
    }

    private void SettingsManager_SettingValueChanged(string name, object value)
    {
        // UI thread (async)
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            switch (name)
            {
                case "OnScreenDisplayRefreshRate":
                    SetProperty("SensorInterval", Convert.ToInt32(value));
                    if (Convert.ToInt32(value) != MemoryInterval)
                        MemoryInterval = Convert.ToInt32(value);

                    break;
            }
        });
    }

    private void PlatformWatchdogElapsed()
    {
        // reset tentative counter
        Tentative = 0;
        //HWiNFOWatchdog?.Start();
    }

    /*
    public void GetSensors()
    {
        try
        {
            for (uint index = 0; index < HWiNFOMemory.dwNumSensorElements; ++index)
                using (var viewStream = MemoryMapped.CreateViewStream(
                           HWiNFOMemory.dwOffsetOfSensorSection + index * HWiNFOMemory.dwSizeOfSensorElement,
                           HWiNFOMemory.dwSizeOfSensorElement, MemoryMappedFileAccess.Read))
                {
                    var buffer = new byte[(int)HWiNFOMemory.dwSizeOfSensorElement];
                    viewStream.Read(buffer, 0, (int)HWiNFOMemory.dwSizeOfSensorElement);
                    var gcHandle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
                    var structure =
                        (SensorStructure)Marshal.PtrToStructure(gcHandle.AddrOfPinnedObject(), typeof(SensorStructure));
                    gcHandle.Free();
                    var sensor = new Sensor
                    {
                        NameOrig = structure.szSensorNameOrig,
                        NameUser = structure.szSensorNameUser,
                        Elements = new ConcurrentDictionary<uint, SensorElement>()
                    };
                    HWSensors[index] = sensor;

                }
        }
        catch
        {
            // do something
        }
    }
    */
    public void PopulateSensors()
    {
        try
        {
            var sensors = HWiNFOReader.ReadLocal();
            MonitoredSensors[SensorElementType.GPUFrequency] = new();
            MonitoredSensors[SensorElementType.GPUFrequencyEffective] = new();
            MonitoredSensors[SensorElementType.CPUBusClock] = new();
            MonitoredSensors[SensorElementType.CPUCoreRatio] = new();
            MonitoredSensors[SensorElementType.CPUFrequencyEffective] = new();
            MonitoredSensors[SensorElementType.BatteryRemainingTime] = new();

            foreach (var sensor in sensors)
            {
                switch (sensor.LabelOrig)
                {
                    case "CPU (Tctl/Tdie)": MonitoredSensors[SensorElementType.CPUTemperature] = sensor; break;
                    case "GPU Temperature": MonitoredSensors[SensorElementType.GPUTemperature] = sensor; break;
                    case "CPU Package Power": MonitoredSensors[SensorElementType.CPUPower] = sensor; break;
                    case "GPU ASIC Power": MonitoredSensors[SensorElementType.GPUPower] = sensor; break;
                    case "GPU D3D Usage": MonitoredSensors[SensorElementType.GPUUsage] = sensor; break;
                    case "Max CPU/Thread Usage": MonitoredSensors[SensorElementType.CPUUsage] = sensor; break;
                    case "GPU Clock":
                        if (sensor.Value != MonitoredSensors[SensorElementType.GPUFrequency].Value)
                            GPUFrequencyChanged?.Invoke(sensor.Value);
                        MonitoredSensors[SensorElementType.GPUFrequency] = sensor;
                        break;
                    case "GPU Clock (Effective)": MonitoredSensors[SensorElementType.GPUFrequencyEffective] = sensor; break;
                    case "P-core 0 Ratio" or "P-core 1 Ratio" or "P-core 10 Ratio":
                    case "P-core 2 Ratio" or "P-core 11 Ratio" or "P-core 3 Ratio" or "P-core 12 Ratio":
                    case "P-core 4 Ratio" or "P-core 13 Ratio" or "P-core 5 Ratio" or "P-core 14 Ratio":
                    case "P-core 6 Ratio" or "P-core 15 Ratio" or "P-core 7 Ratio" or "P-core 16 Ratio":
                    case "P-core 8 Ratio" or "P-core 17 Ratio" or "P-core 9 Ratio" or "P-core 18 Ratio":
                    case "Core 0 Ratio" or "Core 1 Ratio" or "Core 10 Ratio":
                    case "Core 2 Ratio" or "Core 11 Ratio" or "Core 3 Ratio" or "Core 12 Ratio":
                    case "Core 4 Ratio" or "Core 13 Ratio" or "Core 5 Ratio" or "Core 14 Ratio":
                    case "Core 6 Ratio" or "Core 15 Ratio" or "Core 7 Ratio" or "Core 16 Ratio":
                    case "Core 8 Ratio" or "Core 17 Ratio" or "Core 9 Ratio" or "Core 18 Ratio":
                        if (sensor.Value > MonitoredSensors[SensorElementType.CPUCoreRatio].Value)
                            MonitoredSensors[SensorElementType.CPUCoreRatio] = sensor;
                        break;
                    case "Bus Clock": MonitoredSensors[SensorElementType.CPUBusClock] = sensor; break;
                    case "Core 0 Clock" or "Core 1 Clock" or "Core 10 Clock":
                    case "Core 2 Clock" or "Core 11 Clock" or "Core 3 Clock" or "Core 12 Clock":
                    case "Core 4 Clock" or "Core 13 Clock" or "Core 5 Clock" or "Core 14 Clock":
                    case "Core 6 Clock" or "Core 15 Clock" or "Core 7 Clock" or "Core 16 Clock":
                    case "Core 8 Clock" or "Core 17 Clock" or "Core 9 Clock" or "Core 18 Clock":
                        if (sensor.Value > MonitoredSensors[SensorElementType.CPUFrequency].Value)
                            MonitoredSensors[SensorElementType.CPUFrequency] = sensor;
                        break;
                    case "P-core 0 T0 Effective Clock" or "P-core 0 T1 Effective Clock":
                    case "P-core 1 T0 Effective Clock" or "P-core 1 T1 Effective Clock":
                    case "P-core 2 T0 Effective Clock" or "P-core 2 T1 Effective Clock":
                    case "P-core 3 T0 Effective Clock" or "P-core 3 T1 Effective Clock":
                    case "P-core 4 T0 Effective Clock" or "P-core 4 T1 Effective Clock":
                    case "P-core 5 T0 Effective Clock" or "P-core 5 T1 Effective Clock":
                    case "P-core 6 T0 Effective Clock" or "P-core 6 T1 Effective Clock":
                    case "P-core 7 T0 Effective Clock" or "P-core 7 T1 Effective Clock":
                    case "P-core 8 T0 Effective Clock" or "P-core 8 T1 Effective Clock":
                    case "P-core 9 T0 Effective Clock" or "P-core 9 T1 Effective Clock":
                    case "P-core 10 T0 Effective Clock" or "P-core 10 T1 Effective Clock":
                    case "P-core 11 T0 Effective Clock" or "P-core 11 T1 Effective Clock":
                    case "P-core 12 T0 Effective Clock" or "P-core 12 T1 Effective Clock":
                    case "P-core 13 T0 Effective Clock" or "P-core 13 T1 Effective Clock":
                    case "P-core 14 T0 Effective Clock" or "P-core 14 T1 Effective Clock":
                    case "P-core 15 T0 Effective Clock" or "P-core 15 T1 Effective Clock":
                    case "P-core 16 T0 Effective Clock" or "P-core 16 T1 Effective Clock":
                    case "P-core 17 T0 Effective Clock" or "P-core 17 T1 Effective Clock":
                    case "P-core 18 T0 Effective Clock" or "P-core 18 T1 Effective Clock":
                    case "Core 0 T0 Effective Clock" or "Core 0 T1 Effective Clock":
                    case "Core 1 T0 Effective Clock" or "Core 1 T1 Effective Clock":
                    case "Core 2 T0 Effective Clock" or "Core 2 T1 Effective Clock":
                    case "Core 3 T0 Effective Clock" or "Core 3 T1 Effective Clock":
                    case "Core 4 T0 Effective Clock" or "Core 4 T1 Effective Clock":
                    case "Core 5 T0 Effective Clock" or "Core 5 T1 Effective Clock":
                    case "Core 6 T0 Effective Clock" or "Core 6 T1 Effective Clock":
                    case "Core 7 T0 Effective Clock" or "Core 7 T1 Effective Clock":
                    case "Core 8 T0 Effective Clock" or "Core 8 T1 Effective Clock":
                    case "Core 9 T0 Effective Clock" or "Core 9 T1 Effective Clock":
                    case "Core 10 T0 Effective Clock" or "Core 10 T1 Effective Clock":
                    case "Core 11 T0 Effective Clock" or "Core 11 T1 Effective Clock":
                    case "Core 12 T0 Effective Clock" or "Core 12 T1 Effective Clock":
                    case "Core 13 T0 Effective Clock" or "Core 13 T1 Effective Clock":
                    case "Core 14 T0 Effective Clock" or "Core 14 T1 Effective Clock":
                    case "Core 15 T0 Effective Clock" or "Core 15 T1 Effective Clock":
                    case "Core 16 T0 Effective Clock" or "Core 16 T1 Effective Clock":
                    case "Core 17 T0 Effective Clock" or "Core 17 T1 Effective Clock":
                    case "Core 18 T0 Effective Clock" or "Core 18 T1 Effective Clock":
                        if (sensor.Value > MonitoredSensors[SensorElementType.CPUFrequencyEffective].Value)
                            MonitoredSensors[SensorElementType.CPUFrequencyEffective] = sensor;
                        break;
                    case "Remaining Capacity": MonitoredSensors[SensorElementType.BatteryRemainingCapacity] = sensor; break;
                    case "Charge Rate": MonitoredSensors[SensorElementType.BatteryChargeRate] = sensor; break;
                    case "Charge Level": MonitoredSensors[SensorElementType.BatteryChargeLevel] = sensor; break;
                    case "Estimated Remaining Time": MonitoredSensors[SensorElementType.BatteryRemainingTime] = sensor; break;
                    case "Virtual Memory Committed": MonitoredSensors[SensorElementType.VirtualMemoryUsage] = sensor; break;
                    case "Physical Memory Used": MonitoredSensors[SensorElementType.PhysicalMemoryUsage] = sensor; break;
                    case "GPU D3D Memory Dedicated": MonitoredSensors[SensorElementType.GPUMemoryUsage] = sensor; break;
                    case "PL1 Power Limit" or "PL1 Power Limit (Dynamic)":
                        {
                            var reading = (int)Math.Ceiling(sensor.Value);
                            if (reading != MonitoredSensors[SensorElementType.PL1].Value)
                                PowerLimitChanged?.Invoke(PowerType.Slow, reading);
                            MonitoredSensors[SensorElementType.PL1] = sensor;
                        }
                        break;
                    case "PL2 Power Limit" or "PL2 Power Limit (Dynamic)":
                        {
                            var reading = (int)Math.Ceiling(sensor.Value);
                            if (reading != MonitoredSensors[SensorElementType.PL2].Value)
                                PowerLimitChanged?.Invoke(PowerType.Fast, reading);

                            MonitoredSensors[SensorElementType.PL2] = sensor;
                        }
                        break;
                    case "CPU PPT SLOW Limit":
                        {
                            var reading = (int)Math.Floor(MonitoredSensors[SensorElementType.CPUPower].Value / sensor.Value * 100.0d);
                            if (reading != MonitoredSensors[SensorElementType.PL1].Value)
                                PowerLimitChanged?.Invoke(PowerType.Slow, reading);
                            MonitoredSensors[SensorElementType.PL1] = sensor;
                        }
                        break;
                    case "CPU PPT FAST Limit":
                        {
                            var reading = (int)Math.Floor(MonitoredSensors[SensorElementType.CPUPower].Value / sensor.Value * 100.0d);
                            if (reading != MonitoredSensors[SensorElementType.PL2].Value)
                                PowerLimitChanged?.Invoke(PowerType.Fast, reading);
                            MonitoredSensors[SensorElementType.PL2] = sensor;
                        }
                        break;
                    case "APU STAPM Limit":
                        {
                            var reading = (int)Math.Floor(MonitoredSensors[SensorElementType.CPUPower].Value / sensor.Value * 100.0d);
                            if (reading != MonitoredSensors[SensorElementType.PL3].Value)
                                PowerLimitChanged?.Invoke(PowerType.Stapm, reading);
                            MonitoredSensors[SensorElementType.PL3] = sensor;
                        }
                        break;
                }
                //LogManager.LogDebug($"{sensor.GroupLabelOrig} > {sensor.LabelOrig}:\t {sensor.Value}{sensor.Unit}\t {sensor.Type}");
            }
            //foreach (var sensor in MonitoredSensors)
            //    LogManager.LogDebug($"{sensor.Key} {sensor.Value.GroupLabelOrig} > {sensor.Value.LabelOrig}:\t {sensor.Value.Value}{sensor.Value.Unit}\t {sensor.Value.Type}");
        }
        catch
        {
            // raise event
            StopProcess();
            SetStatus(PlatformStatus.Stalled);
        }
    }

    private bool SetProperty(string propertyName, object value)
    {
        try
        {
            IniFile settings = new(SettingsPath);
            settings.Write(propertyName, Convert.ToString(value), "Settings");

            return true;
        }
        catch
        {
            return false;
        }
    }

    internal bool GetProperty(string propertyName)
    {
        try
        {
            IniFile settings = new(SettingsPath);
            string value = settings.Read(propertyName, "Settings");

            if (string.IsNullOrEmpty(value))
                return false;

            return Convert.ToBoolean(Convert.ToInt16(value));
        }
        catch
        {
            return false;
        }
    }

    public override bool StartProcess()
    {
        if (!IsInstalled)
            return false;

        if (IsRunning)
            KillProcess();

        // (re)set elements
        DisposeMemory();

        // Quiet startup
        SetProperty("OpenSystemSummary", 0);
        SetProperty("OpenSensors", 1);
        SetProperty("MinimalizeMainWnd", 1);
        SetProperty("MinimalizeSensors", 1);
        SetProperty("MinimalizeSensorsClose", 1);
        SetProperty("SensorInterval", 900);

        SetProperty("SensorsSM", 1); // Shared Memory Support [12-HOUR LIMIT]

        SetProperty("ShowWelcomeAndProgress", 0);
        SetProperty("SensorsOnly", 1);
        SetProperty("AutoUpdateBetaDisable", 1);
        SetProperty("AutoUpdate", 0);

        // stop watchdog
        PlatformWatchdog.Stop();

        return base.StartProcess();
    }

    public override bool StopProcess()
    {
        if (IsStarting)
            return false;
        KillProcess();

        return true;
    }

    public void ReaffirmRunningProcess()
    {
        // start HWiNFO if not running or Shared Memory is disabled
        var hasSensorsSM = GetProperty("SensorsSM");
        if (!IsRunning || !hasSensorsSM)
        {
            StopProcess();
            StartProcess();
        }

        PopulateSensors();
    }

    private void DisposeMemory()
    {
        //if (HWiNFOReader is not null)
        //    HWiNFOReader.Dispose();

        //if (MemoryMapped is not null)
        //{
        //    MemoryMapped.Dispose();
        //    MemoryMapped = null;
        //}

        //if (MemoryAccessor is not null)
        //{
        //    MemoryAccessor.Dispose();
        //    MemoryAccessor = null;
        //}

        //if (HWSensors is not null)
        //    HWSensors = null;

        //prevPoll_time = -1;
    }

    public override void Dispose()
    {
        DisposeMemory();
        HWiNFOReader?.Dispose();
        base.Dispose();
    }

    #region events

    public event LimitChangedHandler PowerLimitChanged;
    public delegate void LimitChangedHandler(PowerType type, int limit);

    public event GPUFrequencyChangedHandler GPUFrequencyChanged;
    public delegate void GPUFrequencyChangedHandler(double value);

    #endregion
}
