using System;
using System.Globalization;

namespace HandheldCompanion.Managers.Overlay.Widget;

public class TimeWidget : IWidget
{
    public void Build(OverlayEntry entry, short? level = null)
    {
        var _level = level ?? OSDManager.OverlayTimeLevel;
        switch (_level)
        {
            case WidgetLevel.FULL:
                //entry.elements.Add(new OverlayEntryElement(DateTime.Now.ToString(CultureInfo.InvariantCulture)));
                entry.elements.Add(new OverlayEntryElement("<TIME=%b %d %y %I:%M:%S>", "<TIME=%p>"));
                break;
            case WidgetLevel.MINIMAL:
                //entry.elements.Add(new OverlayEntryElement(DateTime.Now.ToString("t")));
                entry.elements.Add(new OverlayEntryElement("<TIME=%I:%M:%S>", "<TIME=%p>"));
                break;
        }
    }
}