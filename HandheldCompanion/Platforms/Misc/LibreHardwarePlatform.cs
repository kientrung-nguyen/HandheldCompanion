using HandheldCompanion.Devices;
using HandheldCompanion.Managers;
using HandheldCompanion.Misc;
using HandheldCompanion.Shared;
using HandheldCompanion.Utils;
using LibreHardwareMonitor.Hardware;
using System;
using System.Net.NetworkInformation;
using System.Timers;
using Monitor = System.Threading.Monitor;

namespace HandheldCompanion.Platforms.Misc
{
    public class LibreHardwarePlatform : IPlatform
    {
        private const int MinimumCpuPollingInterval = 500;
        private const int MinimumGpuPollingInterval = 500;
        private const int MinimumFanPollingInterval = 3000;
        private const int MinimumMemoryPollingInterval = 2000;
        private const int MinimumBatteryPollingInterval = 5000;
        private const int MinimumNetworkPollingInterval = 1000;

        private const short INTERVAL_DEFAULT = 3000; // default interval between value scans
        private const short INTERVAL_DEGRADED = 5000; // degraded interval between value scans

        private Computer computer;
        private NetworkInterface? networkInterface;

        private bool computerOpened;

        private volatile bool halting = false;

        private Timer updateTimer;
        private int updateInterval = 1000;

        private long lastCpuUpdateTick;
        private long lastGpuUpdateTick;
        private long lastFanUpdateTick;
        private long lastMemoryUpdateTick;
        private long lastBatteryUpdateTick;
        private long lastNetworkUpdateTick;

        // CPU
        private float? CPULoad;
        private float? CPULoadMax;
        private float? CPUClock;
        private float? CPUClockMax;
        private float? CPUPower;
        private float? CPUTemperature;

        // GPU
        private float? GPULoad;
        private float? GPUClock;
        private float? GPUPower;
        private float? GPUTemperature;
        private float? GPUMemory;
        private float? GPUMemoryDedicated;
        private float? GPUMemoryShared;
        private float? GPUMemoryTotal;
        private float? GPUMemoryDedicatedTotal;
        private float? GPUMemorySharedTotal;

        // MEMORY
        private float? MemoryUsage;
        private float? MemoryAvailable;

        // BATTERY
        private float? BatteryLevel;
        private float? BatteryPower;
        private float? BatteryTimeSpan;

        private float? BatteryDesignCapacity;
        private float? BatteryFullCapacity;
        private float? BatteryRemainingCapacity;

        // NETWORK
        private float? NetworkSpeedUp;
        private float? NetworkSpeedDown;

        // FAN
        private float? CPUFanRPM;

        public LibreHardwarePlatform()
        {
            Name = "LibreHardwareMonitor";
            IsInstalled = true;

            // watchdog to populate sensors
            updateTimer = new Timer(updateInterval) { Enabled = false };
            updateTimer.Elapsed += UpdateTimer_Elapsed;

            // prepare for sensors reading
            computer = new Computer
            {
                IsNetworkEnabled = IDevice.GetCurrent().NetworkMonitor,
                IsCpuEnabled = IDevice.GetCurrent().CpuMonitor,
                IsGpuEnabled = IDevice.GetCurrent().GpuMonitor,
                IsMemoryEnabled = IDevice.GetCurrent().MemoryMonitor,
                IsBatteryEnabled = IDevice.GetCurrent().BatteryMonitor
            };
        }

        private void SettingsManager_SettingValueChanged(string name, object? value, bool temporary)
        {
            switch (name)
            {
                case "OnScreenDisplayRefreshRate":
                    updateInterval = Convert.ToInt32(value);
                    updateTimer.Interval = updateInterval;
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

            networkInterface ??= DeviceUtils.GetPrimaryNetworkInterface();
            halting = true;

            HardwareControl.InitCPUAsync();

            if (computer is not null)
            {
                // open computer, slow task
                try
                {
                    computer.Open();
                    computerOpened = true;
                }
                catch (Exception ex)
                {
                    LogManager.LogError("LibreHardwareMonitor computer.Open() failed, {0}", ex.Message);
                    computerOpened = false;
                }

                // prevent sensor from being stored to memory for too long
                var window = new TimeSpan(0, 0, 10);
                foreach (var hardware in computer.Hardware)
                    ApplyValuesTimeWindow(hardware, window);
            }

            updateTimer?.Start();

            return base.Start();
        }

        private static void ApplyValuesTimeWindow(IHardware hardware, TimeSpan window)
        {
            foreach (var sensor in hardware.Sensors)
                sensor.ValuesTimeWindow = window;
            foreach (var subHardware in hardware.SubHardware)
                ApplyValuesTimeWindow(subHardware, window);
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
            SettingsManager_SettingValueChanged("OnScreenDisplayRefreshRate", ManagerFactory.settingsManager.GetString("OnScreenDisplayRefreshRate"), false);
        }

        public void Pause()
        {
            halting = true;
            GPUManager.GetCurrent()?.StopTelemetry();
        }

        public void Resume()
        {
            halting = false;
            GPUManager.GetCurrent()?.StartTelemetry();
        }

        public override bool Stop(bool kill = false)
        {
            ManagerFactory.settingsManager.SettingValueChanged -= SettingsManager_SettingValueChanged;
            ManagerFactory.settingsManager.Initialized -= SettingsManager_Initialized;

            halting = true;
            updateTimer?.Stop();

            HardwareControl.Dispose();

            networkInterface = null;

            // wait until all tasks are complete
            lock (updateLock)
            {
                computerOpened = false;
                try { computer.Close(); } catch { }
            }

            return base.Stop(kill);
        }

        private void UpdateTimer_Elapsed(object? sender, ElapsedEventArgs e)
        {
            if (!computerOpened || computer is null)
                return;

            var hasHook = PlatformManager.RTSS?.HasHook() ?? false;
            if (!hasHook)
            {
                if (halting)
                    // raise events
                    SettingsManager_SettingValueChanged("OnScreenDisplayRefreshRate", INTERVAL_DEGRADED.ToString(), false);
                else
                    SettingsManager_SettingValueChanged("OnScreenDisplayRefreshRate", INTERVAL_DEFAULT.ToString(), false);
            }
            else
            {
                SettingsManager_SettingValueChanged("OnScreenDisplayRefreshRate", ManagerFactory.settingsManager.GetString("OnScreenDisplayRefreshRate"), false);
                halting = false;
            }

            if (halting)
            {
                HandleCPU_TemperatureValue(HardwareControl.GetCPUTemp());
                return;
            }

            if (Monitor.TryEnter(updateLock))
            {
                try
                {
                    long now = Environment.TickCount64;
                    bool shouldUpdateCpu = ShouldUpdateHardware(now, ref lastCpuUpdateTick, MinimumCpuPollingInterval);
                    bool shouldUpdateGpu = ShouldUpdateHardware(now, ref lastGpuUpdateTick, MinimumGpuPollingInterval);
                    bool shouldUpdateFan = ShouldUpdateHardware(now, ref lastFanUpdateTick, MinimumFanPollingInterval);
                    bool shouldUpdateMemory = ShouldUpdateHardware(now, ref lastMemoryUpdateTick, MinimumMemoryPollingInterval);
                    bool shouldUpdateBattery = ShouldUpdateHardware(now, ref lastBatteryUpdateTick, MinimumBatteryPollingInterval);
                    bool shouldUpdateNetwork = ShouldUpdateHardware(now, ref lastNetworkUpdateTick, MinimumNetworkPollingInterval);
                    
                    if (shouldUpdateFan)
                    {
                        if (HardwareControl.CPUFanRPM is not null)
                        {
                            shouldUpdateFan = false;
                            if (CPUFanRPM != HardwareControl.CPUFanRPM)
                            {
                                CPUFanRPM = HardwareControl.CPUFanRPM;
                                CPUFanSpeedChanged?.Invoke(CPUFanRPM);
                            }
                        }
                    }

                    if (shouldUpdateCpu)
                    {
                        HardwareControl.ReadCPUSensors();
                        if (HardwareControl.CPUUsage is not null &&
                            HardwareControl.CPUPower is not null &&
                            HardwareControl.CPUClock is not null &&
                            HardwareControl.CPUTemp is not null)
                        {
                            shouldUpdateCpu = false;
                            HandleCPU_LoadValue(HardwareControl.CPUUsage);
                            HandleCPU_PowerValue(HardwareControl.CPUPower);
                            HandleCPU_TemperatureValue(HardwareControl.CPUTemp);
                            HandleCPU_ClockValue(HardwareControl.CPUClock);
                        }
                    }

                    if (shouldUpdateBattery)
                    {
                        HardwareControl.ReadBatteryState();
                        if (HardwareControl.BatteryCapacity is not null and not 0 &&
                            HardwareControl.BatteryRemainingCapacity is not null and not 0)
                        {
                            shouldUpdateBattery = false;
                            HandleBattery_PowerValue(HardwareControl.BatteryChargeRate);
                            HandleBattery_CapacityValue(HardwareControl.BatteryCapacity);
                            HandleBattery_TimeValue(HardwareControl.TimeFullInMinutes ?? HardwareControl.TimeLeftInMinutes);
                        }
                    }

                    foreach (IHardware? hardware in computer.Hardware)
                    {
                        if (!ShouldUpdateHardware(hardware, shouldUpdateCpu, shouldUpdateGpu, shouldUpdateMemory, shouldUpdateBattery, shouldUpdateNetwork))
                            continue;

                        try { hardware.Update(); } catch { /* keep going */ }

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
                            case HardwareType.Memory:
                                HandleMemory(hardware);
                                break;
                            case HardwareType.Network when networkInterface != null && hardware.Name.Equals(networkInterface.Name, StringComparison.OrdinalIgnoreCase):
                                HandleNetwork(hardware);
                                break;
                            case HardwareType.Battery:
                                HandleBattery(hardware);
                                break;
                        }
                    }
                }
                catch { }
                finally
                {
                    Monitor.Exit(updateLock);
                }
            }
        }

        private int GetPollingInterval(int minimumInterval)
        {
            return Math.Max(updateInterval, minimumInterval);
        }

        private bool ShouldUpdateHardware(long now, ref long lastUpdateTick, int minimumInterval)
        {
            if (lastUpdateTick != 0 && now - lastUpdateTick < GetPollingInterval(minimumInterval))
                return false;

            lastUpdateTick = now;
            return true;
        }

        private static bool ShouldUpdateHardware(IHardware hardware, bool shouldUpdateCpu, bool shouldUpdateGpu, bool shouldUpdateMemory, bool shouldUpdateBattery, bool shouldUpdateNetwork)
        {
            return hardware.HardwareType switch
            {
                HardwareType.Cpu => shouldUpdateCpu,
                HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel => shouldUpdateGpu,
                HardwareType.Memory => shouldUpdateMemory,
                HardwareType.Battery => shouldUpdateBattery,
                HardwareType.Network => shouldUpdateNetwork,
                _ => false,
            };
        }

        #region gpu updates
        public float? GetGPULoad() => computer?.IsGpuEnabled ?? false ? GPULoad : null;
        public float? GetGPUClock() => computer?.IsCpuEnabled ?? false ? CPUClock : null;
        public float? GetGPUPower() => computer?.IsGpuEnabled ?? false ? GPUPower : null;
        public float? GetGPUTemperature() => computer?.IsGpuEnabled ?? false ? GPUTemperature : null;

        public float? GetGPUMemory() => computer?.IsGpuEnabled ?? false ? GPUMemory : null;
        public float? GetGPUMemoryDedicated() => computer?.IsGpuEnabled ?? false ? GPUMemoryDedicated : null;
        public float? GetGPUMemoryShared() => computer?.IsGpuEnabled ?? false ? GPUMemoryShared : null;

        public float? GetGPUMemoryTotal() => computer?.IsGpuEnabled ?? false ? GPUMemoryTotal : null;
        public float? GetGPUMemoryDedicatedTotal() => computer?.IsGpuEnabled ?? false ? GPUMemoryDedicatedTotal : null;
        public float? GetGPUMemorySharedTotal() => computer?.IsGpuEnabled ?? false ? GPUMemorySharedTotal : null;

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
                        HandleGPU_Temperature(sensor);
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
            float? sensorValue = sensor.Value;
            if (!sensorValue.HasValue)
                return;

            switch (sensor.Name)
            {
                case "GPU Memory Used":
                    {
                        float value = sensorValue.Value / 1024.0f; // MB to GB
                        if (GPUMemory != value)
                        {
                            GPUMemory = value;
                            GPUMemoryChanged?.Invoke(GPUMemory);
                        }

                        break;
                    }

                case "D3D Dedicated Memory Used":
                    {
                        float value = sensorValue.Value / 1024.0f; // MB to GB
                        if (GPUMemoryDedicated != value)
                        {
                            GPUMemoryDedicated = value;
                            GPUMemoryDedicatedChanged?.Invoke(GPUMemoryDedicated);
                        }

                        break;
                    }

                case "D3D Shared Memory Used":
                    {
                        float value = sensorValue.Value / 1024.0f; // MB to GB
                        if (GPUMemoryShared != value)
                        {
                            GPUMemoryShared = value;
                            GPUMemorySharedChanged?.Invoke(GPUMemoryShared);
                        }

                        break;
                    }

                case "GPU Memory Total":
                    {
                        float value = sensorValue.Value / 1024.0f; // MB to GB
                        if (GPUMemoryTotal != value)
                            GPUMemoryTotal = value;
                        break;
                    }

                case "D3D Dedicated Memory Total":
                    {
                        float value = sensorValue.Value / 1024.0f; // MB to GB
                        if (GPUMemoryDedicatedTotal != value)
                            GPUMemoryDedicatedTotal = value;
                        break;
                    }

                case "D3D Shared Memory Total":
                    {
                        float value = sensorValue.Value / 1024.0f; // MB to GB
                        if (GPUMemorySharedTotal != value)
                            GPUMemorySharedTotal = value;
                        break;
                    }
            }
        }

        private void HandleGPU_Load(ISensor sensor)
        {
            float? sensorValue = sensor.Value;
            if (!sensorValue.HasValue)
                return;

            if (sensor.Name == "D3D 3D")
            {
                float value = sensorValue.Value;
                if (GPULoad != value)
                {
                    GPULoad = value;
                    GPULoadChanged?.Invoke(GPULoad);
                }
            }
        }

        private void HandleGPU_Clock(ISensor sensor)
        {
            float? sensorValue = sensor.Value;
            if (!sensorValue.HasValue)
                return;

            if (sensor.Name == "GPU Core")
            {
                float value = sensorValue.Value;
                if (GPUClock != value)
                {
                    GPUClock = value;
                    GPUClockChanged?.Invoke(GPUClock);
                }
            }
        }

        private void HandleGPU_Power(ISensor sensor)
        {
            float? sensorValue = sensor.Value;
            if (!sensorValue.HasValue)
                return;

            switch (sensor.Name)
            {
                case "GPU SoC":
                    //case "GPU Package":
                    {
                        float value = sensorValue.Value;
                        if (GPUPower != value)
                        {
                            GPUPower = value;
                            GPUPowerChanged?.Invoke(GPUPower);
                        }
                    }
                    break;
            }
        }

        private void HandleGPU_Temperature(ISensor sensor)
        {
            float? sensorValue = sensor.Value;
            if (!sensorValue.HasValue)
                return;

            switch (sensor.Name)
            {
                case "GPU Core":
                case "GPU VR SoC":
                    {
                        float value = sensorValue.Value;
                        if (GPUTemperature != value)
                        {
                            GPUTemperature = value;
                            GPUTemperatureChanged?.Invoke(GPUTemperature);
                        }

                        break;
                    }
            }
        }
        #endregion

        #region cpu updates
        public float? GetCPULoad() => computer?.IsCpuEnabled ?? false ? CPULoad : null;
        public float? GetCPULoadMax() => computer?.IsCpuEnabled ?? false ? CPULoadMax : null;
        public float? GetCPUClock() => computer?.IsCpuEnabled ?? false ? CPUClock : null;
        public float? GetCPUClockMax() => computer?.IsCpuEnabled ?? false ? CPUClockMax : null;
        public float? GetCPUPower() => computer?.IsCpuEnabled ?? false ? CPUPower : null;
        public float? GetCPUTemperature() => computer?.IsCpuEnabled ?? false ? CPUTemperature : null;

        private void HandleCPU(IHardware cpu)
        {
            foreach (var sensor in cpu.Sensors)
            {
                // May crash the app when Value is null, better to check first
                if (!sensor.Value.HasValue || sensor.Value == 0)
                    continue;

                switch (sensor.SensorType)
                {
                    case SensorType.Load:
                        HandleCPU_Load(sensor);
                        break;
                    case SensorType.Clock:
                        HandleCPU_Clock(sensor);
                        break;
                    case SensorType.Power:
                        HandleCPU_Power(sensor);
                        break;
                    case SensorType.Temperature:
                        HandleCPU_Temperature(sensor);
                        break;
                }
            }
        }

        private void HandleCPU_Load(ISensor sensor)
        {
            float? sensorValue = sensor.Value;
            if (!sensorValue.HasValue)
                return;

            switch (sensor.Name)
            {
                case "Max CPU Usage":
                    HandleCPU_LoadMaxValue(sensorValue);
                    break;
                case "Total CPU Usage":
                    HandleCPU_LoadValue(sensorValue);
                    break;
            }
        }

        private void HandleCPU_LoadMaxValue(float? sensorValue)
        {
            if (!sensorValue.HasValue) return;
            float value = sensorValue.Value;
            if (CPULoadMax != value)
                CPULoadMax = value;
        }

        private void HandleCPU_LoadValue(float? sensorValue)
        {
            if (!sensorValue.HasValue) return;
            float value = sensorValue.Value;
            if (CPULoad != value)
            {
                CPULoad = value;
                CPULoadChanged?.Invoke(CPULoad);
            }
        }

        private void HandleCPU_Clock(ISensor sensor)
        {
            float? sensorValue = sensor.Value;
            if (!sensorValue.HasValue)
                return;
            switch (sensor.Name)
            {
                case "Max Core Clock":
                    HandleCPU_ClockMaxValue(sensorValue);
                    break;
                case "Average Core Clock":
                    HandleCPU_ClockValue(sensorValue);
                    break;
            }
        }

        private void HandleCPU_ClockMaxValue(float? sensorValue)
        {
            if (!sensorValue.HasValue) return;
            float value = sensorValue.Value;
            if (CPUClockMax != value)
                CPUClockMax = value;
        }

        private void HandleCPU_ClockValue(float? sensorValue)
        {
            if (!sensorValue.HasValue) return;
            float value = sensorValue.Value;
            if (CPUClock != value)
            {
                CPUClock = value;
                CPUClockChanged?.Invoke(CPUClock);
            }
        }

        private void HandleCPU_Power(ISensor sensor)
        {
            float? sensorValue = sensor.Value;
            if (!sensorValue.HasValue)
                return;
            switch (sensor.Name)
            {
                case "Package":
                case "CPU Package":
                    HandleCPU_PowerValue(sensorValue);
                    break;
            }
        }

        private void HandleCPU_PowerValue(float? sensorValue)
        {
            if (!sensorValue.HasValue) return;
            float value = sensorValue.Value;
            if (CPUPower != value)
            {
                CPUPower = value;
                CPUPowerChanged?.Invoke(CPUPower);
            }
        }

        private void HandleCPU_Temperature(ISensor sensor)
        {
            float? sensorValue = sensor.Value;
            if (!sensorValue.HasValue)
                return;

            switch (sensor.Name)
            {
                case "CPU Package":
                case "Core (Tctl/Tdie)":
                    HandleCPU_TemperatureValue(sensorValue);
                    break;
            }
        }

        private void HandleCPU_TemperatureValue(float? sensorValue)
        {
            if (!sensorValue.HasValue) return;
            float value = sensorValue.Value;
            if (CPUTemperature != value)
            {
                CPUTemperature = value;
                CPUTemperatureChanged?.Invoke(CPUTemperature);
            }
        }

        #endregion

        #region memory updates
        public float? GetMemoryUsage() => computer?.IsMemoryEnabled ?? false ? MemoryUsage : null;
        public float? GetMemoryAvailable() => computer?.IsMemoryEnabled ?? false ? MemoryAvailable : null;
        public float? GetMemoryTotal() => GetMemoryUsage() + GetMemoryAvailable();

        private void HandleMemory(IHardware memory)
        {
            // Only read physical RAM; skip VirtualMemory (page file) hardware
            if (memory.Name != "Total Memory")
                return;

            foreach (var sensor in memory.Sensors)
            {
                // May crash the app when Value is null, better to check first
                if (!sensor.Value.HasValue || sensor.Value == 0)
                    continue;

                switch (sensor.SensorType)
                {
                    case SensorType.Data:
                    case SensorType.SmallData:
                        HandleMemory_Data(sensor);
                        break;
                }
            }
        }

        private void HandleMemory_Data(ISensor sensor)
        {
            float? sensorValue = sensor.Value;
            if (!sensorValue.HasValue)
                return;

            switch (sensor.Name)
            {
                case "Memory Used":
                    {
                        float value = sensorValue.Value;
                        if (MemoryUsage != value)
                        {
                            MemoryUsage = value;
                            MemoryUsageChanged?.Invoke(MemoryUsage);
                        }

                        break;
                    }

                case "Memory Available":
                    {
                        float value = sensorValue.Value;
                        if (MemoryAvailable != value)
                        {
                            MemoryAvailable = value;
                            MemoryAvailableChanged?.Invoke(MemoryAvailable);
                        }

                        break;
                    }
            }
        }
        #endregion

        private void HandleNetwork(IHardware network)
        {
            foreach (var sensor in network.Sensors)
            {
                // May crash the app when Value is null, better to check first
                if (!sensor.Value.HasValue || sensor.Value == 0)
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
            float? sensorValue = sensor.Value;
            if (!sensorValue.HasValue)
                return;
            switch (sensor.Name)
            {
                case "Upload Speed":
                    {
                        float value = sensorValue.Value;
                        if (NetworkSpeedUp != value)
                        {
                            NetworkSpeedUp = value;
                            NetworkSpeedUpChanged?.Invoke(NetworkSpeedUp);
                        }
                        break;
                    }
                case "Download Speed":
                    {
                        float value = sensorValue.Value;
                        if (NetworkSpeedDown != value)
                        {
                            NetworkSpeedDown = value;
                            NetworkSpeedDownChanged?.Invoke(NetworkSpeedDown);
                        }
                        break;
                    }
            }
        }

        #region battery updates
        public float? GetBatteryLevel() => computer?.IsBatteryEnabled ?? false ? BatteryLevel : null;
        public float? GetBatteryPower() => computer?.IsBatteryEnabled ?? false ? BatteryPower : null;
        public float? GetBatteryTimeSpan() => computer?.IsBatteryEnabled ?? false ? BatteryTimeSpan : null;
        public float? GetBatteryRemainingCapacity() => computer?.IsBatteryEnabled ?? false ? BatteryRemainingCapacity : null;
        public float? GetBatteryFullCapacity() => computer?.IsBatteryEnabled ?? false ? BatteryFullCapacity : null;

        private void HandleBattery(IHardware cpu)
        {
            foreach (var sensor in cpu.Sensors)
            {
                // May crash the app when Value is null, better to check first
                if (!sensor.Value.HasValue || sensor.Value == 0)
                    continue;

                switch (sensor.SensorType)
                {
                    case SensorType.Level:
                        HandleBattery_Level(sensor);
                        break;
                    case SensorType.Power:
                        HandleBattery_Power(sensor);
                        break;
                    case SensorType.Energy:
                        HandleBattery_Energy(sensor);
                        break;
                    case SensorType.TimeSpan:
                        HandleBattery_TimeSpan(sensor);
                        break;
                }
            }
        }


        private void HandleBattery_Energy(ISensor sensor)
        {
            float? sensorValue = sensor.Value;
            if (!sensorValue.HasValue)
                return;
            switch (sensor.Name)
            {
                case "Designed Capacity":
                    BatteryDesignCapacity = sensorValue;
                    break;
                case "Full-Charged Capacity":
                    BatteryFullCapacity = sensorValue;
                    break;
                case "Remaining Capacity":
                    BatteryRemainingCapacity = sensorValue;
                    break;
            }
        }


        private void HandleBattery_Level(ISensor sensor)
        {
            float? sensorValue = sensor.Value;
            if (!sensorValue.HasValue)
                return;

            if (sensor.Name == "Charge Level")
            {
                HandleBattery_CapacityValue(sensorValue);
            }
        }

        private void HandleBattery_CapacityValue(float? sensorValue)
        {
            if (!sensorValue.HasValue) return;
            float value = sensorValue.Value;
            if (BatteryLevel != value)
            {
                BatteryLevel = value;
                BatteryLevelChanged?.Invoke(BatteryLevel);
            }
        }

        private void HandleBattery_Power(ISensor sensor)
        {
            float? sensorValue = sensor.Value;
            if (!sensorValue.HasValue)
                return;

            switch (sensor.Name)
            {
                case "Charge Rate":
                    HandleBattery_PowerValue(sensorValue);
                    break;
                case "Discharge Rate":
                    HandleBattery_PowerValue(sensorValue * -1f);
                    break;

            }
        }

        private void HandleBattery_PowerValue(float? sensorValue)
        {
            if (!sensorValue.HasValue) return;
            float value = -sensorValue.Value;
            if (BatteryPower != value)
            {
                BatteryPower = value;
                BatteryPowerChanged?.Invoke(BatteryPower);
            }
        }

        private void HandleBattery_TimeSpan(ISensor sensor)
        {
            float? sensorValue = sensor.Value;
            if (!sensorValue.HasValue)
                return;

            if (sensor.Name == "Remaining Time (Estimated)")
            {
                float value = sensorValue.Value / 60.0f;
                if (BatteryTimeSpan != value)
                {
                    BatteryTimeSpan = value;
                    BatteryTimeSpanChanged?.Invoke(BatteryTimeSpan);
                }
            }
        }

        private void HandleBattery_TimeValue(float? sensorValue)
        {
            if (!sensorValue.HasValue) return;
            float value = sensorValue.Value / 60.0f;
            if (BatteryTimeSpan != value)
            {
                BatteryTimeSpan = value;
                BatteryTimeSpanChanged?.Invoke(BatteryTimeSpan);
            }
        }
        #endregion

        #region events
        public delegate void ChangedHandler(float? value);

        public event ChangedHandler? CPULoadChanged;
        public event ChangedHandler? CPUPowerChanged;
        public event ChangedHandler? CPUClockChanged;
        public event ChangedHandler? CPUTemperatureChanged;
        public event ChangedHandler? CPUFanSpeedChanged;

        public event ChangedHandler? GPULoadChanged;
        public event ChangedHandler? GPUPowerChanged;
        public event ChangedHandler? GPUClockChanged;
        public event ChangedHandler? GPUTemperatureChanged;
        public event ChangedHandler? GPUMemoryChanged;
        public event ChangedHandler? GPUMemoryDedicatedChanged;
        public event ChangedHandler? GPUMemorySharedChanged;

        public event ChangedHandler? MemoryUsageChanged;
        public event ChangedHandler? MemoryAvailableChanged;

        public event ChangedHandler? NetworkSpeedUpChanged;
        public event ChangedHandler? NetworkSpeedDownChanged;

        public event ChangedHandler? BatteryLevelChanged;
        public event ChangedHandler? BatteryPowerChanged;
        public event ChangedHandler? BatteryTimeSpanChanged;
        #endregion
    }
}