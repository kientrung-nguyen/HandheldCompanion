using HandheldCompanion.Misc;
using HandheldCompanion.Shared;
using System;
using System.Windows.Forms;
using Windows.Devices.Power;

namespace HandheldCompanion.Managers.Overlay.Widget;

public class BatteryWidget : IWidget
{

    public void Build(OverlayEntry entry, short? level = null)
    {
        short _level = level ?? OSDManager.OverlayBATTLevel;
        switch (_level)
        {
            case WidgetLevel.FULL:
                //OSDManager.AddElementIfNotNull(entry, PlatformManager.LibreHardware.GetBatteryLevel() ?? BatteryLifePercent(), "%");
                //OSDManager.AddElementIfNotNull(entry, PlatformManager.LibreHardware.GetBatteryPower() ?? BatteryChargeRateInWatts(), "W");
                OSDManager.AddElementIfNotNull(entry, PlatformManager.LibreHardware.GetBatteryLevel() ?? HardwareControl.BatteryCapacity, "%");
                OSDManager.AddElementIfNotNull(entry, PlatformManager.LibreHardware.GetBatteryPower() ?? HardwareControl.BatteryChargeRate, "W");
                OSDManager.AddElementIfNotNull(entry, HardwareControl.CPUFanRPM, "rpm");
                break;
            case WidgetLevel.MINIMAL:
                OSDManager.AddElementIfNotNull(entry, PlatformManager.LibreHardware.GetBatteryLevel() ?? HardwareControl.BatteryCapacity, "%");
                break;
            default:
                return;
        }

        if (IsBatteryCharging())
        {
            if (HardwareControl.TimeFullInMinutes is null or 0)
                return;

            if (HardwareControl.TimeFullInMinutes / 60f is not null and not 0)
                OSDManager.AddElementIfNotNull(entry, HardwareControl.TimeFullInMinutes / 60f, "h");

            if (HardwareControl.TimeFullInMinutes % 60f is not null and not 0)
                OSDManager.AddElementIfNotNull(entry, HardwareControl.TimeFullInMinutes % 60f, "m");

            return;
        }

        if (HardwareControl.TimeLeftInMinutes is null or 0)
            return;

        if (HardwareControl.TimeLeftInMinutes / 60f is not null and not 0)
            OSDManager.AddElementIfNotNull(entry, HardwareControl.TimeLeftInMinutes / 60f, "h");

        if (HardwareControl.TimeLeftInMinutes % 60f is not null and not 0)
            OSDManager.AddElementIfNotNull(entry, HardwareControl.TimeLeftInMinutes % 60f, "m");
    }

    private static bool IsBatteryCharging() => SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Online;
}
