namespace HandheldCompanion.Managers.Overlay;

public interface IOverlayStrategy
{
    public string? GetConfig(int direction = 0);
}
