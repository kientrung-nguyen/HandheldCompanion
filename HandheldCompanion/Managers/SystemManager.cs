using HandheldCompanion.Misc;
using HandheldCompanion.Utils;
using HandheldCompanion.Views.Windows;
using Microsoft.Win32;
using Sentry;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using SystemPowerManager = Windows.System.Power.PowerManager;

namespace HandheldCompanion.Managers;

public static class SystemManager
{
    // Import SetThreadExecutionState Win32 API and define flags
    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern uint SetThreadExecutionState(uint esFlags);

    private static CrossThreadLock autoLock = new();
    public const uint ES_CONTINUOUS = 0x80000000;
    public const uint ES_SYSTEM_REQUIRED = 0x00000001;
    static long lastAuto;

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
        SystemEvents.PowerModeChanged += SystemEvents_PowerModeChanged;
        SystemEvents.SessionSwitch += SystemEvents_SessionSwitch;

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
    private static void BatteryStatusChanged(object? sender, object e)
    {
        PowerStatusChanged?.Invoke(SystemInformation.PowerStatus);
        AutoRoutine();
    }

    public static void Start()
    {
        // check if current session is locked
        var handle = OpenInputDesktop(0, false, 0);
        IsSessionLocked = handle == IntPtr.Zero;
        isPlugged = SystemInformation.PowerStatus.PowerLineStatus;
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
        SystemEvents.PowerModeChanged -= SystemEvents_PowerModeChanged;
        SystemEvents.SessionSwitch -= SystemEvents_SessionSwitch;

        LogManager.LogInformation("{0} has stopped", "PowerManager");
    }

    private static void SystemEvents_PowerModeChanged(object s, PowerModeChangedEventArgs e)
    {
        switch (e.Mode)
        {
            case PowerModes.Resume:
                IsPowerSuspended = false;
                LogManager.LogDebug("System is trying to resume. Performing tasks...");
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
                break;
        }

        LogManager.LogDebug("Device power mode set to {0}", e.Mode);
        SystemRoutine();
    }

    private static void SystemEvents_SessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        switch (e.Reason)
        {
            case SessionSwitchReason.SessionUnlock:
            case SessionSwitchReason.SessionLogon:
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
        {
            currentSystemStatus = SystemStatus.SystemReady;
            AutoRoutine();
        }
        else
            currentSystemStatus = SystemStatus.SystemPending;

        if (previousSystemStatus == currentSystemStatus)
            return;

        // only raise event is system status has changed
        LogManager.LogInformation("System status set to {0}", currentSystemStatus);
        SystemStatusChanged?.Invoke(currentSystemStatus, previousSystemStatus);
        previousSystemStatus = currentSystemStatus;
    }

    static void AutoRoutine()
    {
        if (Math.Abs(DateTimeOffset.Now.ToUnixTimeMilliseconds() - lastAuto) < 3000) return;
        lastAuto = DateTimeOffset.Now.ToUnixTimeMilliseconds();

        if (currentSystemStatus != SystemStatus.SystemReady)
            return;

        if (autoLock.TryEnter())
        {
            try
            {
                NightLight.Auto();
                ScreenControl.Auto();
                if (isPlugged != SystemInformation.PowerStatus.PowerLineStatus)
                {
                    isPlugged = SystemInformation.PowerStatus.PowerLineStatus;
                    var currentProfile = ProfileManager.GetCurrent();
                    var powerProfile = PowerProfileManager.GetProfile(isPlugged == PowerLineStatus.Online
                            ? currentProfile.PowerProfile
                            : currentProfile.BatteryProfile);
                    ProfileManager.UpdateOrCreateProfile(currentProfile, UpdateSource.Background);
                    ToastManager.RunToast($"{powerProfile.Name}",
                        SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Online
                        ? ToastIcons.Charger
                        : ToastIcons.Battery);
                }
            }
            finally
            {
                autoLock.Exit();
            }
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