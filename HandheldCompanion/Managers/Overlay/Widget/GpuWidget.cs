using HandheldCompanion.GraphicsProcessingUnit;

namespace HandheldCompanion.Managers.Overlay.Widget;

public class GpuWidget : IWidget
{
    public void Build(OverlayEntry entry, short? level = null)
    {
        GPU? _gpu = GPUManager.GetCurrent();
        if (_gpu == null)
        {
            return;
        }

        var _level = level ?? OSDManager.OverlayGPULevel;
        switch (_level)
        {
            case WidgetLevel.FULL:
                OSDManager.AddElementIfNotNull(entry, _gpu.HasLoad() ? _gpu.GetLoad() : PlatformManager.LibreHardware.GetGPULoad(), "%");
                OSDManager.AddElementIfNotNull(entry, _gpu.HasClock() ? _gpu.GetClock() : PlatformManager.LibreHardware.GetGPUClock(), "MHz");
                OSDManager.AddElementIfNotNull(entry, _gpu.HasTemperature() ? _gpu.GetTemperature() : PlatformManager.LibreHardware.GetGPUTemperature(), "°C");
                OSDManager.AddElementIfNotNull(entry, _gpu.HasPower() ? _gpu.GetPower() : PlatformManager.LibreHardware.GetGPUPower(), "W");
                OSDManager.AddElementIfNotNull(entry, _gpu.HasVRAMUsage() && _gpu.HasSharedMemory() 
                    ? _gpu.GetVRAMUsage() + _gpu.GetSharedMemory() 
                    : PlatformManager.LibreHardware.GetGPUMemoryDedicated() + PlatformManager.LibreHardware.GetGPUMemoryShared(), "MB");
                OSDManager.AddElementIfNotNull(entry, _gpu.HasVRAMClock() ? _gpu.GetVRAMClock() : PlatformManager.LibreHardware.GetGPUClock(), "MHz");
                //OSDManager.AddElementIfNotNull(entry,
                //    PlatformManager.LibreHardware.GetGPUMemoryDedicated() + PlatformManager.LibreHardware.GetGPUMemoryShared(),
                //    PlatformManager.LibreHardware.GetGPUMemoryDedicatedTotal(), "GB");
                break;
            case WidgetLevel.MINIMAL:
                OSDManager.AddElementIfNotNull(entry, _gpu.HasLoad() ? _gpu.GetLoad() : PlatformManager.LibreHardware.GetGPULoad(), "%");
                OSDManager.AddElementIfNotNull(entry, _gpu.HasTemperature() ? _gpu.GetTemperature() : PlatformManager.LibreHardware.GetGPUTemperature(), "°C");
                OSDManager.AddElementIfNotNull(entry, _gpu.HasPower() ? _gpu.GetPower() : PlatformManager.LibreHardware.GetGPUPower(), "W");
                break;
        }
    }
}