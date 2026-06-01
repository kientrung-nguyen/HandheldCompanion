namespace HandheldCompanion.Managers.Overlay.Strategy;

public class MinimalStrategy : IOverlayStrategy
{
    public string GetConfig(int direction = 0)
    {
        OverlayRow row1 = new();

        OverlayEntry fpsEntry = new("<APP>", "FF0000", true);
        WidgetFactory.CreateWidget("FPS", fpsEntry, WidgetLevel.MINIMAL);
        row1.entries.Add(fpsEntry);

        return row1.ToString();
    }
}
