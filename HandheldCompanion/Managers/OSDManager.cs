using HandheldCompanion.Properties;
using HandheldCompanion.Utils;
using HandheldCompanion.Views.Windows;
using Hwinfo.SharedMemory;
using PrecisionTiming;
using RTSSSharedMemoryNET;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;

namespace HandheldCompanion.Managers;

public enum OverlayDisplayLevel : short
{
    Disabled,
    Minimal,
    Extended,
    Full,
    Custom,
    External
}
public enum OverlayEntryLevel
{
    Disabled, Minimal, Full
}

public static class OSDManager
{
    public delegate void InitializedEventHandler();

    // C1: GPU // 8040
    // C2: CPU // 80FF
    // C3: RAM // FF80C0
    // C4: VRAM // FF80FF
    // C5: BATT // FF8000
    // C6: FPS // FF0000
    private const string Header =
        "<LI=Plugins\\Client\\Overlays\\sample.png>" +
        "<C0=FFFFFF><C1=8040><C2=80FF><C3=FF80C0><C4=FF80FF><C5=FF8000><C6=FF0000>" +
        //"<A0=-4><A1=5><A2=-2><A3=-3><A4=-4><A5=-5>" +
        "<S0=-40><S1=90>";
    private const string Footer = "<P1><L0><C=80000000><B=0,0>\b<C><E=-140,-2,4>";
    //<P1> "<C0=FFFFFF><C1=458A6E><C2=4C8DB2><C3=AD7B95><C4=A369A6><C5=F19F86><C6=D76D76><A0=-4><A1=5><A2=-2><A3=-3><A4=-4><A5=-5><S0=-50><S1=80><P1><M=0,0,0,0><L0><C=64000000><B=0,0>\b<C>";

    private static bool IsInitialized;
    public static OverlayDisplayLevel OverlayLevel = OverlayDisplayLevel.Disabled;
    public static string[] OverlayOrder;
    public static int OverlayCount;
    public static OverlayEntryLevel OverlayTimeLevel;
    public static OverlayEntryLevel OverlayFPSLevel;
    public static OverlayEntryLevel OverlayCPULevel;
    public static OverlayEntryLevel OverlayRAMLevel;
    public static OverlayEntryLevel OverlayGPULevel;
    public static OverlayEntryLevel OverlayVRAMLevel;
    public static OverlayEntryLevel OverlayBATTLevel;

    private static readonly PrecisionTimer RefreshTimer;
    private static int RefreshInterval = 100;

    private static readonly ConcurrentDictionary<int, OSD> onScreenDisplays = new();
    private static AppEntry osdAppEntry;
    private static List<string> Content;

    static OSDManager()
    {
        SettingsManager.SettingValueChanged += SettingsManager_SettingValueChanged;

        PlatformManager.RTSS.Hooked += RTSS_Hooked;
        PlatformManager.RTSS.Unhooked += RTSS_Unhooked;
        ProfileManager.Applied += ProfileManager_Applied;

        // timer used to monitor foreground application framerate
        RefreshInterval = SettingsManager.Get<int>("OnScreenDisplayRefreshRate");

        RefreshTimer = new PrecisionTimer();
        RefreshTimer.SetInterval(new Action(UpdateOSD), RefreshInterval, false, 0, TimerMode.Periodic, true);
    }

    public static event InitializedEventHandler Initialized;
    private static void ProfileManager_Applied(Profile profile, UpdateSource source)
    {
        OverlayLevel = profile.OverlayLevel;
        OverlayTimeLevel = profile.OverlayTimeLevel;
        OverlayBATTLevel = profile.OverlayBATTLevel;
        OverlayRAMLevel = profile.OverlayRAMLevel;
        OverlayVRAMLevel = profile.OverlayVRAMLevel;
        OverlayCPULevel = profile.OverlayCPULevel;
        OverlayGPULevel = profile.OverlayGPULevel;
        OverlayFPSLevel = profile.OverlayFPSLevel;

        SettingsManager.Set("OnScreenDisplayLevel", (int)OverlayLevel);
        SettingsManager.Set("OnScreenDisplayCPULevel", (int)OverlayCPULevel);
        SettingsManager.Set("OnScreenDisplayFPSLevel", (int)OverlayFPSLevel);
        SettingsManager.Set("OnScreenDisplayBATTLevel", (int)OverlayBATTLevel);
        SettingsManager.Set("OnScreenDisplayGPULevel", (int)OverlayGPULevel);
        SettingsManager.Set("OnScreenDisplayRAMLevel", (int)OverlayRAMLevel);
        SettingsManager.Set("OnScreenDisplayVRAMLevel", (int)OverlayVRAMLevel);

        if (OverlayLevel != OverlayDisplayLevel.Disabled)
        {

            if (OverlayLevel == OverlayDisplayLevel.External)
            {
                // No need to update OSD in External
                RefreshTimer.Stop();

                // Remove previous UI in External
                foreach (var pair in onScreenDisplays)
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
            foreach (var pair in onScreenDisplays)
            {
                var processOSD = pair.Value;
                processOSD.Update(string.Empty);
                processOSD.Dispose();
            }

            onScreenDisplays.Clear();
        }
    }

    private static void RTSS_Unhooked(int processId)
    {
        try
        {
            // clear previous display
            if (onScreenDisplays.TryRemove(processId, out var osd))
            {
                ToastManager.RunToast($"On-screen display {Path.GetFileNameWithoutExtension(osdAppEntry.Name)} {Resources.Off}", ToastIcons.Game);

                osd.Update(string.Empty);
                osd.Dispose();
            }
        }
        catch { }
    }

    private static void RTSS_Hooked(AppEntry appEntry)
    {
        try
        {
            // update foreground id
            osdAppEntry = appEntry;

            // only create a new OSD if needed
            //
            if (!onScreenDisplays.TryGetValue(appEntry.ProcessId, out var osd))
            {
                onScreenDisplays[osdAppEntry.ProcessId] = new OSD(osdAppEntry.Name);
                ToastManager.RunToast($"On-screen display {Path.GetFileNameWithoutExtension(osdAppEntry.Name)} {Resources.On}", ToastIcons.Game);
            }

        }
        catch { }
    }

    public static void Start()
    {
        IsInitialized = true;
        Initialized?.Invoke();

        LogManager.LogInformation("{0} has started", "OSDManager");

        if (OverlayLevel != OverlayDisplayLevel.Disabled && !RefreshTimer.IsRunning())
            RefreshTimer.Start();
    }

    private static void UpdateOSD()
    {
        if (OverlayLevel == OverlayDisplayLevel.Disabled)
            return;

        if (!PlatformManager.RTSS.HasHook())
            return;

        foreach (var osd in onScreenDisplays)
        {
            var processId = osd.Key;
            var processOSD = osd.Value;

            try
            {
                if (osdAppEntry is not null &&
                    osdAppEntry.ProcessId == processId)
                    processOSD.Update(Draw(processId));
                else
                    processOSD.Update(string.Empty);

            }
            catch (Exception ex)
            {
                LogManager.LogError($"{nameof(OSDManager)} [{processId}] failed {ex.Message} {ex.StackTrace}");
            }
        }
    }

    private static string Draw(int processId)
    {
        Content = [];
        switch (OverlayLevel)
        {
            default:
            case OverlayDisplayLevel.Disabled: // Disabled
            case OverlayDisplayLevel.External: // External
                /*
                 * Intended to simply allow RTSS/HWINFO to run, and let the user configure the overlay within those
                 * tools as they wish
                 */
                break;

            case OverlayDisplayLevel.Minimal: // Minimal
                {
                    using OverlayRow row1 = new();

                    OverlayEntry FPSentry = new("<APP>", "C6");
                    FPSentry.elements.Add(new OverlayEntryElement("<FR>", "FPS"));
                    FPSentry.elements.Add(new OverlayEntryElement("<FT>", "ms"));
                    row1.entries.Add(FPSentry);

                    // add header to row1
                    Content.Add(Header + row1);
                }
                break;

            case OverlayDisplayLevel.Extended: // Extended
                {

                    using OverlayRow row1 = new();

                    using OverlayEntry GPUentry = new("GPU", "C1");
                    AddElementIfNotNull(GPUentry, PlatformManager.LibreHardwareMonitor.GPULoad, "%");
                    AddElementIfNotNull(GPUentry, PlatformManager.LibreHardwareMonitor.GPUPower, "W");
                    row1.entries.Add(GPUentry);

                    using OverlayEntry CPUentry = new("CPU", "C2");
                    AddElementIfNotNull(CPUentry, PlatformManager.LibreHardwareMonitor.CPULoad, "%");
                    AddElementIfNotNull(CPUentry, PlatformManager.LibreHardwareMonitor.CPUPower, "W");
                    row1.entries.Add(CPUentry);

                    using OverlayEntry RAMentry = new("RAM", "C3");
                    AddElementIfNotNull(RAMentry, PlatformManager.LibreHardwareMonitor.MemoryUsage / 1024, "GiB", "{0:0.0}");
                    row1.entries.Add(RAMentry);

                    using OverlayEntry BATTentry = new("BATT", "C5");

                    AddElementIfNotNull(BATTentry, PlatformManager.LibreHardwareMonitor.BatteryCapacity, "%");
                    //AddElementIfNotNull(BATTentry, PlatformManager.LibreHardwareMonitor.BatteryTimeSpan, "mins");
                    BATTentry.elements.Add(new OverlayEntryElement(PlatformManager.LibreHardwareMonitor.BatteryTimeSpan.ToString(@"hh\:mm"), ""));
                    row1.entries.Add(BATTentry);

                    using OverlayEntry FPSentry = new("FPS", "C6");
                    FPSentry.elements.Add(new OverlayEntryElement("<FR>", ""));
                    FPSentry.elements.Add(new OverlayEntryElement("<FT>", "ms"));
                    row1.entries.Add(FPSentry);

                    // add header to row1
                    Content.Add(Header + row1);
                }
                break;

            case OverlayDisplayLevel.Full: // Full
                {

                    using OverlayRow rowGpu = new();
                    using OverlayRow rowCpu = new();
                    using OverlayRow rowFan = new();
                    using OverlayRow rowRam = new();
                    using OverlayRow rowVram = new();
                    using OverlayRow rowBatt = new();
                    //using OverlayRow rowClock = new();
                    using OverlayRow rowTdp = new();
                    using OverlayRow rowFps = new();

                    using OverlayEntry GPUentry = new("GPU", "C1");
                    AddElementIfNotNull(GPUentry, PlatformManager.LibreHardwareMonitor.GPUClock, "MHz");
                    AddElementIfNotNull(GPUentry, PlatformManager.LibreHardwareMonitor.GPULoad, "%");
                    AddElementIfNotNull(GPUentry, PlatformManager.LibreHardwareMonitor.GPUTemp, "°C");
                    AddElementIfNotNull(GPUentry, PlatformManager.LibreHardwareMonitor.GPUPower, "W");
                    rowGpu.entries.Add(GPUentry);

                    using OverlayEntry CPUentry = new("CPU", "C2");
                    AddElementIfNotNull(CPUentry, PlatformManager.LibreHardwareMonitor.CPUClock, "MHz");
                    AddElementIfNotNull(CPUentry, PlatformManager.LibreHardwareMonitor.CPULoad, "%");
                    AddElementIfNotNull(CPUentry, PlatformManager.LibreHardwareMonitor.CPUTemp, "°C");
                    AddElementIfNotNull(CPUentry, PlatformManager.LibreHardwareMonitor.CPUPower, "W");
                    rowCpu.entries.Add(CPUentry);

                    using OverlayEntry RAMentry = new("RAM", "C3");
                    AddElementIfNotNull(RAMentry, PlatformManager.LibreHardwareMonitor.MemoryUsage / 1024, "GB", "{0:0.0}");
                    rowRam.entries.Add(RAMentry);


                    using OverlayEntry VRAMentry = new("VRAM", "C4");
                    AddElementIfNotNull(VRAMentry, PlatformManager.LibreHardwareMonitor.GPUMemoryUsage / 1024, "GB", "{0:0.0}");
                    rowVram.entries.Add(VRAMentry);

                    using OverlayEntry BATTentry = new("BATT", "C5");
                    AddElementIfNotNull(BATTentry, PlatformManager.LibreHardwareMonitor.BatteryCapacity, "%", "{0:0}");
                    AddElementIfNotNull(BATTentry, PlatformManager.LibreHardwareMonitor.BatteryPower, "W", "{0:0.0}");
                    //AddElementIfNotNull(BATTentry, PlatformManager.LibreHardwareMonitor.BatteryTimeSpan, "mins", "{0:0}");
                    BATTentry.elements.Add(new OverlayEntryElement(PlatformManager.LibreHardwareMonitor.BatteryTimeSpan.ToString(@"hh\:mm"), ""));
                    rowBatt.entries.Add(BATTentry);

                    //using OverlayEntry TIMEentry = new("", "C0");
                    //TIMEentry.elements.Add(new OverlayEntryElement("<TIME=%X>", ""));
                    //TIMEentry.elements.Add(new OverlayEntryElement("<TIME=%a %d/%m/%Y>", ""));
                    //TIMEentry.elements.Add(new OverlayEntryElement("<TIME=%I:%M:%S>", "<TIME=%p>"));
                    //TIMEentry.elements.Add(new OverlayEntryElement("<TIME=%I:%M>", "<TIME=%p>"));
                    //rowClock.entries.Add(TIMEentry);

                    using OverlayEntry FPSentry = new("FPS", "C6");
                    FPSentry.elements.Add(new OverlayEntryElement("<FR>", ""));
                    //FPSentry.elements.Add(new OverlayEntryElement("<FT>", "ms"));
                    rowFps.entries.Add(FPSentry);

                    using OverlayEntry TDPentry = new("TDP", "C4");
                    AddElementIfNotNull(TDPentry, PerformanceManager.CurrentTDP, "W");
                    rowTdp.entries.Add(TDPentry);

                    using OverlayEntry FANentry = new("FAN", "C2");
                    AddElementIfNotNull(FANentry, PlatformManager.LibreHardwareMonitor.CPUFanSpeed, "rpm");
                    //AddElementIfNotNull(FANentry, PlatformManager.LibreHardwareMonitor.CPUFanDuty, "%");
                    FANentry.elements.Add(new OverlayEntryElement("<TIME=%I:%M:%S>", "<TIME=%p>"));
                    rowFan.entries.Add(FANentry);


                    Content.Add(Header + Footer + "   " +
                        string.Join("<C4> | <C>", [
                            rowFps.ToString(),
                            rowCpu.ToString() + " " + rowRam.ToString(),
                            rowGpu.ToString() + " " + rowVram.ToString(),
                            rowBatt.ToString() + " " + rowTdp.ToString(),
                            rowFan.ToString() + "   "
                            //rowClock.ToString()
                        ]));
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
            case OverlayDisplayLevel.Custom:
                {
                    for (int i = 0; i < OverlayCount; i++)
                    {
                        var name = OverlayOrder[i];
                        var content = EntryContent(name);
                        if (content == "") continue;
                        Content.Add(content);
                    }

                    // Add header to row1
                    if (Content.Count > 0) Content[0] = Header + Content[0];
                }
                break;

        }

    Exit:
        return string.Join("\n", Content);
    }

    public static void Stop()
    {
        if (!IsInitialized)
            return;

        RefreshTimer.Stop();

        // unhook all processes
        foreach (var osd in onScreenDisplays)
        {
            osd.Value.Update(string.Empty);
            osd.Value.Dispose();
        }

        onScreenDisplays.Clear();

        IsInitialized = false;

        LogManager.LogInformation("{0} has stopped", "OSDManager");
    }

    private static string EntryContent(string name)
    {
        using OverlayRow row = new();
        using OverlayEntry entry = new(name, EntryColor(name), true);
        switch (name.ToUpper())
        {
            case "TIME":
                switch (OverlayTimeLevel)
                {
                    case OverlayEntryLevel.Full:
                    case OverlayEntryLevel.Minimal:
                        //entry.elements.Add(new OverlayEntryElement(DateTime.Now.ToString(), ""));
                        entry.elements.Add(new OverlayEntryElement("<TIME=%X>", ""));
                        break;
                }
                break;
            case "FPS":
                switch (OverlayFPSLevel)
                {
                    case OverlayEntryLevel.Full:
                        entry.elements.Add(new OverlayEntryElement("<FR>", "FPS"));
                        entry.elements.Add(new OverlayEntryElement("<FT>", "ms"));
                        break;
                    case OverlayEntryLevel.Minimal:
                        entry.elements.Add(new OverlayEntryElement("<FR>", "FPS"));
                        break;
                }
                break;
            case "CPU":
                switch (OverlayCPULevel)
                {
                    case OverlayEntryLevel.Full:
                        AddElementIfNotNull(entry, PlatformManager.LibreHardwareMonitor.CPUClock / 1000, "GHz");
                        AddElementIfNotNull(entry, PlatformManager.LibreHardwareMonitor.CPULoad, "%");
                        AddElementIfNotNull(entry, PlatformManager.LibreHardwareMonitor.CPUTemp, "°C");
                        AddElementIfNotNull(entry, PlatformManager.LibreHardwareMonitor.CPUPower, "W");
                        break;
                    case OverlayEntryLevel.Minimal:
                        AddElementIfNotNull(entry, PlatformManager.LibreHardwareMonitor.CPULoad, "%");
                        AddElementIfNotNull(entry, PlatformManager.LibreHardwareMonitor.CPUPower, "W");
                        break;
                }
                break;
            case "RAM":
                switch (OverlayRAMLevel)
                {
                    case OverlayEntryLevel.Full:
                    case OverlayEntryLevel.Minimal:
                        AddElementIfNotNull(entry, PlatformManager.LibreHardwareMonitor.MemoryUsage / 1024, "GB");
                        break;
                }
                break;
            case "GPU":
                switch (OverlayGPULevel)
                {
                    case OverlayEntryLevel.Full:
                        AddElementIfNotNull(entry, PlatformManager.LibreHardwareMonitor.GPUClock / 1000, "GHz");
                        AddElementIfNotNull(entry, PlatformManager.LibreHardwareMonitor.GPULoad, "%");
                        AddElementIfNotNull(entry, PlatformManager.LibreHardwareMonitor.GPUTemp, "°C");
                        AddElementIfNotNull(entry, PlatformManager.LibreHardwareMonitor.GPUPower, "W");
                        break;
                    case OverlayEntryLevel.Minimal:
                        AddElementIfNotNull(entry, PlatformManager.LibreHardwareMonitor.GPULoad, "%");
                        AddElementIfNotNull(entry, PlatformManager.LibreHardwareMonitor.GPUPower, "W");
                        break;
                }
                break;
            case "VRAM":
                switch (OverlayVRAMLevel)
                {
                    case OverlayEntryLevel.Full:
                    case OverlayEntryLevel.Minimal:
                        AddElementIfNotNull(entry, PlatformManager.LibreHardwareMonitor.GPUMemoryUsage / 1024, "GB");
                        break;
                }
                break;
            case "BATT":
                switch (OverlayBATTLevel)
                {
                    case OverlayEntryLevel.Full:
                        AddElementIfNotNull(entry, PlatformManager.LibreHardwareMonitor.BatteryCapacity, "%");
                        AddElementIfNotNull(entry, PlatformManager.LibreHardwareMonitor.BatteryPower, "W");
                        break;
                    case OverlayEntryLevel.Minimal:
                        AddElementIfNotNull(entry, PlatformManager.LibreHardwareMonitor.BatteryCapacity, "%");

                        break;
                }
                break;
        }
        // Skip empty rows
        if (entry.elements.Count == 0) return "";
        row.entries.Add(entry);
        return row.ToString();
    }

    private static string EntryColor(String name)
    {
        // C1: GPU
        // C2: CPU
        // C3: RAM
        // C4: VRAM
        // C5: BATT
        // C6: FPS
        return name.ToUpper() switch
        {
            "FPS" => "C6",
            "CPU" => "C2",
            "GPU" => "C1",
            "RAM" => "C3",
            "VRAM" => "C4",
            "BATT" => "C5",
            _ => "C0",
        };
    }

    private static void AddElementIfNotNull(OverlayEntry entry, float? value, string unit, string format = "{0:00}")
    {
        if (value is float fl && !float.IsNaN(fl))
            entry.elements.Add(new OverlayEntryElement(fl, unit, format));
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

                        if (OverlayLevel != OverlayDisplayLevel.Disabled)
                            RefreshTimer.Start();

                        if (OverlayLevel == OverlayDisplayLevel.External)
                            RefreshTimer.Stop();
                    }
                }
                break;
            case "OnScreenDisplayOrder":
                OverlayOrder = value.ToString().Split(",");
                OverlayCount = OverlayOrder.Length;
                break;
            case "OnScreenDisplayLevel":
                {
                    // set OSD toggle hotkey state
                    SettingsManager.Set("OnScreenDisplayToggle", Convert.ToBoolean(value));

                    // set lastOSDLevel to be used in OSD toggle hotkey
                    SettingsManager.Set("LastOnScreenDisplayLevel", value);
                }
                break;
                /*
                    case "OnScreenDisplayLevel":
                        {
                            OverlayLevel = EnumUtils<OverlayDisplayLevel>.Parse(Convert.ToInt16(value));

                            // set OSD toggle hotkey state
                            SettingsManager.Set("OnScreenDisplayToggle", Convert.ToBoolean(value));

                            if (OverlayLevel != OverlayDisplayLevel.Disabled)
                            {
                                // set lastOSDLevel to be used in OSD toggle hotkey
                                SettingsManager.Set("LastOnScreenDisplayLevel", value);

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
                    case "OnScreenDisplayTimeLevel":
                        OverlayTimeLevel = EnumUtils<OverlayEntryLevel>.Parse(Convert.ToInt16(value));
                        break;
                    case "OnScreenDisplayFPSLevel":
                        OverlayFPSLevel = EnumUtils<OverlayEntryLevel>.Parse(Convert.ToInt16(value));
                        break;
                    case "OnScreenDisplayCPULevel":
                        OverlayCPULevel = EnumUtils<OverlayEntryLevel>.Parse(Convert.ToInt16(value));
                        break;
                    case "OnScreenDisplayRAMLevel":
                        OverlayRAMLevel = EnumUtils<OverlayEntryLevel>.Parse(Convert.ToInt16(value));
                        break;
                    case "OnScreenDisplayGPULevel":
                        OverlayGPULevel = EnumUtils<OverlayEntryLevel>.Parse(Convert.ToInt16(value));
                        break;
                    case "OnScreenDisplayVRAMLevel":
                        OverlayVRAMLevel = EnumUtils<OverlayEntryLevel>.Parse(Convert.ToInt16(value));
                        break;
                    case "OnScreenDisplayBATTLevel":
                        OverlayBATTLevel = EnumUtils<OverlayEntryLevel>.Parse(Convert.ToInt16(value));
                        break;
            */
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

    public OverlayEntryElement(string value, string unit)
    {
        Value = value;
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
        Name = indent ? (name != null && name.Length > 0 ? name + ":\t" : "") : (name != null && name.Length > 0 ? name + ":" : "");

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

            var entryStr = new List<string>();

            foreach (var element in entry.elements)
                entryStr.Add(element.ToString());

            var itemStr = entry.Name + "   " + string.Join(" ", entryStr);
            rowStr.Add(itemStr);
        }

        return string.Join("<C1> | <C>", rowStr);
    }
}