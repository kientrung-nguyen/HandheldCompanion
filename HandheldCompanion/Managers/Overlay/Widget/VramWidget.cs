namespace HandheldCompanion.Managers.Overlay.Widget;

public class VramWidget : IWidget
{
    public void Build(OverlayEntry entry, short? level = null)
    {
        var _level = level ?? OSDManager.OverlayVRAMLevel;
        switch (_level)
        {
            case WidgetLevel.MINIMAL:
                //OSDManager.AddElementIfNotNull(entry, PlatformManager.LibreHardware.GetGPUMemory(), "GB");
                OSDManager.AddElementIfNotNull(entry,
                    PlatformManager.LibreHardware.GetGPUMemoryDedicated() + PlatformManager.LibreHardware.GetGPUMemoryShared(), "GB");
                break;
            case WidgetLevel.FULL:
                //OSDManager.AddElementIfNotNull(entry, PlatformManager.LibreHardware.GetGPUMemory(), PlatformManager.LibreHardware.GetGPUMemoryTotal(), "GB");
                // Our numbers shows "dedicated video memory" + "shared video memory" / "total dedicated video memory".
                // Shared video memory is memory that was placed in system RAM rather than in dedicated GPU memory.
                // If you are using a CPU integrated GPU, your GPU will not have physically separate memory,
                // but it will have some amount of superficially dedicated memory it has fast access to and this will show up as dedicated memory.
                // If you see the first number larger than the second, we will color it red as a warning, and you are out of dedicated GPU memory
                OSDManager.AddElementIfNotNull(entry, 
                    PlatformManager.LibreHardware.GetGPUMemoryDedicated() + PlatformManager.LibreHardware.GetGPUMemoryShared(),
                    PlatformManager.LibreHardware.GetGPUMemoryDedicatedTotal(), "GB");
                break;
        }
    }
}