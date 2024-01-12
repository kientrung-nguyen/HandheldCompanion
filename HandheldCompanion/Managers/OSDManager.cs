using Hwinfo.SharedMemory;
using PrecisionTiming;
using RTSSSharedMemoryNET;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Windows;
using static HandheldCompanion.Platforms.HWiNFO;

namespace HandheldCompanion.Managers;

public static class OSDManager
{
    public enum OverlayDisplayLevel : short
    {
        Disabled,
        Minimal,
        Extended,
        Full,
        Horizontal,
        External
    }
    public delegate void InitializedEventHandler();

    // C1: GPU
    // C2: CPU
    // C3: RAM
    // C4: VRAM
    // C5: BATT
    // C6: FPS
    private const string Header =
        "<C0=FFFFFF><C1=458A6E><C2=4C8DB2><C3=AD7B95><C4=A369A6><C5=F19F86><C6=D76D76><A0=-4><A1=5><A2=-2><A3=-3><A4=-4><A5=-5><S0=-50><S1=90>";

    private static bool IsInitialized;
    public static short OverlayLevel;

    private static readonly PrecisionTimer RefreshTimer;
    private static int RefreshInterval = 100;

    private static readonly ConcurrentDictionary<int, OSD> OnScreenDisplay = new();
    private static AppEntry OnScreenAppEntry;
    private static List<string> Content;

    static OSDManager()
    {
        SettingsManager.SettingValueChanged += SettingsManager_SettingValueChanged;

        PlatformManager.RTSS.Hooked += RTSS_Hooked;
        PlatformManager.RTSS.Unhooked += RTSS_Unhooked;

        // timer used to monitor foreground application framerate
        RefreshInterval = SettingsManager.GetInt("OnScreenDisplayRefreshRate");

        // OverlayLevel
        OverlayLevel = Convert.ToInt16(SettingsManager.GetInt("OnScreenDisplayLevel"));

        RefreshTimer = new PrecisionTimer();
        RefreshTimer.SetInterval(new Action(UpdateOSD), RefreshInterval, false, 0, TimerMode.Periodic, true);
    }

    public static event InitializedEventHandler Initialized;

    private static void RTSS_Unhooked(int processId)
    {
        try
        {
            // clear previous display
            if (OnScreenDisplay.TryGetValue(processId, out var OSD))
            {
                OSD.Update(string.Empty);
                OSD.Dispose();
                OnScreenDisplay.TryRemove(new KeyValuePair<int, OSD>(processId, OSD));
            }
        }
        catch { }
    }

    private static void RTSS_Hooked(AppEntry appEntry)
    {
        try
        {
            // update foreground id
            OnScreenAppEntry = appEntry;

            // only create a new OSD if needed
            if (OnScreenDisplay.ContainsKey(appEntry.ProcessId))
                return;

            OnScreenDisplay[OnScreenAppEntry.ProcessId] = new OSD(OnScreenAppEntry.Name);
        }
        catch { }
    }

    public static void Start()
    {
        IsInitialized = true;
        Initialized?.Invoke();

        LogManager.LogInformation("{0} has started", "OSDManager");

        if (OverlayLevel != (short)OverlayDisplayLevel.Disabled && !RefreshTimer.IsRunning())
            RefreshTimer.Start();
    }
    /*
    private static uint OSDIndex(this OSD? osd)
    {
        if (osd is null)
            return uint.MaxValue;

        var osdSlot = typeof(OSD).GetField("m_osdSlot",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var value = osdSlot?.GetValue(osd);
        if (value is null)
            return uint.MaxValue;

        return (uint)value;
    }

    private static uint OSDIndex(string name)
    {
        var entries = OSD.GetOSDEntries();
        for (var i = 0; i < entries.Length; i++)
            if (entries[i].Owner == name)
                return (uint)i;
        return 0;
    }
    */
    private static void UpdateOSD()
    {
        if (OverlayLevel == (short)OverlayDisplayLevel.Disabled || OnScreenAppEntry is null)
            return;

        foreach (var pair in OnScreenDisplay)
        {
            var processId = pair.Key;
            var processOSD = pair.Value;

            try
            {
                processOSD.Update(Draw(processId));
            }
            catch { }
        }
    }

    private static string Draw(int processId)
    {
        if (OnScreenAppEntry is null || OnScreenAppEntry.ProcessId != processId)
            return string.Empty;

        SensorReading sensor;
        Content = new List<string>();

        switch (OverlayLevel)
        {
            default:
            case (short)OverlayDisplayLevel.Disabled: // Disabled
                break;

            case (short)OverlayDisplayLevel.Minimal: // Minimal
                {
                    OverlayRow row1 = new();

                    OverlayEntry FPSentry = new("<APP>", "C6");
                    FPSentry.elements.Add(new OverlayEntryElement
                    {
                        Value = "<FR>",
                        SzUnit = "FPS"
                    });
                    FPSentry.elements.Add(new OverlayEntryElement
                    {
                        Value = "<FT>",
                        SzUnit = "ms"
                    });
                    row1.entries.Add(FPSentry);

                    // add header to row1
                    Content.Add(Header + row1);
                }
                break;

            case (short)OverlayDisplayLevel.Extended: // Extended
                {
                    PlatformManager.HWiNFO.ReaffirmRunningProcess();

                    OverlayRow row1 = new();

                    OverlayEntry BATTentry = new("BATT", "C5");
                    if (PlatformManager.HWiNFO.MonitoredSensors.TryGetValue(SensorElementType.BatteryChargeLevel, out sensor))
                        BATTentry.elements.Add(new OverlayEntryElement(sensor));
                    if (PlatformManager.HWiNFO.MonitoredSensors.TryGetValue(SensorElementType.BatteryRemainingCapacity, out sensor))
                        BATTentry.elements.Add(new OverlayEntryElement(sensor));
                    row1.entries.Add(BATTentry);

                    OverlayEntry GPUentry = new("GPU", "C1");
                    if (PlatformManager.HWiNFO.MonitoredSensors.TryGetValue(SensorElementType.GPUUsage, out sensor))
                        GPUentry.elements.Add(new OverlayEntryElement(sensor));
                    if (PlatformManager.HWiNFO.MonitoredSensors.TryGetValue(SensorElementType.GPUPower, out sensor))
                        GPUentry.elements.Add(new OverlayEntryElement(sensor));
                    row1.entries.Add(GPUentry);

                    OverlayEntry CPUentry = new("CPU", "C2");
                    if (PlatformManager.HWiNFO.MonitoredSensors.TryGetValue(SensorElementType.CPUUsage, out sensor))
                        CPUentry.elements.Add(new OverlayEntryElement(sensor));
                    if (PlatformManager.HWiNFO.MonitoredSensors.TryGetValue(SensorElementType.CPUPower, out sensor))
                        CPUentry.elements.Add(new OverlayEntryElement(sensor));
                    row1.entries.Add(CPUentry);

                    OverlayEntry RAMentry = new("RAM", "C3");
                    if (PlatformManager.HWiNFO.MonitoredSensors.TryGetValue(SensorElementType.PhysicalMemoryUsage,
                            out sensor))
                        RAMentry.elements.Add(new OverlayEntryElement(sensor));
                    row1.entries.Add(RAMentry);

                    OverlayEntry FPSentry = new("<APP>", "C6");
                    FPSentry.elements.Add(new OverlayEntryElement
                    {
                        Value = "<FR>",
                        SzUnit = "FPS"
                    });
                    FPSentry.elements.Add(new OverlayEntryElement
                    {
                        Value = "<FT>",
                        SzUnit = "ms"
                    });

                    row1.entries.Add(FPSentry);

                    // add header to row1
                    Content.Add(Header + row1);
                }
                break;
            case (short)OverlayDisplayLevel.Full: // Full
            case (short)OverlayDisplayLevel.Horizontal:
                {
                    PlatformManager.HWiNFO.ReaffirmRunningProcess();

                    OverlayRow gpuRow = new();
                    OverlayRow cpuRow = new();
                    OverlayRow ramRow = new();
                    OverlayRow vramRow = new();
                    OverlayRow fpsRow = new();
                    OverlayRow battRow = new();

                    OverlayEntry GPUentry = new("GPU", "C1", OverlayLevel != (short)OverlayDisplayLevel.Horizontal);
                    if (PlatformManager.HWiNFO.MonitoredSensors.TryGetValue(SensorElementType.GPUUsage, out sensor))
                        GPUentry.elements.Add(new OverlayEntryElement(sensor));
                    if (PlatformManager.HWiNFO.MonitoredSensors.TryGetValue(SensorElementType.GPUPower, out sensor))
                        GPUentry.elements.Add(new OverlayEntryElement(sensor, "{0:0.0}"));
                    if (PlatformManager.HWiNFO.MonitoredSensors.TryGetValue(SensorElementType.GPUTemperature, out sensor))
                        GPUentry.elements.Add(new OverlayEntryElement(sensor, "{0:0.0}"));
                    if (PlatformManager.HWiNFO.MonitoredSensors.TryGetValue(SensorElementType.GPUFrequency, out sensor))
                    {
                        var gpuEntry = new OverlayEntryElement(sensor);
                        if (PlatformManager.HWiNFO.MonitoredSensors.TryGetValue(SensorElementType.GPUFrequencyEffective, out sensor))
                            gpuEntry.Value = string.Format("{0:00}", sensor.Value) + "/" + gpuEntry.Value;
                        GPUentry.elements.Add(gpuEntry);
                    }
                    gpuRow.entries.Add(GPUentry);

                    OverlayEntry CPUentry = new("CPU", "C2", OverlayLevel != (short)OverlayDisplayLevel.Horizontal);
                    if (PlatformManager.HWiNFO.MonitoredSensors.TryGetValue(SensorElementType.CPUUsage, out sensor))
                        CPUentry.elements.Add(new OverlayEntryElement(sensor));
                    if (PlatformManager.HWiNFO.MonitoredSensors.TryGetValue(SensorElementType.CPUPower, out sensor))
                        CPUentry.elements.Add(new OverlayEntryElement(sensor, "{0:0.0}"));
                    if (PlatformManager.HWiNFO.MonitoredSensors.TryGetValue(SensorElementType.CPUTemperature, out sensor))
                        CPUentry.elements.Add(new OverlayEntryElement(sensor, "{0:0.0}"));
                    if (PlatformManager.HWiNFO.MonitoredSensors.TryGetValue(SensorElementType.CPUFrequency, out sensor))
                    {
                        var cpuFrequency = new OverlayEntryElement(sensor);
                        if (sensor.Type == SensorType.SensorTypeNone &&
                            PlatformManager.HWiNFO.MonitoredSensors.TryGetValue(SensorElementType.CPUCoreRatio, out var sensorCore) &&
                            PlatformManager.HWiNFO.MonitoredSensors.TryGetValue(SensorElementType.CPUBusClock, out var sensorClock))
                        {
                            cpuFrequency = new OverlayEntryElement
                            {
                                Value = string.Format("{0:00}", sensorClock.Value * sensorCore.Value),
                                SzUnit = "MHz"
                            };
                        }
                        if (PlatformManager.HWiNFO.MonitoredSensors.TryGetValue(SensorElementType.CPUFrequencyEffective, out sensor))
                            cpuFrequency.Value = string.Format("{0:00}", sensor.Value) + "/" + cpuFrequency.Value;
                        CPUentry.elements.Add(cpuFrequency);
                    }
                    cpuRow.entries.Add(CPUentry);

                    OverlayEntry RAMentry = new("RAM", "C3", OverlayLevel != (short)OverlayDisplayLevel.Horizontal);
                    if (PlatformManager.HWiNFO.MonitoredSensors.TryGetValue(SensorElementType.PhysicalMemoryUsage, out sensor))
                    {
                        var ramElement = new OverlayEntryElement
                        {
                            Value = string.Format("{0:0.0}", sensor.Value / 1024),
                            SzUnit = "GB"
                        };
                        RAMentry.elements.Add(ramElement);
                        //RAMentry.elements.Add(new OverlayEntryElement(sensor));
                    }
                    ramRow.entries.Add(RAMentry);

                    OverlayEntry VRAMentry = new("VRAM", "C4", OverlayLevel != (short)OverlayDisplayLevel.Horizontal);
                    if (PlatformManager.HWiNFO.MonitoredSensors.TryGetValue(SensorElementType.GPUMemoryUsage, out sensor))
                    {
                        var vramElement = new OverlayEntryElement
                        {
                            Value = string.Format("{0:0.0}", sensor.Value / 1024),
                            SzUnit = "GB"
                        };
                        VRAMentry.elements.Add(vramElement);
                        //VRAMentry.elements.Add(new OverlayEntryElement(sensor));
                    }
                    vramRow.entries.Add(VRAMentry);

                    OverlayEntry BATTentry = new("BATT", "C5", OverlayLevel != (short)OverlayDisplayLevel.Horizontal);
                    if (PlatformManager.HWiNFO.MonitoredSensors.TryGetValue(SensorElementType.BatteryChargeLevel,
                            out sensor))
                        BATTentry.elements.Add(new OverlayEntryElement(sensor));
                    if (PlatformManager.HWiNFO.MonitoredSensors.TryGetValue(SensorElementType.BatteryChargeRate,
                            out sensor))
                        BATTentry.elements.Add(new OverlayEntryElement(sensor));
                    if (PlatformManager.HWiNFO.MonitoredSensors.TryGetValue(SensorElementType.BatteryRemainingTime,
                            out sensor))
                        BATTentry.elements.Add(new OverlayEntryElement(sensor));
                    battRow.entries.Add(BATTentry);

                    OverlayEntry FPSentry = new("<APP>", "C6", OverlayLevel != (short)OverlayDisplayLevel.Horizontal);
                    FPSentry.elements.Add(new OverlayEntryElement
                    {
                        Value = "<FR>",
                        SzUnit = "FPS"
                    });
                    FPSentry.elements.Add(new OverlayEntryElement
                    {
                        Value = "<FT>",
                        SzUnit = "ms"
                    });
                    fpsRow.entries.Add(FPSentry);

                    // add header to row1
                    if (OverlayLevel == (short)OverlayDisplayLevel.Horizontal)
                        Content.Add(Header + string.Join(" | ", new[] {
                            fpsRow.ToString(),
                            gpuRow.ToString(),
                            vramRow.ToString(),
                            cpuRow.ToString(),
                            ramRow.ToString(),
                            battRow.ToString()
                        }));
                    else
                    {
                        Content.Add(Header + gpuRow);
                        Content.Add(cpuRow.ToString());
                        Content.Add(ramRow.ToString());
                        Content.Add(vramRow.ToString());
                        Content.Add(battRow.ToString());
                        Content.Add(fpsRow.ToString());
                    }
                }
                break;

            case (short)OverlayDisplayLevel.External: // External
                {
                    /*
                     * Intended to simply allow RTSS/HWINFO to run, and let the user configure the overlay within those
                     * tools as they wish
                     */
                }
                break;
        }

        return string.Join("\n", Content);
    }

    public static void Stop()
    {
        if (!IsInitialized)
            return;

        RefreshTimer.Stop();

        // unhook all processes
        foreach (var processId in OnScreenDisplay.Keys)
            RTSS_Unhooked(processId);

        IsInitialized = false;

        LogManager.LogInformation("{0} has stopped", "OSDManager");
    }

    private static void SettingsManager_SettingValueChanged(string name, object value)
    {
        switch (name)
        {
            case "OnScreenDisplayLevel":
                {
                    OverlayLevel = Convert.ToInt16(value);

                    if (OverlayLevel > 0)
                    {
                        if (OverlayLevel == (short)OverlayDisplayLevel.External)
                        {
                            // No need to update OSD in External
                            RefreshTimer.Stop();

                            // Remove previous UI in External
                            foreach (var pair in OnScreenDisplay)
                            {
                                var processOSD = pair.Value;
                                processOSD.Update(string.Empty);
                            }
                        }
                        else
                        {
                            // Other modes need the refresh timer to update OSD
                            if (!RefreshTimer.IsRunning())
                                RefreshTimer.Start();
                        }
                    }
                    else
                    {
                        RefreshTimer.Stop();

                        // clear UI on stop
                        foreach (var pair in OnScreenDisplay)
                        {
                            var processOSD = pair.Value;
                            processOSD.Update(string.Empty);
                        }
                    }
                }
                break;

            case "OnScreenDisplayRefreshRate":
                {
                    RefreshInterval = Convert.ToInt32(value);

                    if (RefreshTimer.IsRunning())
                    {
                        RefreshTimer.Stop();
                        RefreshTimer.SetPeriod(RefreshInterval);
                        RefreshTimer.Start();
                    }
                }
                break;
        }
    }
}

public struct OverlayEntryElement
{
    public string Value { get; set; }
    public string SzUnit { get; set; }

    public override string ToString()
    {
        return string.Format("<C0>{0}<S1>{1}<S><C>", Value, SzUnit);
    }

    //public OverlayEntryElement(SensorElement sensor)
    //{
    //    Value = string.Format("{0:00}", sensor.Value);
    //    SzUnit = sensor.szUnit;
    //}

    public OverlayEntryElement(SensorReading sensor, string format = "{0:00}")
    {
        Value = string.Format(format, sensor.Value);
        SzUnit = sensor.Unit;
    }
}

public class OverlayEntry : IDisposable
{
    public List<OverlayEntryElement> elements = new();

    public OverlayEntry(string name, string colorScheme = "", bool indent = false)
    {
        Name = indent ? name + ":\t" : name + ":";

        if (colorScheme != null && colorScheme.Length > 0)
            Name = "<" + colorScheme + ">" + Name + "<C>";
    }

    public string Name { get; set; }

    public void Dispose()
    {
        elements.Clear();
        elements = null;
    }
}

public class OverlayRow : IDisposable
{
    public List<OverlayEntry> entries = new();

    public void Dispose()
    {
        entries.Clear();
        entries = null;
    }

    public override string ToString()
    {
        var rowStr = new List<string>();
        foreach (var entry in entries)
        {
            if (entry.elements is null || entry.elements.Count == 0)
                continue;

            var entryStr = new List<string>() { entry.Name };

            foreach (var element in entry.elements)
                entryStr.Add(element.ToString());

            var itemStr = string.Join(" ", entryStr);
            rowStr.Add(itemStr);
        }

        return string.Join(" | ", rowStr);
    }
}