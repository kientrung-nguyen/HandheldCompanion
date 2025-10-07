using HandheldCompanion.Devices;
using HandheldCompanion.GraphicsProcessingUnit;
using HandheldCompanion.Managers;
using HandheldCompanion.Shared;
using HandheldCompanion.Utils;
using HandheldCompanion.Views.Windows;
using LibreHardwareMonitor.Hardware;
using System;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Timers;

namespace HandheldCompanion.Platforms.Misc;

public class LibreHardware : IPlatform
{
    private Computer computer;

    private Timer sensorTimer;
    private int updateInterval = 1000;

    private float CPULoad = float.NaN
        , CPUClock = float.NaN
        , CPUPower = float.NaN
        , CPUTemp = float.NaN;

    public float CPUFanSpeed = float.NaN
        , CPUFanDuty = float.NaN;

    private float GPULoad = float.NaN
        , GPUClock = float.NaN
        , GPUPower = float.NaN
        , GPUTemp = float.NaN;

    private float GPUMemory = float.NaN
        , GPUMemoryTotal = float.NaN
        , GPUMemoryShared = float.NaN
        , GPUMemorySharedTotal = float.NaN;

    private float GPUMemoryDedicated = float.NaN
        , GPUMemoryDedicatedTotal = float.NaN;

    private float MemoryUsage = float.NaN
        , MemoryAvailable = float.NaN;

    private float NetworkSpeedDown = float.NaN
        , NetworkSpeedUp = float.NaN;

    private float BatteryChargeLevel = float.NaN
        , BatteryDesignCapacity = float.NaN
        , BatteryFullCapacity = float.NaN
        , BatteryRemainingCapacity = float.NaN
        , BatteryPower = float.NaN
        , BatteryCapacity = float.NaN
        , BatteryHealth = float.NaN;
    private float BatteryTimeSpan = float.NaN;

    long lastFanRefresh;
    long lastBatteryRefresh;


    public LibreHardware()
    {
        PlatformType = PlatformType.LibreHardwareMonitor;
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

        if (computer is not null)
        {
            // open computer, slow task
            computer.Open();
            // prevent sensor from being stored to memory for too long
            foreach (var hardware in computer.Hardware)
                foreach (var sensor in hardware.Sensors)
                    sensor.ValuesTimeWindow = new(0, 0, 10);
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
        lock (updateLock)
        {
            computer?.Close();
        }

        return base.Stop(kill);
    }

    private void sensorTimer_Elapsed(object? sender, ElapsedEventArgs e)
    {
        lock (updateLock)
        {
            //Refresh again only after 15 Minutes since the last refresh

            if (lastBatteryRefresh == 0 || Math.Abs(DateTimeOffset.Now.ToUnixTimeMilliseconds() - lastBatteryRefresh) > 10 * 60_000)
            {
                lastBatteryRefresh = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                RefreshBatteryHealth();
            }

            if (Math.Abs(DateTimeOffset.Now.ToUnixTimeMilliseconds() - lastFanRefresh) > 5000)
            {
                lastFanRefresh = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            }

            //if (Math.Abs(DateTimeOffset.Now.ToUnixTimeMilliseconds() - lastGPURefresh) > 500)
            //{
            //    lastGPURefresh = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            //    HandleGPU(GPUManager.GetCurrent());
            //}

            var primaryInterface = DeviceUtils.GetPrimaryNetworkInterface();
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
                    case HardwareType.Network when hardware.Name.Equals(primaryInterface.Name, StringComparison.OrdinalIgnoreCase):
                        HandleNetwork(hardware);
                        break;
                    case HardwareType.Memory when hardware.Name.Equals("Total Memory", StringComparison.OrdinalIgnoreCase):
                        HandleMemory(hardware);
                        break;
                    case HardwareType.Battery:
                        //HandleBattery(hardware);
                        ReadBatterySensors();
                        break;
                    default: continue;

                }
            }

            HandleFan();

            if (computer?.IsCpuEnabled ?? false)
                CPUPerformanceChanged?.Invoke(
                    cpuLoad: CPULoad,
                    cpuPower: CPUPower,
                    cpuClock: CPUClock,
                    cpuTemp: CPUTemp);
            if (computer?.IsMemoryEnabled ?? false)
                RAMUsageChanged?.Invoke(
                    memUsage: MemoryUsage,
                    memAvail: MemoryAvailable);

            if (computer?.IsGpuEnabled ?? false)
            {
                GPUPerformanceChanged?.Invoke(
                    gpuLoad: GPULoad,
                    gpuPower: GPUPower,
                    gpuClock: GPUClock,
                    gpuTemp: GPUTemp);
                VRAMUsageChanged?.Invoke(
                    memUsage: GPUMemoryDedicated,
                    memAvail: GPUMemoryDedicatedTotal - GPUMemoryDedicated);
            }

            if (computer?.IsBatteryEnabled ?? false)
                BatteryStatusChanged?.Invoke(
                    battLevel: BatteryChargeLevel,
                    battPower: BatteryPower,
                    battTime: BatteryTimeSpan);

            if (computer?.IsNetworkEnabled ?? false)
                NetworkSpeedChanged?.Invoke(
                    speedDown: NetworkSpeedDown,
                    speedUp: NetworkSpeedUp);
        }
    }

    private void HandleFan()
    {
        CPUFanSpeed = IDevice.GetCurrent().ReadFanSpeed();
        CPUFanDuty = IDevice.GetCurrent().ReadFanDuty();
    }

    public float GetNetworkSpeedDown() => computer?.IsNetworkEnabled ?? false ? NetworkSpeedDown : float.NaN;
    public float GetNetworkSpeedUp() => computer?.IsNetworkEnabled ?? false ? NetworkSpeedUp : float.NaN;
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
        switch (sensor.Name)
        {
            case "Upload Speed":
                NetworkSpeedUp = sensor.Value.GetValueOrDefault();
                NetworkSpeedUpChanged?.Invoke(NetworkSpeedUp);
                break;
            case "Download Speed":
                NetworkSpeedDown = sensor.Value.GetValueOrDefault();
                NetworkSpeedDownChanged?.Invoke(NetworkSpeedDown);
                break;
        }
    }

    #region GPU updates
    public float GetGPULoad() => computer?.IsGpuEnabled ?? false ? GPULoad : float.NaN;
    public float GetGPUPower() => computer?.IsGpuEnabled ?? false ? GPUPower : float.NaN;
    public float GetGPUTemperature() => computer?.IsGpuEnabled ?? false ? GPUTemp : float.NaN;
    public float GetGPUClock() => computer?.IsGpuEnabled ?? false ? GPUClock : float.NaN;

    public float GetGPUMemory() => computer?.IsGpuEnabled ?? false ? GPUMemory : float.NaN;
    public float GetGPUMemoryDedicated() => computer?.IsGpuEnabled ?? false ? GPUMemoryDedicated : float.NaN;
    public float GetGPUMemoryShared() => computer?.IsGpuEnabled ?? false ? GPUMemoryShared : float.NaN;

    public float GetGPUMemoryTotal() => computer?.IsGpuEnabled ?? false ? GPUMemoryTotal : float.NaN;
    public float GetGPUMemoryDedicatedTotal() => computer?.IsGpuEnabled ?? false ? GPUMemoryDedicatedTotal : float.NaN;
    public float GetGPUMemorySharedTotal() => computer?.IsGpuEnabled ?? false ? GPUMemorySharedTotal : float.NaN;

    private void HandleGPU(GPU? gpu)
    {
        if (gpu is null || !gpu.IsInitialized)
            return;

        if (gpu.HasLoad())
            GPULoad = gpu.GetLoad();
        if (gpu.HasClock())
            GPUClock = gpu.GetClock();
        if (gpu.HasPower())
            GPUPower = gpu.GetPower();
        if (gpu.HasTemperature())
            GPUTemp = gpu.GetTemperature();
        if (gpu.HasVRAMUsage())
            GPUMemory = gpu.GetVRAMUsage();
    }

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
        switch (sensor.Name)
        {
            case "GPU Memory Used":
                GPUMemory = sensor.Value.GetValueOrDefault();
                GPUMemoryChanged?.Invoke(GPUMemory);
                break;
            case "GPU Memory Total":
                GPUMemoryTotal = sensor.Value.GetValueOrDefault();
                break;
            case "D3D Dedicated Memory Used":
                GPUMemoryDedicated = sensor.Value.GetValueOrDefault();
                GPUMemoryDedicatedChanged?.Invoke(GPUMemoryDedicated);
                break;
            case "D3D Dedicated Memory Total":
                GPUMemoryDedicatedTotal = sensor.Value.GetValueOrDefault();
                break;
            case "D3D Shared Memory Used":
                GPUMemoryShared = sensor.Value.GetValueOrDefault();
                GPUMemorySharedChanged?.Invoke(GPUMemoryShared);
                break;
            case "D3D Shared Memory Total":
                GPUMemorySharedTotal = sensor.Value.GetValueOrDefault();
                break;

        }
    }

    private void HandleGPU_Load(ISensor sensor)
    {
        switch (sensor.Name)
        {
            case "D3D 3D":
                GPULoad = sensor.Value.GetValueOrDefault();
                GPULoadChanged?.Invoke(GPULoad);
                break;
        }
    }

    private void HandleGPU_Clock(ISensor sensor)
    {
        switch (sensor.Name)
        {
            case "GPU Core":
                if (sensor.Value != GPUClock)
                {
                    GPUClock = sensor.Value.GetValueOrDefault();
                    GPUClockChanged?.Invoke(GPUClock);
                }
                break;
        }
    }

    private void HandleGPU_Power(ISensor sensor)
    {
        switch (sensor.Name)
        {
            case "GPU Core":
                GPUPower = sensor.Value.GetValueOrDefault();
                GPUPowerChanged?.Invoke(GPUPower);
                break;
        }
    }

    private void HandleGPU_Temp(ISensor sensor)
    {
        switch (sensor.Name)
        {
            case "GPU VR SoC":
                GPUTemp = sensor.Value.GetValueOrDefault();
                GPUTemperatureChanged?.Invoke(GPUTemp);
                break;
        }
    }

    #endregion

    #region CPU updates

    public float GetCPULoad() => computer?.IsCpuEnabled ?? false ? CPULoad : float.NaN;
    public float GetCPUPower() => computer?.IsCpuEnabled ?? false ? CPUPower : float.NaN;
    public float GetCPUTemperature() => computer?.IsCpuEnabled ?? false ? CPUTemp : float.NaN;
    public float GetCPUClock() => computer?.IsCpuEnabled ?? false ? CPUClock : float.NaN;

    private void HandleCPU(IHardware cpu)
    {
        var highestClock = 0f;
        foreach (var sensor in cpu.Sensors)
        {
            // May crash the app when Value is null, better to check first
            if (sensor.Value is null)
                continue;

            switch (sensor.SensorType)
            {
                case SensorType.Load:
                    HandleCPU_Load(sensor);
                    break;
                case SensorType.Clock:
                    highestClock = HandleCPU_Clock(sensor, highestClock);
                    break;
                case SensorType.Power:
                    HandleCPU_Power(sensor);
                    break;
                case SensorType.Temperature:
                    try
                    {
                        using var ct = new PerformanceCounter("Thermal Zone Information", "Temperature", @"\_TZ.TZ01", true);
                        CPUTemp = ct.NextValue() - 273.15f;
                    }
                    catch
                    {
                        HandleCPU_Temp(sensor);
                    }

                    break;
            }
        }
    }

    private void HandleCPU_Load(ISensor sensor)
    {
        switch (sensor.Name)
        {
            //case "CPU Total":
            case "CPU Core Max":
                CPULoad = sensor.Value.GetValueOrDefault();
                CPULoadChanged?.Invoke(CPULoad);
                break;
        }
    }

    private float HandleCPU_Clock(ISensor sensor, float currentHighest)
    {
        if (sensor.Name.StartsWith("Core #", StringComparison.OrdinalIgnoreCase) ||
            sensor.Name.StartsWith("CPU Core #", StringComparison.OrdinalIgnoreCase))
        {
            var value = sensor.Value.GetValueOrDefault();
            if (value > currentHighest)
            {
                CPUClock = sensor.Value.GetValueOrDefault();
                CPUClockChanged?.Invoke(CPUClock);
                return value;
            }
        }
        return currentHighest;
    }

    private void HandleCPU_Power(ISensor sensor)
    {
        switch (sensor.Name)
        {
            case "Package":
            case "CPU Package":
                CPUPower = sensor.Value.GetValueOrDefault();
                CPUPowerChanged?.Invoke(CPUPower);
                break;
        }
    }

    private void HandleCPU_Temp(ISensor sensor)
    {
        switch (sensor.Name)
        {
            case "CPU Package":
            case "Core (Tctl/Tdie)":
                CPUTemp = sensor.Value.GetValueOrDefault();
                CPUTemperatureChanged?.Invoke(CPUTemp);
                break;
        }

    }

    #endregion

    #region Memory updates
    public float GetMemoryUsage() => computer?.IsMemoryEnabled ?? false ? MemoryUsage : float.NaN;
    public float GetMemoryAvailable() => computer?.IsMemoryEnabled ?? false ? MemoryAvailable : float.NaN;
    public float GetMemoryTotal() => GetMemoryUsage() + GetMemoryAvailable();

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
        switch (sensor.Name)
        {
            case "Memory Used":
                MemoryUsage = sensor.Value.GetValueOrDefault() * 1024;
                MemoryUsageChanged?.Invoke(MemoryUsage);
                break;
            case "Memory Available":
                MemoryAvailable = sensor.Value.GetValueOrDefault();
                MemoryAvailableChanged?.Invoke(MemoryAvailable);
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
        //BatteryPower = 0f;
        //BatteryRemainingCapacity = 0f;

        try
        {
            var scope = new ManagementScope("root\\WMI");
            var query = new ObjectQuery("SELECT * FROM BatteryStatus");

            using var searcher = new ManagementObjectSearcher(scope, query);
            foreach (var obj in searcher.Get().Cast<ManagementObject>())
            {
                BatteryRemainingCapacity = Convert.ToSingle(obj["RemainingCapacity"]);
                var chargeRate = Convert.ToSingle(obj["ChargeRate"]);
                var dischargeRate = Convert.ToSingle(obj["DischargeRate"]);

                if (chargeRate > 0)
                    BatteryPower = chargeRate / 1000;
                else
                    BatteryPower = -dischargeRate / 1000;

                if (float.IsNormal(BatteryPower))
                    BatteryPowerChanged?.Invoke(BatteryPower);
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
            var scope = new ManagementScope("root\\WMI");
            var query = new ObjectQuery("SELECT * FROM BatteryFullChargedCapacity");

            using var searcher = new ManagementObjectSearcher(scope, query);
            foreach (var obj in searcher.Get().OfType<ManagementObject>())
            {
                BatteryFullCapacity = Convert.ToSingle(obj["FullChargedCapacity"]);
            }

        }
        catch (Exception ex)
        {
            LogManager.LogError("Full Charge Reading: " + ex.Message);
        }

    }

    private void ReadDesignCapacity()
    {
        if (BatteryDesignCapacity > 0) return;

        try
        {
            var scope = new ManagementScope("root\\WMI");
            var query = new ObjectQuery("SELECT * FROM BatteryStaticData");

            using var searcher = new ManagementObjectSearcher(scope, query);
            foreach (var obj in searcher.Get().OfType<ManagementObject>())
            {
                BatteryDesignCapacity = Convert.ToSingle(obj["DesignedCapacity"]);
            }

        }
        catch (Exception ex)
        {
            LogManager.LogError("Design Capacity Reading: " + ex.Message);
        }
    }

    private void RefreshBatteryHealth()
    {
        BatteryFullCapacity = float.NaN;
        BatteryHealth = GetBatteryHealthInternal() * 100f;
    }

    private float GetBatteryHealthInternal()
    {
        if (float.IsNaN(BatteryDesignCapacity))
        {
            ReadDesignCapacity();
        }
        ReadFullChargeCapacity();

        if (float.IsNaN(BatteryDesignCapacity) || float.IsNaN(BatteryFullCapacity) || float.IsNaN(BatteryDesignCapacity) || float.IsNaN(BatteryFullCapacity))
        {
            return -1f;
        }

        var health = (float)BatteryFullCapacity / (float)BatteryDesignCapacity;
        LogManager.LogInformation($"Design Capacity: {BatteryDesignCapacity}mWh,  Remaining Capacity: {BatteryRemainingCapacity}mWh,  Full Charge Capacity: {BatteryFullCapacity}mWh,  Health: {health}%");

        return health;
    }

    private void ReadBatterySensors()
    {
        //BatteryPower = 0f;
        //BatteryTimeSpan = TimeSpan.Zero;
        //BatteryFullCapacity = float.NaN;

        ReadFullChargeCapacity();
        GetBatteryStatus();

        if (float.IsNormal(BatteryFullCapacity) && float.IsNormal(BatteryRemainingCapacity))
        {
            var currentBatteryCapacity = (float)Math.Min(100, (decimal)BatteryRemainingCapacity / (decimal)BatteryFullCapacity * 100);

            if (!float.IsNaN(BatteryCapacity) && BatteryCapacity != 100f && currentBatteryCapacity == 100f)
                ToastManager.RunToast(
                    Properties.Resources.BatteryFullyCharged, ToastIcons.BatteryFull
                    );

            BatteryCapacity = currentBatteryCapacity;
            BatteryLevelChanged?.Invoke(BatteryCapacity);

            if (float.IsNormal(BatteryPower))
            {
                if (BatteryPower > 0)
                    BatteryTimeSpan = (BatteryFullCapacity / 1000 - BatteryRemainingCapacity / 1000) / BatteryPower * 60f;
                else
                    BatteryTimeSpan = (BatteryRemainingCapacity / 1000 / BatteryPower) * 60f * -1;
                BatteryTimeSpanChanged?.Invoke(BatteryTimeSpan);
            }
        }
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

    public delegate void NetworkSpeedEventHandler(float speedDown, float speedUp);
    public delegate void CPUPerformanceEventHandler(float cpuLoad, float cpuPower, float cpuClock, float cpuTemp);
    public delegate void GPUPerformanceEventHandler(float gpuLoad, float gpuPower, float gpuClock, float gpuTemp);

    public delegate void RAMUsageEventHandler(float memUsage, float memAvail);
    public delegate void VRAMUsageEventHandler(float memUsage, float memAvail);
    public delegate void FanSpeedEventHandler(float fanDuty, float fanSpeed);
    public delegate void BatteryEventHandler(float battLevel, float battPower, float battTime);

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