namespace HandheldCompanion.Managers.Overlay.Strategy;

public class ExtendedStrategy : IOverlayStrategy
{
    public string GetConfig(int direction = 0)
    {
        OverlayRow row1 = new();
        OverlayEntry FPSentry = new("<APP>", OverlayColors.FPS_COLOR, true);
        WidgetFactory.CreateWidget("FPS", FPSentry, WidgetLevel.FULL);
        row1.entries.Add(FPSentry);

        OverlayEntry GPUentry = new("GPU", OverlayColors.GPU_COLOR, true);
        WidgetFactory.CreateWidget("GPU", GPUentry, WidgetLevel.MINIMAL);
        row1.entries.Add(GPUentry);

        //OverlayEntry VRAMentry = new("VRAM", OverlayColors.VRAM_COLOR, true);
        //WidgetFactory.CreateWidget("VRAM", VRAMentry, WidgetLevel.MINIMAL);
        //row1.entries.Add(VRAMentry);

        OverlayEntry CPUentry = new("CPU", OverlayColors.CPU_COLOR, true);
        WidgetFactory.CreateWidget("CPU", CPUentry, WidgetLevel.MINIMAL);
        row1.entries.Add(CPUentry);

        OverlayEntry RAMentry = new("RAM", OverlayColors.RAM_COLOR, true);
        WidgetFactory.CreateWidget("RAM", RAMentry, WidgetLevel.MINIMAL);
        row1.entries.Add(RAMentry);

        OverlayEntry BATTentry = new("BATT", OverlayColors.BATT_COLOR, true);
        WidgetFactory.CreateWidget("BATT", BATTentry, WidgetLevel.MINIMAL);
        row1.entries.Add(BATTentry);

        return direction == 0 
            ? row1.ToString() 
            : string.Join("\n", row1.ToString().Split("<C1> | <C>"));
    }
}
