using HandheldCompanion.Devices;
using HandheldCompanion.Managers;
using HandheldCompanion.Shared;
using HandheldCompanion.Utils;
using HandheldCompanion.Views.Windows;
using LibreHardwareMonitor.Hardware;
using System;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Text.RegularExpressions;
using System.Timers;
using Windows.Media.Devices;
using YamlDotNet.Core.Tokens;

namespace HandheldCompanion.Platforms.Misc;

// Struct to hold hardware metrics snapshot
public struct HardwareMetrics
{
    public int HISTORY_POSITION = 0;
    public int CURRENT_CORE = 0;
    public static int MAX_CORES = MotherboardInfo.NumberOfCores;
    public float CpuBusClock { get; set; } = float.NaN;
    public float CpuCoreRatio { get; set; } = float.NaN;
    public float CpuLoad { get; set; } = float.NaN;
    public float CpuLoadMax { get; set; } = float.NaN;
    public float CpuPower { get; set; } = float.NaN;
    public float CpuClock { get; set; } = float.NaN;
    public float CpuClockEffective { get; set; } = float.NaN;
    public float CpuClockMax { get; set; } = float.NaN;

    public float[] FpsHistory { get; set; } = new float[MAX_CORES];
    public float[] CpuLoadCores { get; set; } = new float[MAX_CORES];
    public float[] CpuClockCores { get; set; } = new float[MAX_CORES];
    public float[] CpuRatioCores { get; set; } = new float[MAX_CORES];

    public float CpuTemp { get; set; } = float.NaN;
    public float MemUsed { get; set; } = float.NaN;
    public float MemAvailable { get; set; } = float.NaN;
    public float GpuLoad { get; set; } = float.NaN;
    public float GpuPower { get; set; } = float.NaN;
    public float GpuClock { get; set; } = float.NaN;
    public float GpuTemp { get; set; } = float.NaN;
    public float GpuMemDedicated { get; set; } = float.NaN;
    public float GpuMemShared { get; set; } = float.NaN;
    public float GpuMemDedicatedAvailable { get; set; } = float.NaN;
    public float GpuMemSharedAvailable { get; set; } = float.NaN;
    public float BattFullCapacity { get; set; } = float.NaN;
    public float BattDesignCapacity { get; set; } = float.NaN;
    public float BattRemainingCapacity { get; set; } = float.NaN;
    public float BattHealth { get; set; } = float.NaN;
    public float BattCapacity { get; set; } = float.NaN;
    public float BattPower { get; set; } = float.NaN;
    public float BattTime { get; set; } = float.NaN;
    public float FanSpeed { get; set; } = float.NaN;

    public float NetworkSpeedUp { get; set; } = float.NaN;
    public float NetworkSpeedDown { get; set; } = float.NaN;

    public HardwareMetrics()
    {
    }
}

public class LibreHardware : IPlatform
{
    private Computer computer;
    private NetworkInterface? networkInterface;

    private Timer sensorTimer;
    private int updateInterval = 1000;


    private const int GB_TO_MB = 1024;

    private float BatteryChargeLevel = float.NaN
        , BatteryDesignCapacity = float.NaN
        , BatteryFullCapacity = float.NaN
        , BatteryRemainingCapacity = float.NaN
        , BatteryPower = float.NaN
        , BatteryCapacity = float.NaN
        , BatteryHealth = float.NaN;
    private float BatteryTimeSpan = float.NaN;

    private readonly float ProcessorMaxClockSpeed = MotherboardInfo.ProcessorMaxClockSpeed;

    private HardwareMetrics currentMetrics;
    public LibreHardware()
    {
        Name = "LibreHardwareMonitor";
        IsInstalled = true;

        // watchdog to populate sensors
        sensorTimer = new Timer(updateInterval) { Enabled = false };
        sensorTimer.Elapsed += sensorTimer_Elapsed;

        // prepare for sensors reading
        computer = new Computer
        {
            IsNetworkEnabled = IDevice.GetCurrent().NetworkMonitor,
            IsCpuEnabled = IDevice.GetCurrent().CpuMonitor,
            IsGpuEnabled = IDevice.GetCurrent().GpuMonitor,
            IsMemoryEnabled = IDevice.GetCurrent().MemoryMonitor,
            IsBatteryEnabled = IDevice.GetCurrent().BatteryMonitor,
        };
        currentMetrics = new HardwareMetrics();
    }

    private void SettingsManager_SettingValueChanged(string name, object value, bool temporary)
    {
        switch (name)
        {
            case "OnScreenDisplayRefreshRate":
                updateInterval = Convert.ToInt32(value);
                sensorTimer.Interval = updateInterval;
                break;
        }
    }

    public override bool Start()
    {
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

        networkInterface = DeviceUtils.GetPrimaryNetworkInterface();

        if (computer is not null)
        {
            // open computer, slow task
            computer.Open();
            // prevent sensor from being stored to memory for too long
            foreach (var hardware in computer.Hardware)
                foreach (var sensor in hardware.Sensors)
                    sensor.ValuesTimeWindow = TimeSpan.FromMilliseconds(updateInterval);
        }

        sensorTimer?.Start();
        return base.Start();
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
        SettingsManager_SettingValueChanged("OnScreenDisplayRefreshRate", ManagerFactory.settingsManager.Get<double>("OnScreenDisplayRefreshRate"), false);
    }

    public override bool Stop(bool kill = false)
    {
        ManagerFactory.settingsManager.SettingValueChanged -= SettingsManager_SettingValueChanged;
        ManagerFactory.settingsManager.Initialized -= SettingsManager_Initialized;

        sensorTimer?.Stop();

        // wait until all tasks are complete
        computer?.Close();

        return base.Stop(kill);
    }

    private void Reset()
    {
        computer?.Reset();

        // prepare for sensors reading
        computer = new Computer
        {
            IsNetworkEnabled = IDevice.GetCurrent().NetworkMonitor,
            IsCpuEnabled = IDevice.GetCurrent().CpuMonitor,
            IsGpuEnabled = IDevice.GetCurrent().GpuMonitor,
            IsMemoryEnabled = IDevice.GetCurrent().MemoryMonitor,
            IsBatteryEnabled = IDevice.GetCurrent().BatteryMonitor,
        };


        // prevent sensor from being stored to memory for too long
        foreach (var hardware in computer.Hardware)
            foreach (var sensor in hardware.Sensors)
                sensor.ValuesTimeWindow = TimeSpan.FromMilliseconds(updateInterval);

        currentMetrics = new HardwareMetrics();
    }

    long lastFanRefresh;
    long lastBatteryRefresh;

    private void sensorTimer_Elapsed(object? sender, ElapsedEventArgs e)
    {
        lock (updateLock)
        {
            //Refresh again only after 15 Minutes since the last refresh
            if (lastBatteryRefresh == 0 || Math.Abs(DateTimeOffset.Now.ToUnixTimeMilliseconds() - lastBatteryRefresh) > 5_000)
            {
                lastBatteryRefresh = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                ReadBatterySensors();
                RefreshBatteryHealth();
            }

            if (Math.Abs(DateTimeOffset.Now.ToUnixTimeMilliseconds() - lastFanRefresh) > 5_000)
            {
                lastFanRefresh = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                HandleFan();
            }

            // pull temperature sensor
            foreach (var hardware in computer.Hardware)
            {
                try { hardware.Update(); } catch { }
                switch (hardware.HardwareType)
                {
                    case HardwareType.Cpu:
                        HandleCPU(hardware);
                        break;
                    case HardwareType.GpuNvidia:
                    case HardwareType.GpuAmd:
                    case HardwareType.GpuIntel:
                        HandleGPU(hardware);
                        break;
                    case HardwareType.Network when networkInterface != null && hardware.Name.Equals(networkInterface.Name, StringComparison.OrdinalIgnoreCase):
                        HandleNetwork(hardware);
                        break;
                    case HardwareType.Memory when hardware.Name.Equals("Total Memory", StringComparison.OrdinalIgnoreCase):
                        HandleMemory(hardware);
                        break;
                    default: continue;

                }
            }

            HardwareMetricsChanged?.Invoke(currentMetrics);

        }
    }

    private void HandleFan()
    {
        currentMetrics.FanSpeed = IDevice.GetCurrent().ReadFanSpeed();
    }

    public float GetNetworkSpeedDown() => computer?.IsNetworkEnabled ?? false ? currentMetrics.NetworkSpeedDown : float.NaN;
    public float GetNetworkSpeedUp() => computer?.IsNetworkEnabled ?? false ? currentMetrics.NetworkSpeedUp : float.NaN;
    private void HandleNetwork(IHardware network)
    {
        foreach (var sensor in network.Sensors)
        {
            // May crash the app when Value is null, better to check first
            if (sensor.Value is null)
                continue;
            switch (sensor.SensorType)
            {
                case SensorType.Throughput:
                    HandleNetwork_Throughput(sensor);
                    break;
                default: continue;
            }
        }
    }

    private void HandleNetwork_Throughput(ISensor sensor)
    {
        var value = sensor.Value.GetValueOrDefault();
        switch (sensor.Name)
        {
            case "Upload Speed":
                currentMetrics.NetworkSpeedUp = value;
                NetworkSpeedUpChanged?.Invoke(value);
                break;
            case "Download Speed":
                currentMetrics.NetworkSpeedDown = value;
                NetworkSpeedDownChanged?.Invoke(value);
                break;
        }
    }

    #region GPU updates
    public float GetGPULoad() => computer?.IsGpuEnabled ?? false ? currentMetrics.GpuLoad : float.NaN;
    public float GetGPUPower() => computer?.IsGpuEnabled ?? false ? currentMetrics.GpuPower : float.NaN;
    public float GetGPUTemperature() => computer?.IsGpuEnabled ?? false ? currentMetrics.GpuTemp : float.NaN;
    public float GetGPUClock() => computer?.IsGpuEnabled ?? false ? currentMetrics.GpuClock : float.NaN;

    public float GetGPUMemory() => computer?.IsGpuEnabled ?? false ? currentMetrics.GpuMemDedicated : float.NaN;
    public float GetGPUMemoryDedicated() => computer?.IsGpuEnabled ?? false ? currentMetrics.GpuMemDedicated : float.NaN;
    public float GetGPUMemoryShared() => computer?.IsGpuEnabled ?? false ? currentMetrics.GpuMemShared : float.NaN;

    private void HandleGPU(IHardware gpu)
    {
        foreach (var sensor in gpu.Sensors)
        {
            // May crash the app when Value is null, better to check first
            if (sensor.Value is null)
                continue;

            switch (sensor.SensorType)
            {
                case SensorType.Load:
                    HandleGPU_Load(sensor);
                    break;
                case SensorType.Clock:
                    HandleGPU_Clock(sensor);
                    break;
                case SensorType.Power:
                    HandleGPU_Power(sensor);
                    break;
                case SensorType.Temperature:
                    HandleGPU_Temp(sensor);
                    break;
                case SensorType.Data:
                case SensorType.SmallData:
                    HandleGPU_Data(sensor);
                    break;
            }
        }
    }

    private void HandleGPU_Data(ISensor sensor)
    {
        var value = sensor.Value.GetValueOrDefault();
        switch (sensor.Name)
        {
            case "D3D Dedicated Memory Used":
                currentMetrics.GpuMemDedicated = value;
                GPUMemoryDedicatedChanged?.Invoke(value);
                break;
            case "D3D Dedicated Memory Free":
                currentMetrics.GpuMemDedicatedAvailable = value;
                break;
            case "D3D Shared Memory Used":
                currentMetrics.GpuMemShared = value;
                GPUMemorySharedChanged?.Invoke(value);
                break;
            case "D3D Shared Memory Free":
                currentMetrics.GpuMemSharedAvailable = value;
                break;

        }
    }

    private void HandleGPU_Load(ISensor sensor)
    {
        var value = sensor.Value.GetValueOrDefault();
        switch (sensor.Name)
        {
            case "D3D 3D":
                currentMetrics.GpuLoad = value;
                GPULoadChanged?.Invoke(value);
                break;
        }
    }

    private void HandleGPU_Clock(ISensor sensor)
    {
        var value = sensor.Value.GetValueOrDefault();
        switch (sensor.Name)
        {
            case "GPU Core":
                if (value != currentMetrics.GpuClock)
                {
                    currentMetrics.GpuClock = value;
                    GPUClockChanged?.Invoke(value);
                }
                break;
        }
    }

    private void HandleGPU_Power(ISensor sensor)
    {
        var value = sensor.Value.GetValueOrDefault();
        switch (sensor.Name)
        {
            case "GPU Core":
                currentMetrics.GpuPower = value;
                GPUPowerChanged?.Invoke(value);
                break;
        }
    }

    private void HandleGPU_Temp(ISensor sensor)
    {
        var value = sensor.Value.GetValueOrDefault();
        switch (sensor.Name)
        {
            case "GPU VR SoC":
                currentMetrics.GpuTemp = value;
                GPUTemperatureChanged?.Invoke(value);
                break;
        }
    }

    #endregion

    #region CPU updates

    public float GetCPULoad() => computer?.IsCpuEnabled ?? false ? currentMetrics.CpuLoad : float.NaN;
    public float GetCPUPower() => computer?.IsCpuEnabled ?? false ? currentMetrics.CpuPower : float.NaN;
    public float GetCPUTemperature() => computer?.IsCpuEnabled ?? false ? currentMetrics.CpuTemp : float.NaN;
    public float GetCPUClock() => computer?.IsCpuEnabled ?? false ? currentMetrics.CpuClock : float.NaN;

    private void HandleCPU(IHardware cpu)
    {
        foreach (var sensor in cpu.Sensors)
        {
            // May crash the app when Value is null, better to check first
            if (sensor.Value is null)
                continue;

            switch (sensor.SensorType)
            {
                case SensorType.Factor:
                    HandleCPU_Factor(sensor);
                    break;
                case SensorType.Load:
                    //HandleCPU_Load(sensor);
                    break;
                case SensorType.Clock:
                    HandleCPU_Clock(sensor);
                    break;
                case SensorType.Power:
                    HandleCPU_Power(sensor);
                    break;
                case SensorType.Temperature:
                    try
                    {
                        using var ct = new PerformanceCounter("Thermal Zone Information", "Temperature", @"\_TZ.TZ01", true);
                        currentMetrics.CpuTemp = ct.NextValue() - 273.15f;
                    }
                    catch
                    {
                        HandleCPU_Temp(sensor);
                    }

                    break;
            }
        }

        //HandleCPU_Load();
    }

    private void HandleCPU_Factor(ISensor sensor)
    {
        var value = sensor.Value.GetValueOrDefault();
        switch (sensor.Name)
        {
            default:
                if (sensor.Name.StartsWith("Core #", StringComparison.OrdinalIgnoreCase) ||
                    sensor.Name.StartsWith("CPU Core #", StringComparison.OrdinalIgnoreCase))
                {
                    // Extract core number using regex
                    var match = Regex.Match(sensor.Name, @"Core #(\d+)", RegexOptions.IgnoreCase);
                    if (match.Success && int.TryParse(match.Groups[1].Value, out int coreNumber) && coreNumber <= HardwareMetrics.MAX_CORES)
                    {
                        currentMetrics.CpuRatioCores[coreNumber - 1] = value;
                    }
                }
                break;
        }
    }

    //private void HandleCPU_Load()
    //{
    //    currentMetrics.CpuClock = currentMetrics.CpuRatioCores.Average(v => v * currentMetrics.CpuBusClock);
    //    currentMetrics.CpuClockMax = currentMetrics.CpuRatioCores.Max(v => v * currentMetrics.CpuBusClock);
    //    currentMetrics.CpuLoad = currentMetrics.CpuClockEffective * 100 / ProcessorMaxClockSpeed;
    //    currentMetrics.CpuLoadMax = currentMetrics.CpuLoadCores.Max();
    //}

    private void HandleCPU_Clock(ISensor sensor)
    {
        var value = sensor.Value.GetValueOrDefault();
        switch (sensor.Name)
        {
            case "Bus Speed":
                currentMetrics.CpuBusClock = value;
                break;
            case "Cores (Average Effective)":
                var avgCpuLoad = value * 100 / ProcessorMaxClockSpeed;
                currentMetrics.CpuLoad = avgCpuLoad;
                CPULoadChanged?.Invoke(avgCpuLoad);
                break;
            case "Cores (Max Effective)":
                var maxCpuLoad = value * 100 / ProcessorMaxClockSpeed;
                currentMetrics.CpuLoadMax = maxCpuLoad;
                CPULoadChanged?.Invoke(maxCpuLoad);
                break;
            case "Cores (Average)":
                //currentMetrics.CpuClockEffective = currentMetrics.CpuClock = value;
                currentMetrics.CpuClock = value;
                CPUClockChanged?.Invoke(value);
                break;
            case "Cores (Max)":
                currentMetrics.CpuClockMax = value;
                CPUClockChanged?.Invoke(value);
                break;
            default:
                if ((
                        sensor.Name.StartsWith("Core #", StringComparison.OrdinalIgnoreCase) ||
                        sensor.Name.StartsWith("CPU Core #", StringComparison.OrdinalIgnoreCase)
                    ) &&
                    sensor.Name.EndsWith("(Effective)", StringComparison.OrdinalIgnoreCase))
                {
                    // Extract core number using regex
                    var match = Regex.Match(sensor.Name, @"Core #(\d+)", RegexOptions.IgnoreCase);
                    if (match.Success && int.TryParse(match.Groups[1].Value, out int coreNumber) && coreNumber <= HardwareMetrics.MAX_CORES)
                    {
                        currentMetrics.CpuLoadCores[coreNumber - 1] = value * 100 / ProcessorMaxClockSpeed;
                        currentMetrics.CpuClockCores[coreNumber - 1] = value;
                    }

                }
                break;
        }
    }

    private void HandleCPU_Power(ISensor sensor)
    {
        var value = sensor.Value.GetValueOrDefault();
        switch (sensor.Name)
        {
            case "Package":
            case "CPU Package":
                currentMetrics.CpuPower = value;
                CPUPowerChanged?.Invoke(value);
                break;
        }
    }

    private void HandleCPU_Temp(ISensor sensor)
    {
        var value = sensor.Value.GetValueOrDefault();
        switch (sensor.Name)
        {
            case "CPU Package":
            case "Core (Tctl/Tdie)":
                currentMetrics.CpuTemp = value;
                CPUTemperatureChanged?.Invoke(value);
                if (value == 0f) Reset();
                break;
        }

    }

    #endregion

    #region Memory updates
    public float GetMemoryUsage() => computer?.IsMemoryEnabled ?? false ? currentMetrics.MemUsed : float.NaN;
    public float GetMemoryAvailable() => computer?.IsMemoryEnabled ?? false ? currentMetrics.MemAvailable : float.NaN;

    private void HandleMemory(IHardware cpu)
    {
        foreach (var sensor in cpu.Sensors)
        {
            if (sensor.Value is null)
                continue;

            switch (sensor.SensorType)
            {
                case SensorType.Data:
                    HandleMemory_Data(sensor);
                    break;
            }
        }

    }

    private void HandleMemory_Data(ISensor sensor)
    {
        var value = sensor.Value.GetValueOrDefault();
        switch (sensor.Name)
        {
            case "Memory Used":
                currentMetrics.MemUsed = value * GB_TO_MB;
                MemoryUsageChanged?.Invoke(value);
                break;
            case "Memory Available":
                currentMetrics.MemAvailable = value * GB_TO_MB;
                MemoryAvailableChanged?.Invoke(value);
                break;
        }
    }

    #endregion

    #region Battery updates
    public float GetBatteryLevel() => computer?.IsBatteryEnabled ?? false ? BatteryCapacity : float.NaN;
    public float GetBatteryPower() => computer?.IsBatteryEnabled ?? false ? BatteryPower : float.NaN;
    public TimeSpan GetBatteryTimeSpan() => computer?.IsBatteryEnabled ?? false ? TimeSpan.FromMinutes(BatteryTimeSpan) : TimeSpan.Zero;
    public float GetBatteryHealth() => computer?.IsBatteryEnabled ?? false ? BatteryHealth : float.NaN;
    public float GetBatteryRemainingCapacity() => computer?.IsBatteryEnabled ?? false ? BatteryRemainingCapacity : float.NaN;

    private void GetBatteryStatus()
    {
        try
        {
            currentMetrics.BattRemainingCapacity = float.NaN;
            currentMetrics.BattPower = float.NaN;

            var scope = new ManagementScope("root\\WMI");
            var query = new ObjectQuery("SELECT * FROM BatteryStatus");
            using var searcher = new ManagementObjectSearcher(scope, query);
            foreach (var obj in searcher.Get().OfType<ManagementObject>())
            {
                currentMetrics.BattRemainingCapacity = Convert.ToSingle(obj["RemainingCapacity"]);
                var chargeRate = Convert.ToSingle(obj["ChargeRate"]);
                var dischargeRate = Convert.ToSingle(obj["DischargeRate"]);
                var value = float.NaN;
                if (chargeRate != 0f || dischargeRate != 0f)
                {
                    value = chargeRate > 0f
                        ? chargeRate / 1000f
                        : dischargeRate > 0f
                        ? -dischargeRate / 1000f
                        : float.NaN;
                }
                currentMetrics.BattPower = value;
                BatteryPowerChanged?.Invoke(value);
            }

        }
        catch (Exception ex)
        {
            LogManager.LogError("Discharge Reading: " + ex.Message);
        }
    }

    private void ReadFullChargeCapacity()
    {
        try
        {
            currentMetrics.BattFullCapacity = float.NaN;
            var scope = new ManagementScope("root\\WMI");
            var query = new ObjectQuery("SELECT * FROM BatteryFullChargedCapacity");

            using var searcher = new ManagementObjectSearcher(scope, query);
            foreach (var obj in searcher.Get().OfType<ManagementObject>())
            {
                currentMetrics.BattFullCapacity = Convert.ToSingle(obj["FullChargedCapacity"]);
            }

        }
        catch (Exception ex)
        {
            LogManager.LogError("Full Charge Reading: " + ex.Message);
        }

    }

    private void ReadDesignCapacity()
    {
        try
        {
            currentMetrics.BattDesignCapacity = float.NaN;
            var scope = new ManagementScope("root\\WMI");
            var query = new ObjectQuery("SELECT * FROM BatteryStaticData");

            using var searcher = new ManagementObjectSearcher(scope, query);
            foreach (var obj in searcher.Get().OfType<ManagementObject>())
            {
                currentMetrics.BattDesignCapacity = Convert.ToSingle(obj["DesignedCapacity"]);
            }

        }
        catch (Exception ex)
        {
            LogManager.LogError("Design Capacity Reading: " + ex.Message);
        }
    }

    private void RefreshBatteryHealth()
    {
        currentMetrics.BattHealth = GetBatteryHealthInternal() * 100f;
    }

    private float GetBatteryHealthInternal()
    {
        ReadDesignCapacity();
        ReadFullChargeCapacity();

        if (float.IsNaN(currentMetrics.BattDesignCapacity) || float.IsNaN(currentMetrics.BattFullCapacity))
        {
            return float.NaN;
        }

        var health = (float)currentMetrics.BattFullCapacity / (float)currentMetrics.BattDesignCapacity;
        LogManager.LogInformation($"Design Capacity: {BatteryDesignCapacity}mWh,  Remaining Capacity: {BatteryRemainingCapacity}mWh,  Full Charge Capacity: {BatteryFullCapacity}mWh,  Health: {health}%");

        return health;
    }

    private void ReadBatterySensors()
    {
        ReadFullChargeCapacity();
        GetBatteryStatus();

        if (float.IsNaN(currentMetrics.BattFullCapacity) || float.IsNaN(currentMetrics.BattRemainingCapacity))
            return;

        var currentCapacity = (float)Math.Min(100, (decimal)currentMetrics.BattRemainingCapacity / (decimal)currentMetrics.BattFullCapacity * 100);

        if (!float.IsNaN(currentMetrics.BattCapacity) && currentMetrics.BattCapacity != 100f && currentCapacity == 100f)
            ToastManager.RunToast(
                Properties.Resources.BatteryFullyCharged, ToastIcons.BatteryFull
                );

        currentMetrics.BattCapacity = currentCapacity;
        BatteryLevelChanged?.Invoke(currentCapacity);

        var value = float.NaN;
        if (!float.IsNaN(currentMetrics.BattPower) && currentMetrics.BattPower != 0f)
        {
            value = currentMetrics.BattPower > 0
                ? (currentMetrics.BattFullCapacity / 1000 - currentMetrics.BattRemainingCapacity / 1000) / currentMetrics.BattPower * 60f
                : (currentMetrics.BattRemainingCapacity / 1000 / currentMetrics.BattPower) * 60f * -1;
        }
        currentMetrics.BattTime = value;
        BatteryTimeSpanChanged?.Invoke(value);
    }


    private void HandleBattery(IHardware cpu)
    {
        foreach (var sensor in cpu.Sensors)
        {
            switch (sensor.SensorType)
            {
                case SensorType.Level:
                    HandleBattery_Level(sensor);
                    break;
                case SensorType.Power:
                    HandleBattery_Power(sensor);
                    break;
                case SensorType.TimeSpan:
                    HandleBattery_TimeSpan(sensor);
                    break;
                case SensorType.Energy:
                    HandleBattery_Energy(sensor);
                    break;
            }
        }

        if (BatteryFullCapacity > 0 && BatteryRemainingCapacity > 0)
        {
            var currentBatteryCapacity = (float)Math.Min(100, (decimal)BatteryRemainingCapacity / (decimal)BatteryFullCapacity * 100);

            if (BatteryCapacity != -1f && BatteryCapacity != 100f && currentBatteryCapacity == 100f)
                ToastManager.RunToast(
                    Properties.Resources.BatteryFullyCharged, ToastIcons.BatteryFull
                    );

            BatteryCapacity = currentBatteryCapacity;
            BatteryLevelChanged?.Invoke(BatteryCapacity);

            if (BatteryPower != 0)
            {
                if (BatteryPower > 0)
                    BatteryTimeSpan = (BatteryFullCapacity / 1000 - BatteryRemainingCapacity / 1000) / BatteryPower * 60f;
                else
                    BatteryTimeSpan = (BatteryRemainingCapacity / 1000 / BatteryPower) * 60f * -1;
                BatteryTimeSpanChanged?.Invoke(BatteryTimeSpan);
            }
        }

        //LogManager.LogDebug($"Charge: {BatteryPower}W, RemainingCapacity: {BatteryRemainingCapacity}mWh, BatteryFullCapacity: {BatteryFullCapacity}mWh, BatterySpan: {BatteryTimeSpan}mins");
    }

    private void HandleBattery_Level(ISensor sensor)
    {
        switch (sensor.Name)
        {
            case "Charge Level":
                BatteryChargeLevel = sensor.Value.GetValueOrDefault();
                BatteryLevelChanged?.Invoke(BatteryChargeLevel);
                break;
        }
    }

    private void HandleBattery_Power(ISensor sensor)
    {
        switch (sensor.Name)
        {
            case "Discharge Rate":
                BatteryPower = -sensor.Value.GetValueOrDefault();
                BatteryPowerChanged?.Invoke(BatteryPower);
                break;
            case "Charge Rate":
                BatteryPower = sensor.Value.GetValueOrDefault();
                BatteryPowerChanged?.Invoke(BatteryPower);
                break;
        }
    }

    private void HandleBattery_TimeSpan(ISensor sensor)
    {
        switch (sensor.Name)
        {
            case "Remaining Time (Estimated)":
                BatteryTimeSpan = sensor.Value.GetValueOrDefault();
                BatteryTimeSpanChanged?.Invoke(BatteryTimeSpan);
                break;
        }
    }

    private void HandleBattery_Energy(ISensor sensor)
    {
        switch (sensor.Name)
        {
            case "Designed Capacity":
                BatteryDesignCapacity = sensor.Value.GetValueOrDefault();
                break;
            case "Full Charged Capacity":
                BatteryFullCapacity = sensor.Value.GetValueOrDefault();
                break;
            case "Remaining Capacity":
                BatteryRemainingCapacity = sensor.Value.GetValueOrDefault();
                break;
        }
    }

    #endregion

    #region Events

    public delegate void ChangedHandler(float? value);
    public delegate void HardwareMetricsEventHandler(HardwareMetrics metrics);
    public delegate void NetworkSpeedEventHandler(float speedDown, float speedUp);
    public delegate void CPUPerformanceEventHandler(float cpuLoad, float cpuLoadMax, float cpuPower, float cpuClock, float cpuClockMax, float cpuTemp);
    public delegate void GPUPerformanceEventHandler(float gpuLoad, float gpuPower, float gpuClock, float gpuTemp);

    public delegate void RAMUsageEventHandler(float memUsage, float memAvail);
    public delegate void VRAMUsageEventHandler(float memUsage, float memAvail);
    public delegate void FanSpeedEventHandler(float fanDuty, float fanSpeed);
    public delegate void BatteryEventHandler(float battLevel, float battPower, float battTime);

    public event HardwareMetricsEventHandler HardwareMetricsChanged;

    public event ChangedHandler CPULoadChanged;
    public event ChangedHandler CPUPowerChanged;
    public event ChangedHandler CPUClockChanged;
    public event ChangedHandler CPUTemperatureChanged;
    public event CPUPerformanceEventHandler CPUPerformanceChanged;

    public event ChangedHandler GPULoadChanged;
    public event ChangedHandler GPUPowerChanged;
    public event ChangedHandler GPUClockChanged;
    public event ChangedHandler GPUTemperatureChanged;
    public event GPUPerformanceEventHandler GPUPerformanceChanged;

    public event ChangedHandler GPUMemoryChanged;
    public event ChangedHandler GPUMemoryDedicatedChanged;
    public event ChangedHandler GPUMemorySharedChanged;
    public event VRAMUsageEventHandler VRAMUsageChanged;

    public event ChangedHandler MemoryUsageChanged;
    public event ChangedHandler MemoryAvailableChanged;
    public event RAMUsageEventHandler RAMUsageChanged;

    public event ChangedHandler NetworkSpeedDownChanged;
    public event ChangedHandler NetworkSpeedUpChanged;
    public event NetworkSpeedEventHandler NetworkSpeedChanged;

    public event FanSpeedEventHandler FanSpeedChanged;

    public event ChangedHandler BatteryLevelChanged;
    public event ChangedHandler BatteryPowerChanged;
    public event ChangedHandler BatteryTimeSpanChanged;
    public event BatteryEventHandler BatteryStatusChanged;
    #endregion
}