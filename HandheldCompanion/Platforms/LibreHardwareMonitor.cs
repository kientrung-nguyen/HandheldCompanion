using HandheldCompanion.Devices;
using HandheldCompanion.Managers;
using LibreHardwareMonitor.Hardware;
using System;
using System.Timers;

namespace HandheldCompanion.Platforms
{
    public class LibreHardwareMonitor : IPlatform
    {
        private Computer computer;
        private string ProductName;

        private Timer sensorTImer;
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
        public float? GPUMemoryUsage;

        public float? BatteryChargeLevel;
        public float? BatteryPower = 0f;
        public float? BatteryTimeSpan;

        public float? BatteryDesignCapacity;
        public float? BatteryFullCapacity;
        public float? BatteryRemainingCapacity;
        public float? BatteryCapacity = -1f;
        public float? BatteryHealth = -1f;


        public LibreHardwareMonitor()
        {
            PlatformType = PlatformType.LibreHardwareMonitor;
            Name = "LibreHardwareMonitor";
            IsInstalled = true;

            ProductName = MotherboardInfo.Product;

            // watchdog to populate sensors
            sensorTImer = new Timer(updateInterval) { Enabled = false };
            sensorTImer.Elapsed += sensorTImer_Elapsed;

            // prepare for sensors reading
            computer = new Computer
            {
                IsCpuEnabled = true,
                IsMemoryEnabled = true,
                IsBatteryEnabled = true,
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
                    sensorTImer.Interval = updateInterval;
                    break;
            }
        }

        public override bool Start()
        {
            // open computer, slow
            computer?.Open();

            sensorTImer?.Start();

            return base.Start();
        }

        public override bool Stop(bool kill = false)
        {
            sensorTImer?.Stop();

            // wait until all tasks are complete
            lock (updateLock)
            {
                computer?.Close();
            }

            return base.Stop(kill);
        }

        private void sensorTImer_Elapsed(object? sender, ElapsedEventArgs e)
        {
            lock (updateLock)
            {
                // pull temperature sensor
                foreach (IHardware hardware in computer.Hardware)
                {
                    hardware.Update();

                    switch (hardware.HardwareType)
                    {
                        case HardwareType.Cpu:
                            HandleCPU(hardware);
                            break;
                        case HardwareType.GpuAmd:
                            HandleGPU(hardware);
                            break;
                        case HardwareType.Memory:
                            HandleMemory(hardware);
                            break;
                        case HardwareType.Battery:
                            HandleBattery(hardware);
                            break;
                    }
                }
            }
        }

        private void HandleCPUFan()
        {
            CPUFanSpeed = IDevice.GetCurrent().ReadFanSpeed();
            CPUFanDuty = IDevice.GetCurrent().ReadFanDuty();
        }

        #region CPU updates

        private void HandleCPU(IHardware cpu)
        {
            HandleCPUFan();
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
                        HandleCPU_Temp(sensor);
                        break;
                }
            }
        }

        private void HandleCPU_Load(ISensor sensor)
        {
            switch (sensor.Name)
            {
                case "CPU Total":
                    //case "CPU Core Max":
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

        private void HandleCPU_Temp(ISensor sensor)
        {
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
                    case SensorType.Data:
                        HandleGPU_Data(sensor);
                        break;
                }
            }
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
        }

        private void HandleMemory_Data(ISensor sensor)
        {
            switch (sensor.Name)
            {
                case "Memory Used":
                    MemoryUsage = sensor.Value * 1024;
                    MemoryUsageChanged?.Invoke(MemoryUsage);
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
                BatteryCapacity = (float)Math.Min(100, ((decimal)BatteryRemainingCapacity / (decimal)BatteryFullCapacity) * 100);
            }

            RefreshBatteryHealth();
        }

        private void RefreshBatteryHealth()
        {
            BatteryHealth = GetBatteryHealth() * 100;
        }

        private float GetBatteryHealth()
        {
            decimal health = (decimal)BatteryFullCapacity / (decimal)BatteryDesignCapacity;
            return (float)health;
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
}