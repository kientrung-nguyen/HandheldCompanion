using HandheldCompanion.Views.Windows;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using SystemPowerManager = Windows.System.Power.PowerManager;

namespace HandheldCompanion.Managers;

public static class SystemManager
{

    #region import

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr OpenInputDesktop(uint dwFlags, bool fInherit, uint dwDesiredAccess);
    // Import SetThreadExecutionState Win32 API and define flags
    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern uint SetThreadExecutionState(uint esFlags);

    #endregion


    public const uint ES_CONTINUOUS = 0x80000000;
    public const uint ES_SYSTEM_REQUIRED = 0x00000001;

    public enum SystemStatus
    {
        SystemBooting = 0,
        SystemPending = 1,
        SystemReady = 2
    }

    private static bool IsPowerSuspended;
    private static bool IsSessionLocked = true;

    private static SystemStatus currentSystemStatus = SystemStatus.SystemBooting;
    private static SystemStatus previousSystemStatus = SystemStatus.SystemBooting;

    private static PowerLineStatus isPlugged = SystemInformation.PowerStatus.PowerLineStatus;

    private static bool IsInitialized;

    public static readonly SortedDictionary<string, string> PowerStatusIcon = new()
    {
        { "Battery0", "\uE850" },
        { "Battery1", "\uE851" },
        { "Battery2", "\uE852" },
        { "Battery3", "\uE853" },
        { "Battery4", "\uE854" },
        { "Battery5", "\uE855" },
        { "Battery6", "\uE856" },
        { "Battery7", "\uE857" },
        { "Battery8", "\uE858" },
        { "Battery9", "\uE859" },
        { "Battery10", "\uE83F" },

        { "BatteryCharging0", "\uE85A" },
        { "BatteryCharging1", "\uE85B" },
        { "BatteryCharging2", "\uE85C" },
        { "BatteryCharging3", "\uE85D" },
        { "BatteryCharging4", "\uE85E" },
        { "BatteryCharging5", "\uE85F" },
        { "BatteryCharging6", "\uE860" },
        { "BatteryCharging7", "\uE861" },
        { "BatteryCharging8", "\uE862" },
        { "BatteryCharging9", "\uE83E" },
        { "BatteryCharging10", "\uEA93" },

        { "BatterySaver0", "\uE863" },
        { "BatterySaver1", "\uE864" },
        { "BatterySaver2", "\uE865" },
        { "BatterySaver3", "\uE866" },
        { "BatterySaver4", "\uE867" },
        { "BatterySaver5", "\uE868" },
        { "BatterySaver6", "\uE869" },
        { "BatterySaver7", "\uE86A" },
        { "BatterySaver8", "\uE86B" },
        { "BatterySaver9", "\uEA94" },
        { "BatterySaver10", "\uEA95" },

        { "VerticalBattery0", "\uf5f2" },
        { "VerticalBattery1", "\uf5f3" },
        { "VerticalBattery2", "\uf5f4" },
        { "VerticalBattery3", "\uf5f5" },
        { "VerticalBattery4", "\uf5f6" },
        { "VerticalBattery5", "\uf5f7" },
        { "VerticalBattery6", "\uf5f8" },
        { "VerticalBattery7", "\uf5f9" },
        { "VerticalBattery8", "\uf5fa" },
        { "VerticalBattery9", "\uf5fb" },
        { "VerticalBattery10", "\uEA9c" }
    };

    static SystemManager()
    {
        // listen to system events
        SystemEvents.PowerModeChanged += OnPowerChange;
        SystemEvents.SessionSwitch += OnSessionSwitch;

        SystemPowerManager.BatteryStatusChanged += BatteryStatusChanged;
        SystemPowerManager.EnergySaverStatusChanged += BatteryStatusChanged;
        SystemPowerManager.PowerSupplyStatusChanged += BatteryStatusChanged;
        SystemPowerManager.RemainingChargePercentChanged += BatteryStatusChanged;
        SystemPowerManager.RemainingDischargeTimeChanged += BatteryStatusChanged;
    }


    private static void BatteryStatusChanged(object sender, object e)
    {
        PowerStatusChanged?.Invoke(SystemInformation.PowerStatus);
        if (isPlugged != SystemInformation.PowerStatus.PowerLineStatus)
        {
            var currentProfile = ProfileManager.GetCurrent();
            ToastManager.RunToast($"{currentProfile.Name}", SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Online ? ToastIcons.Charger : ToastIcons.Battery);
            isPlugged = SystemInformation.PowerStatus.PowerLineStatus;
        }
    }

    public static void Start()
    {
        // check if current session is locked
        var handle = OpenInputDesktop(0, false, 0);
        IsSessionLocked = handle == IntPtr.Zero;

        SystemRoutine();

        IsInitialized = true;
        Initialized?.Invoke();

        PowerStatusChanged?.Invoke(SystemInformation.PowerStatus);

        LogManager.LogInformation("{0} has started", "PowerManager");
    }

    public static void Stop()
    {
        if (!IsInitialized)
            return;

        IsInitialized = false;

        // stop listening to system events
        SystemEvents.PowerModeChanged -= OnPowerChange;
        SystemEvents.SessionSwitch -= OnSessionSwitch;

        LogManager.LogInformation("{0} has stopped", "PowerManager");
    }

    private static void OnPowerChange(object s, PowerModeChangedEventArgs e)
    {
        switch (e.Mode)
        {
            case PowerModes.Resume:
                IsPowerSuspended = false;
                break;
            case PowerModes.Suspend:
                IsPowerSuspended = true;

                // Prevent system sleep
                SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED);
                LogManager.LogDebug("System is trying to suspend. Performing tasks...");
                break;
            default:
            case PowerModes.StatusChange:
                PowerStatusChanged?.Invoke(SystemInformation.PowerStatus);
                return;
        }

        string cpuTemp = "";
        string gpuTemp = "";
        string battery = "";

        if (PlatformManager.LibreHardwareMonitor.CPUPower != null)
            cpuTemp += $": {Math.Round((decimal)PlatformManager.LibreHardwareMonitor.CPUPower.Value, 1):0.0}W";

        if (PlatformManager.LibreHardwareMonitor.CPUTemp != null)
            cpuTemp += $" {Math.Round((decimal)PlatformManager.LibreHardwareMonitor.CPUTemp.Value)}°C";

        if (PlatformManager.LibreHardwareMonitor.CPULoad != null)
            cpuTemp += $" {Math.Round((decimal)PlatformManager.LibreHardwareMonitor.CPULoad.Value)}%";

        if (PlatformManager.LibreHardwareMonitor.MemoryUsage != null)
            cpuTemp += $" {Math.Round((decimal)PlatformManager.LibreHardwareMonitor.MemoryUsage.Value / 1024, 1)}GB";

        if (PlatformManager.LibreHardwareMonitor.GPUPower != null)
            gpuTemp += $": {Math.Round((decimal)PlatformManager.LibreHardwareMonitor.GPUPower.Value, 1):0.0}W";

        if (PlatformManager.LibreHardwareMonitor.GPUTemp != null)
            gpuTemp += $" {Math.Round((decimal)PlatformManager.LibreHardwareMonitor.GPUTemp.Value)}°C";

        if (PlatformManager.LibreHardwareMonitor.GPULoad != null)
            gpuTemp += $" {Math.Round((decimal)PlatformManager.LibreHardwareMonitor.GPULoad.Value)}%";

        if (PlatformManager.LibreHardwareMonitor.GPUMemoryUsage != null)
            gpuTemp += $" {Math.Round((decimal)PlatformManager.LibreHardwareMonitor.GPUMemoryUsage.Value / 1024, 1)}GB";

        if (PlatformManager.LibreHardwareMonitor.BatteryCapacity > 0)
            battery = $": {Math.Round((decimal)PlatformManager.LibreHardwareMonitor.BatteryCapacity)}%";

        if (PlatformManager.LibreHardwareMonitor.BatteryPower < 0)
            battery += $" ({Math.Round((decimal)PlatformManager.LibreHardwareMonitor.BatteryPower, 1)}W)";
        else if (PlatformManager.LibreHardwareMonitor.BatteryPower > 0)
            battery += $" ({Math.Round((decimal)PlatformManager.LibreHardwareMonitor.BatteryPower, 1)}W)";

        if (PlatformManager.LibreHardwareMonitor.BatteryHealth > 0)
            battery += $" {Properties.Resources.BatteryHealth}: {Math.Round((decimal)PlatformManager.LibreHardwareMonitor.BatteryHealth, 1)}%";

        string trayTip = $"CPU{cpuTemp}";
        if (gpuTemp.Length > 0) trayTip += "; GPU" + gpuTemp;
        if (PlatformManager.LibreHardwareMonitor.CPUFanSpeed != null) trayTip += $"; FAN: {PlatformManager.LibreHardwareMonitor.CPUFanSpeed}RPM";
        if (battery.Length > 0) trayTip += "; BAT" + battery;
        LogManager.LogDebug("Device power mode set to {0} {1}", e.Mode, trayTip);

        SystemRoutine();
    }

    private static void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        switch (e.Reason)
        {
            case SessionSwitchReason.SessionUnlock:
                IsSessionLocked = false;
                break;
            case SessionSwitchReason.SessionLock:
                IsSessionLocked = true;
                break;
            default:
                return;
        }
        string cpuTemp = "";
        string gpuTemp = "";
        string battery = "";

        if (PlatformManager.LibreHardwareMonitor.CPUPower != null)
            cpuTemp += $": {Math.Round((decimal)PlatformManager.LibreHardwareMonitor.CPUPower.Value, 1):0.0}W";

        if (PlatformManager.LibreHardwareMonitor.CPUTemp != null)
            cpuTemp += $" {Math.Round((decimal)PlatformManager.LibreHardwareMonitor.CPUTemp.Value)}°C";

        if (PlatformManager.LibreHardwareMonitor.CPULoad != null)
            cpuTemp += $" {Math.Round((decimal)PlatformManager.LibreHardwareMonitor.CPULoad.Value)}%";

        if (PlatformManager.LibreHardwareMonitor.MemoryUsage != null)
            cpuTemp += $" {Math.Round((decimal)PlatformManager.LibreHardwareMonitor.MemoryUsage.Value / 1024, 1)}GB";

        if (PlatformManager.LibreHardwareMonitor.GPUPower != null)
            gpuTemp += $": {Math.Round((decimal)PlatformManager.LibreHardwareMonitor.GPUPower.Value, 1):0.0}W";

        if (PlatformManager.LibreHardwareMonitor.GPUTemp != null)
            gpuTemp += $" {Math.Round((decimal)PlatformManager.LibreHardwareMonitor.GPUTemp.Value)}°C";

        if (PlatformManager.LibreHardwareMonitor.GPULoad != null)
            gpuTemp += $" {Math.Round((decimal)PlatformManager.LibreHardwareMonitor.GPULoad.Value)}%";

        if (PlatformManager.LibreHardwareMonitor.GPUMemoryUsage != null)
            gpuTemp += $" {Math.Round((decimal)PlatformManager.LibreHardwareMonitor.GPUMemoryUsage.Value / 1024, 1)}GB";

        if (PlatformManager.LibreHardwareMonitor.BatteryCapacity > 0)
            battery = $": {Math.Round((decimal)PlatformManager.LibreHardwareMonitor.BatteryCapacity)}%";

        if (PlatformManager.LibreHardwareMonitor.BatteryPower < 0)
            battery += $" ({Math.Round((decimal)PlatformManager.LibreHardwareMonitor.BatteryPower, 1)}W)";
        else if (PlatformManager.LibreHardwareMonitor.BatteryPower > 0)
            battery += $" ({Math.Round((decimal)PlatformManager.LibreHardwareMonitor.BatteryPower, 1)}W)";

        if (PlatformManager.LibreHardwareMonitor.BatteryHealth > 0)
            battery += $" {Properties.Resources.BatteryHealth}: {Math.Round((decimal)PlatformManager.LibreHardwareMonitor.BatteryHealth, 1)}%";

        string trayTip = $"CPU{cpuTemp}";
        if (gpuTemp.Length > 0) trayTip += "; GPU" + gpuTemp;
        if (PlatformManager.LibreHardwareMonitor.CPUFanSpeed != null) trayTip += $"; FAN: {PlatformManager.LibreHardwareMonitor.CPUFanSpeed}RPM";
        if (battery.Length > 0) trayTip += "; BAT" + battery;
        LogManager.LogDebug("Session switched to {0} {1}", e.Reason, trayTip);

        SystemRoutine();
    }

    private static void SystemRoutine()
    {
        if (!IsPowerSuspended && !IsSessionLocked)
            currentSystemStatus = SystemStatus.SystemReady;
        else
            currentSystemStatus = SystemStatus.SystemPending;

        // only raise event is system status has changed
        if (previousSystemStatus != currentSystemStatus)
        {
            LogManager.LogInformation("System status set to {0}", currentSystemStatus);
            SystemStatusChanged?.Invoke(currentSystemStatus, previousSystemStatus);

            previousSystemStatus = currentSystemStatus;
        }
    }

    #region events

    public static event SystemStatusChangedEventHandler SystemStatusChanged;

    public delegate void SystemStatusChangedEventHandler(SystemStatus status, SystemStatus prevStatus);

    public static event PowerStatusChangedEventHandler PowerStatusChanged;

    public delegate void PowerStatusChangedEventHandler(PowerStatus status);

    public static event InitializedEventHandler Initialized;

    public delegate void InitializedEventHandler();

    #endregion
}