using HandheldCompanion.Devices;
using iNKORE.UI.WPF.Converters;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Windows.Devices.Power;

namespace HandheldCompanion.Misc;

public static class HardwareControl
{
    private static float? batteryFullCapacity;
    private static float? batteryDesignCapacity;
    private static float? batteryRemainingCapacity;
    private static float? batteryChargeRate;
    private static float? batteryCapacity;
    private static float? batteryHealth;

    private static float? timeLeftInMinutes;
    private static float? timeFullInMinutes;

    private static float? cpuPower;
    private static float? cpuTemp;
    private static float? cpuUsage;
    private static float? cpuClock;
    private static float? cpuFanRPM;

    private static PerformanceCounter? _cpuTempCounter;
    private static readonly string[] _tempCounterInstances = [@"\_TZ.TZ01", @"\_TZ.THRM"];

    private static PerformanceCounter? _cpuPowerCounter;
    private static readonly string[] _powerCounterInstances = ["Apu Power", "RAPL_Package0_PKG", "CPU Power", "Socket Power", "Current Socket Power"];

    private static PerformanceCounter? _cpuUsageCounter;
    private static readonly string[] _usageCounterInstances = ["_Total", "0,_Total"];

    private static PerformanceCounter? _cpuClockCounter;
    private static readonly string[] _clockCounterInstances = ["_Total", "0,_Total"];

    public static float? BatteryFullCapacity => batteryFullCapacity;
    public static float? BatteryDesignCapacity => batteryDesignCapacity;
    public static float? BatteryRemainingCapacity => batteryRemainingCapacity;
    public static float? BatteryChargeRate => batteryChargeRate;
    public static float? BatteryCapacity => batteryCapacity;
    public static float? BatteryHealth => batteryHealth;
    public static float? TimeFullInMinutes => timeFullInMinutes;
    public static float? TimeLeftInMinutes => timeLeftInMinutes;

    public static void ReadBatteryState()
    {
        batteryChargeRate = null;
        batteryRemainingCapacity = 0;
        timeFullInMinutes = null;
        timeLeftInMinutes = null;
        batteryHealth = null;

        var report = Battery.AggregateBattery.GetReport();
        if (batteryFullCapacity is null or 0 && report.FullChargeCapacityInMilliwattHours > 0)
            batteryFullCapacity = (float)Math.Round((float)report.FullChargeCapacityInMilliwattHours / 1000.0f, 1);

        if (batteryDesignCapacity != 0 && report.DesignCapacityInMilliwattHours > 0)
            batteryDesignCapacity = (float)Math.Round((float)report.DesignCapacityInMilliwattHours / 1000.0f, 1);


        if (report.RemainingCapacityInMilliwattHours.HasValue)
            batteryRemainingCapacity = (float)Math.Round((float)report.RemainingCapacityInMilliwattHours / 1000.0f, 1);

        if (report.ChargeRateInMilliwatts.HasValue && report.ChargeRateInMilliwatts != 0)
            batteryChargeRate = (float)Math.Round((float)report.ChargeRateInMilliwatts / 1000.0f, 1);

        FormatBatteryCharge();
        //LogManager.LogInformation($"Design Capacity: {BatteryDesignCapacity}Wh,  Remaining Capacity: {BatteryRemainingCapacity}Wh,  Full Charge Capacity: {BatteryFullCapacity}Wh,  Charge/Discharge: {BatteryChargeRate}W, Time left: {TimeLeftInMinutes}mins,  Time full: {TimeFullInMinutes}mins  Health: {BatteryHealth}%");
    }

    private static void FormatBatteryCharge()
    {
        if (batteryFullCapacity > 0 && batteryRemainingCapacity > 0)
            batteryCapacity = Math.Min(100f, (float)Math.Round((float)batteryRemainingCapacity / (float)batteryFullCapacity * 100f, 1));

        if (batteryFullCapacity > 0 && batteryDesignCapacity > 0)
            batteryHealth = (float)Math.Round((float)batteryFullCapacity / (float)batteryDesignCapacity * 100f, 1);

        if (batteryChargeRate > 0)
            timeFullInMinutes = (batteryFullCapacity - batteryRemainingCapacity) / batteryChargeRate * 60f;

        if (batteryChargeRate < 0)
            timeLeftInMinutes = batteryRemainingCapacity / batteryChargeRate * 60f * -1;
    }

    public static void RefreshBatteryHealth()
    {
        batteryFullCapacity = null;
        ReadBatteryState();
    }

    private static bool _cpuInitStarted;

    private static bool _cpuUsageCounterFailed;
    private static int _cpuUsageReadErrors;
    private static int _cpuUsageNullTicks;

    private static bool _cpuClockCounterFailed;
    private static int _cpuClockReadErrors;
    private static int _cpuClockNullTicks;

    private static bool _cpuTempCounterFailed;
    private static int _cpuTempReadErrors;
    private static int _cpuTempNullTicks;
    private static bool _cpuPowerCounterFailed;
    private static int _cpuPowerReadErrors;
    private static int _cpuPowerNullTicks;
    private const int CpuPowerMaxReadErrors = 3;

    public static float? CPUPower => cpuPower;
    public static float? CPUTemp => cpuTemp;
    public static float? CPUUsage => cpuUsage;
    public static float? CPUClock => cpuClock;
    public static float? CPUFanRPM => cpuFanRPM;


    public static void InitCPUAsync()
    {
        if (_cpuInitStarted) return;
        _cpuInitStarted = true;
        Task.Run(() =>
        {
            InitCPUClock();
            InitCPUPower();
            InitCPUUsage();
            InitCPUTemp();
        });
    }
    public static void ReadCPUSensors()
    {
        InitCPUAsync();

        cpuUsage = GetCPUUsage();
        cpuClock = GetCPUClock();
        cpuTemp = GetCPUTemp();
        cpuPower = GetCPUPower();
        cpuFanRPM = GetCPUFanRPM();
    }

    private static void InitCPUTemp()
    {
        if (_cpuTempCounter is not null) return;

        try
        {
            var category = new PerformanceCounterCategory("Thermal Zone Information");
            var instances = category.GetInstanceNames();

            foreach (var name in _tempCounterInstances)
            {
                if (instances.Contains(name, StringComparer.OrdinalIgnoreCase))
                {
                    var counter = new PerformanceCounter("Thermal Zone Information", "Temperature", name, true);
                    counter.NextValue();
                    _cpuTempCounter = counter;
                    return;
                }
            }
            _cpuTempCounterFailed = true;
        }
        catch
        {
            _cpuTempCounterFailed = true;
        }

    }
    private static void InitCPUPower()
    {
        if (_cpuPowerCounter is not null) return;

        try
        {
            var category = new PerformanceCounterCategory("Energy Meter");
            var instances = category.GetInstanceNames();

            foreach (var name in _powerCounterInstances)
            {
                if (instances.Contains(name, StringComparer.OrdinalIgnoreCase))
                {
                    var counter = new PerformanceCounter("Energy Meter", "Power", name, true);
                    counter.NextValue();
                    _cpuPowerCounter = counter;
                    return;
                }
            }
            _cpuPowerCounterFailed = true;
        }
        catch
        {
            _cpuPowerCounterFailed = true;
        }
    }

    private static void InitCPUClock()
    {
        if (_cpuClockCounter is not null) return;

        var category = new PerformanceCounterCategory("Processor Information");
        var instances = category.GetInstanceNames();

        try
        {
            foreach (var name in _clockCounterInstances)
            {
                if (instances.Contains(name, StringComparer.OrdinalIgnoreCase))
                {
                    var counter = new PerformanceCounter("Processor Information", "Actual Frequency", name, true);
                    counter.NextValue();
                    _cpuClockCounter = counter;
                    return;
                }
            }
            _cpuClockCounterFailed = true;
        }
        catch
        {
            _cpuClockCounterFailed = true;
        }
        return;
    }

    private static void InitCPUUsage()
    {
        if (_cpuUsageCounter is not null) return;

        try
        {
            var category = new PerformanceCounterCategory("Processor Information");
            var instances = category.GetInstanceNames();

            foreach (var name in _usageCounterInstances)
            {
                if (instances.Contains(name, StringComparer.OrdinalIgnoreCase))
                {
                    var counter = new PerformanceCounter("Processor Information", "% Processor Utility", name, true);
                    counter.NextValue();
                    _cpuUsageCounter = counter;
                    return;
                }
            }
            _cpuUsageCounterFailed = true;
        }

        catch
        {
            _cpuUsageCounterFailed = true;
        }

    }

    public static float? GetCPUUsage()
    {
        if (_cpuUsageCounterFailed || _cpuUsageCounter is null) return null;
        try
        {
            var newCpu = _cpuUsageCounter.NextValue();
            if (newCpu > 0)
            {
                _cpuUsageNullTicks = 0;
                return newCpu;
            }

            if (++_cpuUsageNullTicks >= 5)
                return null;
            return cpuUsage;
        }
        catch
        {
            // Counter became invalid (e.g. after a fullscreen game exits on Intel).
            // Allow a few re-init attempts, then give up so we don't spawn a Task.Run
            // every second forever on machines where the counter is permanently broken.
            // ResetCPUPowerCounter() on overlay toggle gives it another fresh chance.
            _cpuUsageCounter?.Dispose();
            _cpuUsageCounter = null;
            if (++_cpuUsageReadErrors >= CpuPowerMaxReadErrors)
            {
                _cpuUsageCounterFailed = true;
            }
            else
            {
                _cpuUsageCounterFailed = false;
                _cpuInitStarted = false;
            }
        }
        if (++_cpuUsageNullTicks >= 5)
            return null;
        return cpuUsage;
    }

    public static float? GetCPUClock()
    {
        if (_cpuClockCounterFailed || _cpuClockCounter is null) return null;
        try
        {
            var newCpu = _cpuClockCounter.NextValue();
            if (newCpu > 0)
            {
                _cpuClockNullTicks = 0;
                return newCpu;
            }

            if (++_cpuClockNullTicks >= 5)
                return null;
            return cpuClock;
        }
        catch
        {
            // Counter became invalid (e.g. after a fullscreen game exits on Intel).
            // Allow a few re-init attempts, then give up so we don't spawn a Task.Run
            // every second forever on machines where the counter is permanently broken.
            // ResetCPUPowerCounter() on overlay toggle gives it another fresh chance.
            _cpuClockCounter?.Dispose();
            _cpuClockCounter = null;
            if (++_cpuClockReadErrors >= CpuPowerMaxReadErrors)
            {
                _cpuClockCounterFailed = true;
            }
            else
            {
                _cpuClockCounterFailed = false;
                _cpuInitStarted = false;
            }
        }

        if (++_cpuClockNullTicks >= 5)
            return null;
        return cpuClock;
    }

    public static float? GetCPUPower()
    {
        if (_cpuPowerCounterFailed || _cpuPowerCounter is null) return null;
        try
        {
            var newCpu = (float)Math.Round((_cpuPowerCounter.NextValue()) / 1000f, 1);
            if (newCpu > 0)
            {
                _cpuPowerNullTicks = 0;
                return newCpu;
            }

            if (++_cpuPowerNullTicks >= 5)
                return null;
            return cpuPower;
        }
        catch
        {
            // Counter became invalid (e.g. after a fullscreen game exits on Intel).
            // Allow a few re-init attempts, then give up so we don't spawn a Task.Run
            // every second forever on machines where the counter is permanently broken.
            // ResetCPUPowerCounter() on overlay toggle gives it another fresh chance.
            _cpuPowerCounter?.Dispose();
            _cpuPowerCounter = null;
            if (++_cpuPowerReadErrors >= CpuPowerMaxReadErrors)
            {
                _cpuPowerCounterFailed = true;
            }
            else
            {
                _cpuPowerCounterFailed = false;
                _cpuInitStarted = false;
            }
        }

        if (++_cpuPowerNullTicks >= 5)
            return null;
        return cpuPower;
    }

    public static float? GetCPUTemp()
    {
        if (_cpuTempCounterFailed || _cpuTempCounter is null) return null;
        try
        {
            var newCpu = _cpuTempCounter.NextValue() - 273.15f;
            if (newCpu > 0)
            {
                _cpuTempNullTicks = 0;
                return newCpu;
            }

            if (++_cpuTempNullTicks >= 5)
                return null;
            return cpuTemp;
        }
        catch
        {
            // Counter became invalid (e.g. after a fullscreen game exits on Intel).
            // Allow a few re-init attempts, then give up so we don't spawn a Task.Run
            // every second forever on machines where the counter is permanently broken.
            // ResetCPUPowerCounter() on overlay toggle gives it another fresh chance.
            _cpuTempCounter?.Dispose();
            _cpuTempCounter = null;
            if (++_cpuTempReadErrors >= CpuPowerMaxReadErrors)
            {
                _cpuTempCounterFailed = true;
            }
            else
            {
                _cpuTempCounterFailed = false;
                _cpuInitStarted = false;
            }
        }
        if (++_cpuTempNullTicks >= 5)
            return null;
        return cpuTemp;
    }

    public static float? GetCPUFanRPM()
    {
        if (IDevice.GetCurrent().ECDetails.AddressFanRPM == 0) return null;
        try
        {
            return IDevice.GetCurrent().ReadFanSpeed();
        }
        catch { }
        return null;
    }

    public static void Dispose()
    {
        _cpuTempReadErrors = 0;
        _cpuTempCounterFailed = false;

        _cpuTempCounter?.Dispose();
        _cpuTempCounter = null;


        _cpuPowerReadErrors = 0;
        _cpuPowerCounterFailed = false;

        _cpuPowerCounter?.Dispose();
        _cpuPowerCounter = null;

        _cpuClockReadErrors = 0;
        _cpuClockCounterFailed = false;

        _cpuClockCounter?.Dispose();
        _cpuClockCounter = null;

        _cpuUsageReadErrors = 0;
        _cpuUsageCounterFailed = false;

        _cpuUsageCounter?.Dispose();
        _cpuUsageCounter = null;

        _cpuInitStarted = false;
    }
}
