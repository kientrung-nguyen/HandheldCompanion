using HandheldCompanion.Devices;
using HandheldCompanion.GraphicsProcessingUnit;
using HandheldCompanion.Managers;
using HandheldCompanion.Shared;
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

    private float? CPULoad;
    private float? CPUClock;
    private float? CPUPower;
    private float? CPUTemp;


    public float? CPUFanSpeed;
    public float? CPUFanDuty;

    private float? GPULoad;
    private float? GPUClock;
    private float? GPUPower;
    private float? GPUTemp;

    private float? GPUMemory;
    private float? GPUMemoryTotal;

    private float? GPUMemoryShared;
    private float? GPUMemorySharedTotal;

    private float? GPUMemoryDedicated;
    private float? GPUMemoryDedicatedTotal;

    private float? MemoryUsage;
    private float? MemoryAvailable;

    private float? BatteryChargeLevel;

    private float? BatteryDesignCapacity;
    private float? BatteryFullCapacity;
    private float? BatteryRemainingCapacity;
    private float? BatteryPower;
    private float? BatteryCapacity;
    private float? BatteryHealth;
    private TimeSpan BatteryTimeSpan = TimeSpan.Zero;

    long lastCPURefresh;
    long lastGPURefresh;
    long lastFanRefresh;
    long lastMemoryRefresh;
    long lastBatteryRefresh;
    long lastChargeRefresh;


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
            // open computer, slow
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

            if (Math.Abs(DateTimeOffset.Now.ToUnixTimeMilliseconds() - lastChargeRefresh) > 7000)
            {
                lastChargeRefresh = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                ReadBatterySensors();
            }

            if (lastBatteryRefresh == 0 || Math.Abs(DateTimeOffset.Now.ToUnixTimeMilliseconds() - lastBatteryRefresh) > 10 * 60_000)
            {
                lastBatteryRefresh = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                RefreshBatteryHealth();
            }

            if (Math.Abs(DateTimeOffset.Now.ToUnixTimeMilliseconds() - lastFanRefresh) > 5000)
            {
                lastFanRefresh = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                HandleFan();
            }


            //if (Math.Abs(DateTimeOffset.Now.ToUnixTimeMilliseconds() - lastGPURefresh) > 500)
            //{
            //    lastGPURefresh = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            //    HandleGPU(GPUManager.GetCurrent());
            //}


            // pull temperature sensor
            foreach (var hardware in computer.Hardware)
            {
                try { hardware.Update(); } catch { }
                switch (hardware.HardwareType)
                {
                    case HardwareType.Cpu:
                        if (Math.Abs(DateTimeOffset.Now.ToUnixTimeMilliseconds() - lastCPURefresh) < 1000) continue;
                        lastCPURefresh = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                        HandleCPU(hardware);
                        break;
                    case HardwareType.GpuNvidia:
                    case HardwareType.GpuAmd:
                    case HardwareType.GpuIntel:
                        if (Math.Abs(DateTimeOffset.Now.ToUnixTimeMilliseconds() - lastGPURefresh) < 1000) continue;
                        lastGPURefresh = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                        HandleGPU(hardware);
                        break;

                    case HardwareType.Memory:
                        if (Math.Abs(DateTimeOffset.Now.ToUnixTimeMilliseconds() - lastMemoryRefresh) < 3000) continue;
                        lastMemoryRefresh = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                        HandleMemory(hardware);
                        break;
                    default: continue;
                        /*
                    case HardwareType.Battery:
                        HandleBattery(hardware);
                        break;
                        */
                }
            }
        }
    }

    private void HandleFan()
    {
        CPUFanSpeed = IDevice.GetCurrent().ReadFanSpeed();
        CPUFanDuty = IDevice.GetCurrent().ReadFanDuty();
    }

    #region GPU updates
    public float? GetGPULoad() => computer?.IsGpuEnabled ?? false ? GPULoad : null;
    public float? GetGPUPower() => computer?.IsGpuEnabled ?? false ? GPUPower : null;
    public float? GetGPUTemperature() => computer?.IsGpuEnabled ?? false ? GPUTemp : null;
    public float? GetGPUClock() => computer?.IsGpuEnabled ?? false ? GPUClock : null;

    public float? GetGPUMemory() => computer?.IsGpuEnabled ?? false ? GPUMemory : null;
    public float? GetGPUMemoryDedicated() => computer?.IsGpuEnabled ?? false ? GPUMemoryDedicated : null;
    public float? GetGPUMemoryShared() => computer?.IsGpuEnabled ?? false ? GPUMemoryShared : null;

    public float? GetGPUMemoryTotal() => computer?.IsGpuEnabled ?? false ? GPUMemoryTotal : null;
    public float? GetGPUMemoryDedicatedTotal() => computer?.IsGpuEnabled ?? false ? GPUMemoryDedicatedTotal : null;
    public float? GetGPUMemorySharedTotal() => computer?.IsGpuEnabled ?? false ? GPUMemorySharedTotal : null;

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

    private void HandleGPU_Temp(ISensor sensor)
    {
        switch (sensor.Name)
        {
            case "GPU VR SoC":
                GPUTemp = sensor.Value;
                GPUTemperatureChanged?.Invoke(GPUTemp);
                break;
        }
    }

    private void HandleGPU_Load(ISensor sensor)
    {
        switch (sensor.Name)
        {
            case "D3D 3D":
                GPULoad = sensor.Value;
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
                    GPUClock = sensor.Value;
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
                GPUPower = sensor.Value;
                GPUPowerChanged?.Invoke(GPUPower);
                break;
        }
    }


    private void HandleGPU_Data(ISensor sensor)
    {
        switch (sensor.Name)
        {
            case "GPU Memory Used":
                GPUMemory = sensor.Value;
                GPUMemoryChanged?.Invoke(GPUMemory);
                break;
            case "GPU Memory Total":
                GPUMemoryTotal = sensor.Value;
                break;
            case "D3D Dedicated Memory Used":
                GPUMemoryDedicated = sensor.Value;
                GPUMemoryDedicatedChanged?.Invoke(GPUMemoryDedicated);
                break;
            case "D3D Dedicated Memory Total":
                GPUMemoryDedicatedTotal = sensor.Value;
                break;
            case "D3D Shared Memory Used":
                GPUMemoryShared = sensor.Value;
                GPUMemorySharedChanged?.Invoke(GPUMemoryShared);
                break;
            case "D3D Shared Memory Total":
                GPUMemorySharedTotal = sensor.Value;
                break;

        }
    }

    #endregion

    #region CPU updates

    public float? GetCPULoad() => computer?.IsCpuEnabled ?? false ? CPULoad : null;
    public float? GetCPUPower() => computer?.IsCpuEnabled ?? false ? CPUPower : null;
    public float? GetCPUTemperature() => computer?.IsCpuEnabled ?? false ? CPUTemp : null;
    public float? GetCPUClock() => computer?.IsCpuEnabled ?? false ? CPUClock : null;

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
                CPULoad = sensor.Value;
                CPULoadChanged?.Invoke(CPULoad);
                break;
        }
    }

    private float HandleCPU_Clock(ISensor sensor, float currentHighest)
    {
        if (sensor.Name.StartsWith("Core #", StringComparison.OrdinalIgnoreCase) ||
            sensor.Name.StartsWith("CPU Core #", StringComparison.OrdinalIgnoreCase))
        {
            var value = sensor.Value;
            if (value > currentHighest)
            {
                CPUClock = sensor.Value;
                CPUClockChanged?.Invoke(CPUClock);
                return value ?? 0f;
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
                CPUPower = sensor.Value;
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
                CPUTemp = sensor.Value;
                CPUTemperatureChanged?.Invoke(CPUTemp);
                break;
        }

    }

    #endregion

    #region Memory updates
    public float? GetMemoryUsage() => computer?.IsMemoryEnabled ?? false ? MemoryUsage : null;
    public float? GetMemoryAvailable() => computer?.IsMemoryEnabled ?? false ? MemoryAvailable : null;
    public float? GetMemoryTotal() => GetMemoryUsage() + GetMemoryAvailable();

    private void HandleMemory(IHardware cpu)
    {
        foreach (var sensor in cpu.Sensors)
        {
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
                MemoryUsage = sensor.Value * 1024;
                MemoryUsageChanged?.Invoke(MemoryUsage);
                break;
            case "Memory Available":
                MemoryAvailable = sensor.Value;
                MemoryAvailableChanged?.Invoke(MemoryAvailable);
                break;
        }
    }

    #endregion

    #region Battery updates
    public float? GetBatteryLevel() => computer?.IsBatteryEnabled ?? false ? BatteryCapacity : null;
    public float? GetBatteryPower() => computer?.IsBatteryEnabled ?? false ? BatteryPower : null;
    public TimeSpan? GetBatteryTimeSpan() => computer?.IsBatteryEnabled ?? false ? BatteryTimeSpan : null;
    public float? GetBatteryHealth() => computer?.IsBatteryEnabled ?? false ? BatteryHealth : null;
    public float? GetBatteryRemainingCapacity() => computer?.IsBatteryEnabled ?? false ? BatteryRemainingCapacity : null;

    private void GetBatteryStatus()
    {
        BatteryPower = 0f;
        BatteryRemainingCapacity = 0f;

        try
        {
            ManagementScope scope = new ManagementScope("root\\WMI");
            ObjectQuery query = new ObjectQuery("SELECT * FROM BatteryStatus");

            using ManagementObjectSearcher searcher = new ManagementObjectSearcher(scope, query);
            foreach (ManagementObject obj in searcher.Get().Cast<ManagementObject>())
            {
                BatteryRemainingCapacity = Convert.ToSingle(obj["RemainingCapacity"]);
                var chargeRate = Convert.ToSingle(obj["ChargeRate"]);
                var dischargeRate = Convert.ToSingle(obj["DischargeRate"]);

                if (chargeRate > 0)
                    BatteryPower = chargeRate / 1000;
                else
                    BatteryPower = -dischargeRate / 1000;

                if (BatteryPower != 0)
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
        if (BatteryFullCapacity > 0) return;

        try
        {
            ManagementScope scope = new ManagementScope("root\\WMI");
            ObjectQuery query = new ObjectQuery("SELECT * FROM BatteryFullChargedCapacity");

            using ManagementObjectSearcher searcher = new ManagementObjectSearcher(scope, query);
            foreach (ManagementObject obj in searcher.Get().Cast<ManagementObject>())
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
            ManagementScope scope = new ManagementScope("root\\WMI");
            ObjectQuery query = new ObjectQuery("SELECT * FROM BatteryStaticData");

            using ManagementObjectSearcher searcher = new ManagementObjectSearcher(scope, query);
            foreach (ManagementObject obj in searcher.Get().Cast<ManagementObject>())
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
        BatteryFullCapacity = null;
        BatteryHealth = GetBatteryHealthInternal() * 100f;
    }

    private float GetBatteryHealthInternal()
    {
        if (BatteryDesignCapacity is null)
        {
            ReadDesignCapacity();
        }
        ReadFullChargeCapacity();

        if (BatteryDesignCapacity is null || BatteryFullCapacity is null || BatteryDesignCapacity == 0 || BatteryFullCapacity == 0)
        {
            return -1f;
        }

        var health = (float)BatteryFullCapacity / (float)BatteryDesignCapacity;
        LogManager.LogInformation($"Design Capacity: {BatteryDesignCapacity}mWh,  Remaining Capacity: {BatteryRemainingCapacity}mWh,  Full Charge Capacity: {BatteryFullCapacity}mWh,  Health: {health}%");

        return health;
    }

    private void ReadBatterySensors()
    {
        BatteryPower = 0f;
        BatteryTimeSpan = TimeSpan.Zero;
        BatteryFullCapacity = null;

        ReadFullChargeCapacity();
        GetBatteryStatus();

        if (BatteryFullCapacity > 0 && BatteryRemainingCapacity > 0)
        {
            var currentBatteryCapacity = (float)Math.Min(100, (decimal)BatteryRemainingCapacity / (decimal)BatteryFullCapacity * 100);

            if (BatteryCapacity != null && BatteryCapacity != 100f && currentBatteryCapacity == 100f)
                ToastManager.RunToast(
                    Properties.Resources.BatteryFullyCharged, ToastIcons.BatteryFull
                    );

            BatteryCapacity = currentBatteryCapacity;
            BatteryLevelChanged?.Invoke(BatteryCapacity);

            if (BatteryPower != 0)
            {
                if (BatteryPower > 0)
                    BatteryTimeSpan = TimeSpan.FromMinutes(((BatteryFullCapacity / 1000 - BatteryRemainingCapacity / 1000) / BatteryPower).Value * 60d);
                else
                    BatteryTimeSpan = TimeSpan.FromMinutes((BatteryRemainingCapacity / 1000 / BatteryPower).Value * 60d * -1);
                BatteryTimeSpanChanged?.Invoke((float)BatteryTimeSpan.TotalMinutes);
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
                    BatteryTimeSpan = TimeSpan.FromMinutes(((BatteryFullCapacity / 1000 - BatteryRemainingCapacity / 1000) / BatteryPower).Value * 60d);
                else
                    BatteryTimeSpan = TimeSpan.FromMinutes((BatteryRemainingCapacity / 1000 / BatteryPower).Value * 60d * -1);
                BatteryTimeSpanChanged?.Invoke((float)BatteryTimeSpan.TotalMinutes);
            }
        }

        //LogManager.LogDebug($"Charge: {BatteryPower}W, RemainingCapacity: {BatteryRemainingCapacity}mWh, BatteryFullCapacity: {BatteryFullCapacity}mWh, BatterySpan: {BatteryTimeSpan}mins");
    }

    private void HandleBattery_Level(ISensor sensor)
    {
        switch (sensor.Name)
        {
            case "Charge Level":
                BatteryChargeLevel = sensor.Value;
                BatteryLevelChanged?.Invoke(BatteryChargeLevel);
                break;
        }
    }

    private void HandleBattery_Power(ISensor sensor)
    {
        switch (sensor.Name)
        {
            case "Discharge Rate":
                BatteryPower = -sensor.Value;
                BatteryPowerChanged?.Invoke(BatteryPower);
                break;
            case "Charge Rate":
                BatteryPower = sensor.Value;
                BatteryPowerChanged?.Invoke(BatteryPower);
                break;
        }
    }

    private void HandleBattery_TimeSpan(ISensor sensor)
    {
        switch (sensor.Name)
        {
            case "Remaining Time (Estimated)":
                BatteryTimeSpan = TimeSpan.FromMinutes(sensor.Value ?? 0d / 60d);
                BatteryTimeSpanChanged?.Invoke((float)BatteryTimeSpan.TotalMinutes);
                break;
        }
    }

    private void HandleBattery_Energy(ISensor sensor)
    {
        switch (sensor.Name)
        {
            case "Designed Capacity":
                BatteryDesignCapacity = sensor.Value;
                break;
            case "Full Charged Capacity":
                BatteryFullCapacity = sensor.Value;
                break;
            case "Remaining Capacity":
                BatteryRemainingCapacity = sensor.Value;
                break;
        }
    }

    #endregion

    #region Events

    public delegate void ChangedHandler(float? value);

    public event ChangedHandler CPULoadChanged;
    public event ChangedHandler CPUPowerChanged;
    public event ChangedHandler CPUClockChanged;
    public event ChangedHandler CPUTemperatureChanged;

    public event ChangedHandler GPULoadChanged;
    public event ChangedHandler GPUPowerChanged;
    public event ChangedHandler GPUClockChanged;
    public event ChangedHandler GPUTemperatureChanged;
    public event ChangedHandler GPUMemoryChanged;
    public event ChangedHandler GPUMemoryDedicatedChanged;
    public event ChangedHandler GPUMemorySharedChanged;

    public event ChangedHandler MemoryUsageChanged;
    public event ChangedHandler MemoryAvailableChanged;

    public event ChangedHandler BatteryLevelChanged;
    public event ChangedHandler BatteryPowerChanged;
    public event ChangedHandler BatteryTimeSpanChanged;
    #endregion
}