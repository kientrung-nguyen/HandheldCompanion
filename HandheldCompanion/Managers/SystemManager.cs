using Microsoft.Win32;
using Microsoft.WindowsAPICodePack.ApplicationServices;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using SystemPowerManager = Windows.System.Power.PowerManager;

namespace HandheldCompanion.Managers;

public static class SystemManager
{
    // Import SetThreadExecutionState Win32 API and define flags
    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern uint SetThreadExecutionState(uint esFlags);

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

    private static bool isInitialized;

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
        { "BatterySaver10", "\uEA95" }
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

    #region import

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr OpenInputDesktop(uint dwFlags, bool fInherit, uint dwDesiredAccess);

    #endregion


    public static decimal? batteryRate = 0;
    public static decimal batteryHealth = -1;
    public static decimal batteryCapacity = -1;

    public static decimal? designCapacity;
    public static decimal? fullCapacity;
    public static decimal? chargeCapacity;

    public static void ReadBatterySensors()
    {
        ReadFullChargeCapacity();
        GetBatteryStatus();

        if (fullCapacity > 0 && chargeCapacity > 0)
        {
            batteryCapacity = Math.Min(100, ((decimal)chargeCapacity / (decimal)fullCapacity) * 100);
        }
    }

    public static void GetBatteryStatus()
    {
        batteryRate = 0;
        chargeCapacity = 0;

        try
        {
            ManagementScope scope = new ManagementScope("root\\WMI");
            ObjectQuery query = new ObjectQuery("SELECT * FROM BatteryStatus");

            using ManagementObjectSearcher searcher = new ManagementObjectSearcher(scope, query);
            foreach (ManagementObject obj in searcher.Get().Cast<ManagementObject>())
            {
                chargeCapacity = Convert.ToDecimal(obj["RemainingCapacity"]);
                decimal chargeRate = Convert.ToDecimal(obj["ChargeRate"]);
                decimal dischargeRate = Convert.ToDecimal(obj["DischargeRate"]);

                if (chargeRate > 0)
                    batteryRate = chargeRate / 1000;
                else
                    batteryRate = -dischargeRate / 1000;
            }

        }
        catch (Exception ex)
        {
            LogManager.LogError("Discharge Reading: " + ex.Message);
        }

    }
    public static void ReadFullChargeCapacity()
    {
        if (fullCapacity > 0) return;

        try
        {
            ManagementScope scope = new ManagementScope("root\\WMI");
            ObjectQuery query = new ObjectQuery("SELECT * FROM BatteryFullChargedCapacity");

            using ManagementObjectSearcher searcher = new ManagementObjectSearcher(scope, query);
            foreach (ManagementObject obj in searcher.Get().Cast<ManagementObject>())
            {
                fullCapacity = Convert.ToDecimal(obj["FullChargedCapacity"]);
            }

        }
        catch (Exception ex)
        {
            LogManager.LogError("Full Charge Reading: " + ex.Message);
        }

    }

    public static void ReadDesignCapacity()
    {
        if (designCapacity > 0) return;

        try
        {
            ManagementScope scope = new ManagementScope("root\\WMI");
            ObjectQuery query = new ObjectQuery("SELECT * FROM BatteryStaticData");

            using ManagementObjectSearcher searcher = new ManagementObjectSearcher(scope, query);
            foreach (ManagementObject obj in searcher.Get().Cast<ManagementObject>())
            {
                designCapacity = Convert.ToDecimal(obj["DesignedCapacity"]);
            }

        }
        catch (Exception ex)
        {
            LogManager.LogError("Design Capacity Reading: " + ex.Message);
        }
    }

    public static void RefreshBatteryHealth()
    {
        batteryHealth = GetBatteryHealth() * 100;
    }


    public static decimal GetBatteryHealth()
    {
        if (designCapacity is null)
        {
            ReadDesignCapacity();
        }
        ReadFullChargeCapacity();

        if (designCapacity is null || fullCapacity is null || designCapacity == 0 || fullCapacity == 0)
        {
            return -1;
        }

        decimal health = (decimal)fullCapacity / (decimal)designCapacity;
        LogManager.LogInformation("Design Capacity: " + designCapacity + "mWh, Full Charge Capacity: " + fullCapacity + "mWh, Health: " + health + "%");

        return health;
    }

    private static void BatteryStatusChanged(object sender, object e)
    {
        PowerStatusChanged?.Invoke(SystemInformation.PowerStatus);
    }

    public static void Start()
    {
        // check if current session is locked
        var handle = OpenInputDesktop(0, false, 0);
        IsSessionLocked = handle == IntPtr.Zero;

        SystemRoutine();

        isInitialized = true;
        Initialized?.Invoke();

        PowerStatusChanged?.Invoke(SystemInformation.PowerStatus);

        LogManager.LogInformation("{0} has started", "PowerManager");
    }

    public static void Stop()
    {
        if (!isInitialized)
            return;

        isInitialized = false;

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

        LogManager.LogDebug("Device power mode set to {0}", e.Mode);

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

        LogManager.LogDebug("Session switched to {0}", e.Reason);

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