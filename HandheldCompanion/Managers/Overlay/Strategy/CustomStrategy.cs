using System;
using System.Collections.Generic;

namespace HandheldCompanion.Managers.Overlay.Strategy;

public class CustomStrategy : IOverlayStrategy
{
    public string? GetConfig(int direction = 0)
    {
        List<string> Content = [];
        for (int i = 0; i < OSDManager.OverlayCount; i++)
        {
            var name = OSDManager.OverlayOrder[i];
            var content = EntryContent(name);
            if (content == "") continue;
            Content.Add(content);
        }
        return direction == 0
            ? string.Join("<C1> | <C>", Content)
            : string.Join("\n", Content);
    }


    private static string EntryContent(string name)
    {
        OverlayRow row = new();
        OverlayEntry entry = new(
            name.Equals("Time", StringComparison.OrdinalIgnoreCase) 
            ? string.Empty 
            : name.Equals("FPS", StringComparison.Ordinal)
            ? "<APP>"
            : name, OverlayColors.EntryColor(name), true);
        WidgetFactory.CreateWidget(name, entry);

        // Skip empty rows
        if (entry.elements.Count == 0)
        {
            return "";
        }

        row.entries.Add(entry);
        return row.ToString();
    }
}