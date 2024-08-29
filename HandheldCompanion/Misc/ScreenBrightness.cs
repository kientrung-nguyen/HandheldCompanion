using System;
using System.Diagnostics;
using System.Management;
using HandheldCompanion.Managers;

namespace HandheldCompanion.Misc;

public static class ScreenBrightness
{
    private static ManagementScope scope = new(@"\\.\root\WMI");
    private static ManagementEventWatcher watcher = new(
        scope,
        new EventQuery("SELECT * FROM WmiMonitorBrightnessEvent"));

    public static int Get()
    {
        try
        {
            using var mclass = new ManagementClass("WmiMonitorBrightness")
            {
                Scope = scope
            };
            using var instances = mclass.GetInstances();
            foreach (ManagementObject instance in instances)
            {
                return (byte)instance.GetPropertyValue("CurrentBrightness");
            }
            return 0;
        }
        catch { return -1; }
    }

    public static void Set(int brightness)
    {
        try
        {
            using var mclass = new ManagementClass("WmiMonitorBrightnessMethods")
            {
                Scope = scope
            };
            using var instances = mclass.GetInstances();
            var args = new object[] { 1, brightness };
            foreach (ManagementObject instance in instances)
            {
                instance.InvokeMethod("WmiSetBrightness", args);
            }
        }
        catch { }
    }

    public static int Adjust(int delta)
    {
        int brightness = Get();
        brightness = Math.Min(100, Math.Max(0, brightness + delta));
        Set(brightness);
        return brightness;
    }

    public static void SubscribeToEvents(Action<object, EventArrivedEventArgs> EventHandler)
    {
        try
        {
            scope.Connect();
            watcher.EventArrived += new EventArrivedEventHandler(EventHandler);
            watcher.Start();
        }
        catch
        {
            LogManager.LogError("Cannot connect to Brightness WMI events");
            throw;
        }
    }

    public static void Unsubscribe()
    {
        try
        {
            watcher.Stop();
        }
        catch
        {
            LogManager.LogError("Cannot stop watcher to Brightness WMI events");
            throw;
        }
    }
}
