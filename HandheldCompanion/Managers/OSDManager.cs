using HandheldCompanion.Utils;
using HandheldCompanion.Views;
using Hwinfo.SharedMemory;
using PrecisionTiming;
using RTSSSharedMemoryNET;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
        "<LI=Plugins\\Client\\Overlays\\sample.png>" +
        "<C0=FFFFFF><C1=458A6E><C2=4C8DB2><C3=AD7B95><C4=A369A6><C5=F19F86><C6=D76D76>" +
        "<A0=-4><A1=5><A2=-2><A3=-3><A4=-4><A5=-5>" +
        "<S0=-50><S1=80>";
    private const string Footer = "<P1><L0><C=64000000><B=0,0>\b<C><E=-178,-2,4>";
    //"<C0=FFFFFF><C1=458A6E><C2=4C8DB2><C3=AD7B95><C4=A369A6><C5=F19F86><C6=D76D76><A0=-4><A1=5><A2=-2><A3=-3><A4=-4><A5=-5><S0=-50><S1=80><P1><M=0,0,0,0><L0><C=64000000><B=0,0>\b<C>";

    private static bool IsInitialized;
    public static OverlayDisplayLevel OverlayLevel;
	public static string[] OverlayOrder;
    public static int OverlayCount;
    public static short OverlayLevel;
    public static short OverlayTimeLevel;
    public static short OverlayFPSLevel;
    public static short OverlayCPULevel;
    public static short OverlayRAMLevel;
    public static short OverlayGPULevel;
    public static short OverlayVRAMLevel;
    public static short OverlayBATTLevel;

    private static readonly PrecisionTimer RefreshTimer;
    private static int RefreshInterval = 100;

    private static ConcurrentDictionary<int, OSD> OnScreenDisplay = new();
    private static uint OnScreenAppEntryOSDFrameId;
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
        OverlayLevel = EnumUtils<OverlayDisplayLevel>.Parse(Convert.ToInt16(SettingsManager.GetInt("OnScreenDisplayLevel")));

        RefreshTimer = new PrecisionTimer();
        RefreshTimer.SetInterval(new Action(UpdateOSD), RefreshInterval, false, 0, TimerMode.Periodic, true);
    }

    public static event InitializedEventHandler Initialized;

    private static void RTSS_Unhooked(int processId)
    {
        try
        {
            // clear previous display
            if (OnScreenDisplay.Remove(processId, out var OSD))
            {
                OSD.Update(string.Empty);
                OSD.Dispose();
            }
        }
        catch { }
    }

    private static void RTSS_Hooked(AppEntry appEntry)
    {
        try
        {
            LogManager.LogDebug($"{nameof(OSDManager)} RTSS hooked {appEntry.Name} ({appEntry.ProcessId})");
            // update foreground id
            OnScreenAppEntryOSDFrameId = appEntry.OSDFrameId;
            OnScreenAppEntry = appEntry;

            // only create a new OSD if needed
            if (!OnScreenDisplay.TryGetValue(appEntry.ProcessId, out var OSD))
                OnScreenDisplay[OnScreenAppEntry.ProcessId] = OSD = new OSD(OnScreenAppEntry.Name);
        }
        catch { }
    }

    public static void Start()
    {
        IsInitialized = true;
        Initialized?.Invoke();

        OnScreenDisplay = new();
        OverlayLevel = EnumUtils<OverlayDisplayLevel>.Parse(Convert.ToInt16(SettingsManager.GetInt("OnScreenDisplayLevel")));

        PlatformManager.RTSS.Start();

        LogManager.LogInformation("{0} has started", "OSDManager");

        if (OverlayLevel != OverlayDisplayLevel.Disabled)
            RefreshTimer.Start();
    }

    private static void UpdateOSD()
    {
        if (OverlayLevel == OverlayDisplayLevel.Disabled)
            return;

        foreach (var OSD in OnScreenDisplay)
        {
            var processId = OSD.Key;
            var processOSD = OSD.Value;

            if (OnScreenAppEntry is not null)
            {
                if (OnScreenAppEntry.ProcessId == processId)
                {
                    PlatformManager.RTSS.GetFramerate(processId, out var osdFrameId);
                    processOSD.Update(Draw(osdFrameId));
                    OnScreenAppEntryOSDFrameId = osdFrameId;
                }
                else
                {
                    processOSD.Update(string.Empty);
                    processOSD.Dispose();
                }
            }
        }
    }

    private static string Draw(uint osdFrameId)
    {
        if (OnScreenAppEntryOSDFrameId - osdFrameId == 0)
            return string.Empty;

        Content = [];
        switch (OverlayLevel)
        {
            default:
            case OverlayDisplayLevel.Disabled: // Disabled
                break;

            case OverlayDisplayLevel.Minimal: // Minimal
                {
                    using OverlayRow row1 = new();

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

            case OverlayDisplayLevel.Extended: // Extended
                {
                    PlatformManager.HWiNFO.ReaffirmRunningProcess();

                    using OverlayRow row1 = new();

                    using OverlayEntry GPUentry = new("GPU", "C1");
                    AddElementIfFound(GPUentry, SensorElementType.GPUUsage);
                    AddElementIfFound(GPUentry, SensorElementType.GPUPower);
                    row1.entries.Add(GPUentry);

                    using OverlayEntry CPUentry = new("CPU", "C2");
                    AddElementIfFound(CPUentry, SensorElementType.CPUUsage);
                    AddElementIfFound(CPUentry, SensorElementType.CPUPower);
                    row1.entries.Add(CPUentry);

                    using OverlayEntry RAMentry = new("RAM", "C3");
                    AddElementIfFound(RAMentry, SensorElementType.PhysicalMemoryUsage);
                    row1.entries.Add(RAMentry);

                    using OverlayEntry BATTentry = new("BATT", "C5");
                    AddElementIfFound(BATTentry, SensorElementType.BatteryChargeLevel);
                    AddElementIfFound(BATTentry, SensorElementType.BatteryRemainingTime);
                    row1.entries.Add(BATTentry);

                    using OverlayEntry FPSentry = new("<APP>", "C6");
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

            case OverlayDisplayLevel.Full: // Full
                {
                    PlatformManager.HWiNFO.ReaffirmRunningProcess();

                    using OverlayRow rowGpu = new();
                    using OverlayRow rowCpu = new();
                    using OverlayRow rowFan = new();
                    using OverlayRow rowRam = new();
                    using OverlayRow rowVram = new();
                    using OverlayRow rowBatt = new();
                    using OverlayRow rowFps = new();

                    using OverlayEntry GPUentry = new("GPU", "C1");
                    AddElementIfFound(GPUentry, SensorElementType.GPUFrequencyEffective);
                    AddElementIfFound(GPUentry, SensorElementType.GPUUsage);
                    AddElementIfFound(GPUentry, SensorElementType.GPUTemperature);
                    AddElementIfFound(GPUentry, SensorElementType.GPUPower);
                    rowGpu.entries.Add(GPUentry);

                    using OverlayEntry CPUentry = new("CPU", "C2");
                    AddElementIfFound(CPUentry, SensorElementType.CPUFrequencyEffective);
                    AddElementIfFound(CPUentry, SensorElementType.CPUUsage);
                    AddElementIfFound(CPUentry, SensorElementType.CPUTemperature);
                    AddElementIfFound(CPUentry, SensorElementType.CPUPower);
                    rowCpu.entries.Add(CPUentry);

                    using OverlayEntry FANentry = new("FAN", "C2");
                    AddElementIfNotNull(FANentry, PlatformManager.HWiNFO.CPUFanSpeed, "rpm");
                    AddElementIfNotNull(FANentry, PlatformManager.HWiNFO.CPUFanDuty, "%");
                    rowFan.entries.Add(FANentry);

                    using OverlayEntry RAMentry = new("RAM", "C3");
                    AddElementIfFound(RAMentry, SensorElementType.PhysicalMemoryUsage);
                    rowRam.entries.Add(RAMentry);


                    using OverlayEntry VRAMentry = new("VRAM", "C4");
                    AddElementIfFound(VRAMentry, SensorElementType.GPUMemoryUsage);
                    rowVram.entries.Add(VRAMentry);

                    using OverlayEntry BATTentry = new("BATT", "C5");
                    AddElementIfFound(BATTentry, SensorElementType.BatteryChargeLevel);
                    AddElementIfFound(BATTentry, SensorElementType.BatteryChargeRate);
                    AddElementIfFound(BATTentry, SensorElementType.BatteryRemainingTime);
                    BATTentry.elements.Add(new OverlayEntryElement
                    {
                        Value = " <TIME=%X>"
                    });
                    rowBatt.entries.Add(BATTentry);
                    using OverlayEntry FPSentry = new("<APP>", "C6");
                    FPSentry.elements.Add(new OverlayEntryElement
                    {
                        Value = "<FR>",
                        SzUnit = "FPS"
                    });
                    //FPSentry.elements.Add(new OverlayEntryElement
                    //{
                    //    Value = "<FT>",
                    //    SzUnit = "ms"
                    //});
                    rowFps.entries.Add(FPSentry);


                    using OverlayRow rowTdp = new();
                    using OverlayEntry TDPentry = new("TDP", "C6");
                    AddElementIfFound(TDPentry, SensorElementType.PL3);
                    rowTdp.entries.Add(TDPentry);

                    Content.Add(Header + Footer + string.Join("  ", new[] {
                            rowFps.ToString(),
                            rowGpu.ToString(),
                            rowVram.ToString(),
                            rowCpu.ToString(),
                            rowRam.ToString(),
                            rowFan.ToString(),
                            rowTdp.ToString(),
                            rowBatt.ToString()
                        }));
                    // add header to row1
                    /*
                    Content.Add(Header + rowGpu);
                    Content.Add(rowCpu.ToString());
                    Content.Add(rowRam.ToString());
                    Content.Add(rowVram.ToString());
                    Content.Add(rowBatt.ToString());
                    Content.Add(rowFps.ToString());
                    */
                }
                break;
            case OverlayDisplayLevel.External: // External
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

    private static void AddElementIfNotNull(OverlayEntry entry, float? value, string unit, string format = "{0:00}")
    {
        if (value is float fl && !float.IsNaN(fl))
            entry.elements.Add(new OverlayEntryElement(fl, unit, format));
    }

    private static void AddElementIfFound(OverlayEntry entry, SensorElementType elementType)
    {
        if (PlatformManager.HWiNFO.MonitoredSensors.TryGetValue(elementType, out var sensor) && sensor.Type != SensorType.SensorTypeNone)
        {
            switch (elementType)
            {
                case SensorElementType.PL1:
                case SensorElementType.PL2:
                    if (PlatformManager.HWiNFO.MonitoredSensors.TryGetValue(SensorElementType.CPUPower, out var cpuPower) && cpuPower.Type != SensorType.SensorTypeNone)
                        entry.elements.Add(new OverlayEntryElement()
                        {
                            Value = string.Format("{0:00}", (int)Math.Floor(cpuPower.Value / sensor.Value * 100.0d)),
                            SzUnit = "W"
                        });
                    break;
                case SensorElementType.PL3:
                    if (PlatformManager.HWiNFO.MonitoredSensors.TryGetValue(SensorElementType.APUStapmPower, out var apuStapmPower) && apuStapmPower.Type != SensorType.SensorTypeNone)
                        entry.elements.Add(new OverlayEntryElement()
                        {
                            Value = string.Format("{0:00}", (int)Math.Floor(apuStapmPower.Value / sensor.Value * 100.0d)),
                            SzUnit = "W"
                        });
                    break;
                case SensorElementType.CPUPower:
                case SensorElementType.GPUPower: entry.elements.Add(new OverlayEntryElement(sensor, "{0:0.0}")); break;
                case SensorElementType.GPUMemoryUsage:
                case SensorElementType.PhysicalMemoryUsage:
                    entry.elements.Add(new OverlayEntryElement
                    {
                        Value = string.Format("{0:0.0}", sensor.Value / 1024d),
                        SzUnit = "GB"
                    });
                    break;

                case SensorElementType.GPUFrequencyEffective:
                    {
                        var gpuElement = new OverlayEntryElement(sensor);
                        if (PlatformManager.HWiNFO.MonitoredSensors.TryGetValue(SensorElementType.GPUFrequency, out var gpuFrequency) && gpuFrequency.Type != SensorType.SensorTypeNone)
                            gpuElement.Value += "/" + string.Format("{0:00}", gpuFrequency.Value);
                        entry.elements.Add(gpuElement);
                    }
                    break;
                case SensorElementType.CPUFrequencyEffective:
                    {
                        var cpuElement = new OverlayEntryElement(sensor);
                        if (PlatformManager.HWiNFO.MonitoredSensors.TryGetValue(SensorElementType.CPUCoreRatio, out var coreRatio) && coreRatio.Type != SensorType.SensorTypeNone &&
                            PlatformManager.HWiNFO.MonitoredSensors.TryGetValue(SensorElementType.CPUBusClock, out var busClock) && busClock.Type != SensorType.SensorTypeNone)
                        {
                            cpuElement.Value = string.Format("{0:0.0}", sensor.Value / 1000) + "/" + string.Format("{0:0.0}", coreRatio.Value * busClock.Value / 1000);
                            cpuElement.SzUnit = "GHz";
                        }
                        entry.elements.Add(cpuElement);
                    }
                    break;
                default: entry.elements.Add(new OverlayEntryElement(sensor)); break;
            }
        }
    }

    private static void SettingsManager_SettingValueChanged(string name, object value)
    {
        switch (name)
        {
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

            case "OnScreenDisplayLevel":
                {
                    OverlayLevel = EnumUtils<OverlayDisplayLevel>.Parse(Convert.ToInt16(value));

                    // set OSD toggle hotkey state
                    SettingsManager.SetProperty("OnScreenDisplayToggle", Convert.ToBoolean(value));

                    if ((short)OverlayLevel > 0)
                    {
                        // set lastOSDLevel to be used in OSD toggle hotkey
                        SettingsManager.SetProperty("LastOnScreenDisplayLevel", value);

                        if (OverlayLevel == OverlayDisplayLevel.External)
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

            case "OnScreenDisplayOrder":
                OverlayOrder = value.ToString().Split(",");
                OverlayCount = OverlayOrder.Length;
                break;
            case "OnScreenDisplayTimeLevel":
                OverlayTimeLevel = Convert.ToInt16(value);
                break;
            case "OnScreenDisplayFPSLevel":
                OverlayFPSLevel = Convert.ToInt16(value);
                break;
            case "OnScreenDisplayCPULevel":
                OverlayCPULevel = Convert.ToInt16(value);
                break;
            case "OnScreenDisplayRAMLevel":
                OverlayRAMLevel = Convert.ToInt16(value);
                break;
            case "OnScreenDisplayGPULevel":
                OverlayGPULevel = Convert.ToInt16(value);
                break;
            case "OnScreenDisplayVRAMLevel":
                OverlayVRAMLevel = Convert.ToInt16(value);
                break;
            case "OnScreenDisplayBATTLevel":
                OverlayBATTLevel = Convert.ToInt16(value);
                break;
        }
    }
}

public struct OverlayEntryElement
{
    public string Value { get; set; }
    public string SzUnit { get; set; }

    public override string ToString() => string.Format("<C0>{0:00}<S1>{1}<S><C>", Value, SzUnit);

    public OverlayEntryElement(float value, string unit, string format = "{0:00}")
    {
        Value = string.Format(format, value);
        SzUnit = unit;
    }

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