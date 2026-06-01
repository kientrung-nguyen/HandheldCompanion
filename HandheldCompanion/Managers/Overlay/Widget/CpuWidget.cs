namespace HandheldCompanion.Managers.Overlay.Widget;

public class CpuWidget : IWidget
{
    public void Build(OverlayEntry entry, short? level = null)
    {
        int _level = level ?? OSDManager.OverlayCPULevel;
        switch (_level)
        {
            case WidgetLevel.MINIMAL:
                OSDManager.AddElementIfNotNull(entry, PlatformManager.LibreHardware.GetCPULoad(), "%");
                OSDManager.AddElementIfNotNull(entry, PlatformManager.LibreHardware.GetCPUTemperature(), "°C");
                OSDManager.AddElementIfNotNull(entry, PlatformManager.LibreHardware.GetCPUPower(), "W");
                break;
            case WidgetLevel.FULL:
                OSDManager.AddElementIfNotNull(entry,
                    PlatformManager.LibreHardware.GetCPULoad(),
                    PlatformManager.LibreHardware.GetCPULoadMax(), "%");
                OSDManager.AddElementIfNotNull(entry, PlatformManager.LibreHardware.GetCPUTemperature(), "°C");
                OSDManager.AddElementIfNotNull(entry, PlatformManager.LibreHardware.GetCPUPower(), "W");
                if (PlatformManager.LibreHardware.GetCPUClock() > 1000
                    || PlatformManager.LibreHardware.GetCPUClockMax() > 1000)
                    OSDManager.AddElementIfNotNull(entry,
                        PlatformManager.LibreHardware.GetCPUClock() / 1000f,
                        PlatformManager.LibreHardware.GetCPUClockMax() / 1000f, "GHz");
                else
                    OSDManager.AddElementIfNotNull(entry,
                        PlatformManager.LibreHardware.GetCPUClock(),
                        PlatformManager.LibreHardware.GetCPUClockMax(), "MHz");
                break;
        }
    }
}