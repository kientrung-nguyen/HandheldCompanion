using HandheldCompanion.Devices;
using HandheldCompanion.Managers;
using HandheldCompanion.Views.Windows;
using LibreHardwareMonitor.Hardware;
using System;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Timers;

namespace HandheldCompanion.Platforms;

public class LibreHardwareMonitor : IPlatform
{
    private Computer computer;
    private string ProductName;

    private Timer sensorTimer;
    private int updateInterval = 1000;
    private object updateLock = new();

    public float? CPULoad;
    public float? CPUClock;
    public float? CPUPower;
    public float? CPUTemp;


    public float? CPUFanSpeed;
    public float? CPUFanDuty;

    public float? GPULoad;
    public float? GPUClock;
    public float? GPUPower;
    public float? GPUTemp;

    public float? MemoryUsage;
    public float? MemoryAvailable;
    public float? MemoryLoad;
    public float? GPUMemoryUsage;
    public float? GPUMemoryAvailable;
    public float? GPUMemoryLoad;

    public float? BatteryChargeLevel;

    public float? BatteryDesignCapacity;
    public float? BatteryFullCapacity;
    public float? BatteryRemainingCapacity;
    public float? BatteryPower = 0f;
    public float? BatteryCapacity = -1f;
    public float? BatteryHealth = -1f;
    public float? BatteryTimeSpan;

    static long lastCPURefresh;
    static long lastGPURefresh;
    static long lastFanRefresh;
    static long lastMemoryRefresh;
    static long lastBatteryRefresh;
    static long lastChargeRefresh;

    public LibreHardwareMonitor()
    {
        PlatformType = PlatformType.LibreHardwareMonitor;
        Name = "LibreHardwareMonitor";
        IsInstalled = true;

        ProductName = MotherboardInfo.Product;

        // watchdog to populate sensors
        sensorTimer = new Timer(updateInterval) { Enabled = false };
        sensorTimer.Elapsed += sensorTimer_Elapsed;

        // prepare for sensors reading
        computer = new Computer
        {
            IsCpuEnabled = true,
            IsMemoryEnabled = true,
            //IsBatteryEnabled = true,
            IsGpuEnabled = true
        };

        SettingsManager.SettingValueChanged += SettingsManager_SettingValueChanged;
    }

    private void SettingsManager_SettingValueChanged(string name, object value)
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
        // open computer, slow
        computer?.Open();

        sensorTimer?.Start();

        return base.Start();
    }

    public override bool Stop(bool kill = false)
    {
        sensorTimer?.Stop();

        // wait until all tasks are complete
        lock (updateLock)
        {
            computer?.Close();
        }

        return base.Stop(kill);
    }

    private void ReadBatterySensors()
    {
        BatteryPower = 0f;
        BatteryTimeSpan = null;
        BatteryFullCapacity = null;

        ReadFullChargeCapacity();
        GetBatteryStatus();

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
                    BatteryTimeSpan = ((BatteryFullCapacity / 1000) - (BatteryRemainingCapacity / 1000)) / BatteryPower * 60f;
                else
                    BatteryTimeSpan = BatteryRemainingCapacity / 1000 / BatteryPower * 60f * -1;
                BatteryTimeSpanChanged?.Invoke(BatteryTimeSpan);
            }
        }
    }

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
        BatteryHealth = GetBatteryHealth() * 100f;
    }

    private float GetBatteryHealth()
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

            if (Math.Abs(DateTimeOffset.Now.ToUnixTimeMilliseconds() - lastChargeRefresh) > 7000)
            {
                lastChargeRefresh = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                ReadBatterySensors();
            }

            if (Math.Abs(DateTimeOffset.Now.ToUnixTimeMilliseconds() - lastFanRefresh) > 5000)
            {
                lastFanRefresh = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                HandleFan();
            }

            // pull temperature sensor
            foreach (var hardware in computer.Hardware)
            {
                try
                {
                    switch (hardware.HardwareType)
                    {
                        case HardwareType.Cpu:
                            if (Math.Abs(DateTimeOffset.Now.ToUnixTimeMilliseconds() - lastCPURefresh) < 500) continue;
                            lastCPURefresh = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                            hardware.Update();
                            HandleCPU(hardware);
                            break;
                        case HardwareType.GpuAmd:
                            if (Math.Abs(DateTimeOffset.Now.ToUnixTimeMilliseconds() - lastGPURefresh) < 500) continue;
                            lastGPURefresh = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                            hardware.Update();
                            HandleGPU(hardware);
                            break;
                        case HardwareType.Memory:
                            if (Math.Abs(DateTimeOffset.Now.ToUnixTimeMilliseconds() - lastMemoryRefresh) < 3000) continue;
                            lastMemoryRefresh = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                            hardware.Update();
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
                catch (Exception ex)
                {
                    LogManager.LogError($"{nameof(LibreHardwareMonitor)} Update: {ex.Message}");
                }
            }
        }
    }

    private void HandleFan()
    {
        CPUFanSpeed = IDevice.GetCurrent().ReadFanSpeed();
        CPUFanDuty = IDevice.GetCurrent().ReadFanDuty();
    }

    #region CPU updates

    private void HandleCPU(IHardware cpu)
    {
        var highestClock = 0f;
        foreach (var sensor in cpu.Sensors)
        {
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
                    if (HandleCPU_Temp(sensor) == 0)
                        try
                        {
                            using var ct = new PerformanceCounter("Thermal Zone Information", "Temperature", @"\_TZ.TZ01", true);
                            CPUTemp = ct.NextValue() - 273f;
                        }
                        catch
                        {
                            //Debug.WriteLine("Failed reading CPU temp :" + ex.Message);
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
                CPULoadChanged?.Invoke(CPUPower);
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

    private float HandleCPU_Temp(ISensor sensor)
    {
        var prevTemp = CPUTemp ?? 0;
        switch (sensor.Name)
        {
            case "CPU Package":
            case "Core (Tctl/Tdie)":
                CPUTemp = sensor.Value;
                // dirty
                switch (ProductName)
                {
                    case "Galileo":
                        CPUTemp /= 2.0f;
                        break;
                }
                CPUTemperatureChanged?.Invoke(CPUTemp);
                break;
        }

        return (CPUTemp ?? 0f) - prevTemp;
    }

    #endregion

    #region GPU updates

    private void HandleGPU(IHardware gpu)
    {
        foreach (var sensor in gpu.Sensors)
        {
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
                case SensorType.SmallData:
                    HandleGPU_Data(sensor);
                    break;
            }
        }

        if (GPUMemoryUsage > 0 && GPUMemoryAvailable > 0)
            GPUMemoryLoad = GPUMemoryUsage / (GPUMemoryUsage + GPUMemoryAvailable) * 100;
    }

    private void HandleGPU_Clock(ISensor sensor)
    {
        switch (sensor.Name)
        {
            case "GPU Core":
                GPUClock = sensor.Value;
                break;
        }
    }

    private void HandleGPU_Temp(ISensor sensor)
    {
        switch (sensor.Name)
        {
            case "GPU VR SoC":
                GPUTemp = sensor.Value;
                break;
        }
    }

    private void HandleGPU_Load(ISensor sensor)
    {
        switch (sensor.Name)
        {
            case "D3D 3D":
                GPULoad = sensor.Value;
                break;
        }
    }

    private void HandleGPU_Power(ISensor sensor)
    {
        switch (sensor.Name)
        {
            case "GPU Core":
                GPUPower = sensor.Value;
                break;
        }
    }


    private void HandleGPU_Data(ISensor sensor)
    {
        switch (sensor.Name)
        {
            case "D3D Dedicated Memory Used":
                GPUMemoryUsage = sensor.Value;
                break;
            case "D3D Dedicated Memory Free":
                GPUMemoryAvailable = sensor.Value;
                break;
        }
    }

    #endregion

    #region Memory updates

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

        if (MemoryUsage > 0 && MemoryAvailable > 0)
            MemoryLoad = MemoryUsage / (MemoryUsage + MemoryAvailable) * 100;

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
                MemoryAvailable = sensor.Value * 1024;
                break;
        }
    }

    #endregion

    #region Battery updates

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
            BatteryCapacity = (float)Math.Min(100, (decimal)BatteryRemainingCapacity / (decimal)BatteryFullCapacity * 100);
            if (BatteryPower == 0 || BatteryCapacity == 100)
                BatteryTimeSpan = null;
            if (BatteryPower > 0)
                BatteryTimeSpan = ((BatteryFullCapacity / 1000) - (BatteryRemainingCapacity / 1000)) / BatteryPower * 60f;

            if (BatteryPower < 0 && BatteryTimeSpan == null)
                BatteryTimeSpan = BatteryRemainingCapacity / 1000 / BatteryPower * 60f * -1;
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
                BatteryTimeSpan = sensor.Value / 60;
                BatteryTimeSpanChanged?.Invoke(BatteryTimeSpan);
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

    public event ChangedHandler MemoryUsageChanged;

    public event ChangedHandler BatteryLevelChanged;
    public event ChangedHandler BatteryPowerChanged;
    public event ChangedHandler BatteryTimeSpanChanged;
    #endregion
}