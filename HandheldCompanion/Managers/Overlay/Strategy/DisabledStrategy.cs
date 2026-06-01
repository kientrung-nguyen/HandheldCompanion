namespace HandheldCompanion.Managers.Overlay.Strategy;

public class DisabledStrategy : IOverlayStrategy
{
    public string? GetConfig(int direction = 0)
    {
        return null;
    }
}
