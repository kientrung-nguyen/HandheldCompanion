using HandheldCompanion.Shared;
using HandheldCompanion.Managers;
using HandheldCompanion.Utils;
using Hwinfo.SharedMemory;
using RTSSSharedMemoryNET;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Timers;

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
    public static event InitializedEventHandler Initialized;

    // C1: GPU // 8040
    // C2: CPU // 80FF
    // C3: RAM // FF80C0
    // C4: VRAM // FF80FF
    // C5: BATT // FF8000
    // C6: FPS // FF0000
    private const string Header =
        "<LI=Plugins\\Client\\Overlays\\sample.png>" +
        "<C0=FFFFFF><C1=8000FF><A0=-4><S0=-50><S1=90>";
    //"<C0=FFFFFF><C1=8040><C2=80FF><C3=FF80C0><C4=FF80FF><C5=FF8000><C6=FF0000>" +
    //"<A0=-4><A1=5><A2=-2><A3=-3><A4=-4><A5=-5>" +
    //"<S0=-40><S1=90>";
    private const string HorizontalHypertext = "<P=0,0><L0><C=80000000><B=0,0>\b<C><E=-168,-2,3> {0} <C><S>";
    private const string VerticalHyperText = "<P=0,0><L0><C=80000000><B=0,0>\b<C>\n {0} \n <C><S>";
    //<P1>"<C0=FFFFFF><C1=458A6E><C2=4C8DB2><C3=AD7B95><C4=A369A6><C5=F19F86><C6=D76D76><A0=-4><A1=5><A2=-2><A3=-3><A4=-4><A5=-5><S0=-50><S1=80><P1><M=0,0,0,0><L0><C=64000000><B=0,0>\b<C>";

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
    public static int OverlayOrientation = 0;

    private static readonly Timer RefreshTimer;
    private static int RefreshInterval = 100;

    private static readonly ConcurrentDictionary<int, OSD> onScreenDisplays = new();
    private static AppEntry osdAppEntry;
    private static List<string> Content = new();

    static OSDManager()
    {
        RefreshTimer = new Timer(RefreshInterval) { AutoReset = true };
        RefreshTimer.Elapsed += UpdateOSD;
    }

    public static void Start()
    {
        if (IsInitialized)
            return;

        if (OverlayLevel != 0 && !RefreshTimer.Enabled)
            RefreshTimer.Start();

        // raise events
        switch (ManagerFactory.settingsManager.Status)
        {
            default:
            case ManagerStatus.Initializing:
                ManagerFactory.settingsManager.Initialized += SettingsManager_Initialized;
                break;
            case ManagerStatus.Initialized:
                QuerySettings();
                break;
        }

        switch (ManagerFactory.platformManager.Status)
        {
            default:
            case ManagerStatus.Initializing:
                ManagerFactory.platformManager.Initialized += PlatformManager_Initialized;
                break;
            case ManagerStatus.Initialized:
                QueryPlatforms();
                break;
        }

        IsInitialized = true;
        Initialized?.Invoke();

        LogManager.LogInformation("{0} has started", "OSDManager");
    }

    private static void QueryPlatforms()
    {
        // manage events
        PlatformManager.RTSS.Hooked += RTSS_Hooked;
        PlatformManager.RTSS.Unhooked += RTSS_Unhooked;

        AppEntry appEntry = PlatformManager.RTSS.GetAppEntry();
        if (appEntry is not null)
            RTSS_Hooked(appEntry);
    }

    private static void PlatformManager_Initialized()
    {
        QueryPlatforms();
    }

    private static void SettingsManager_Initialized()
    {
        QuerySettings();
    }

    private static void QuerySettings()
    {
        // manage events
        ManagerFactory.settingsManager.SettingValueChanged += SettingsManager_SettingValueChanged;

        // raise events
        SettingsManager_SettingValueChanged("OnScreenDisplayRefreshRate", ManagerFactory.settingsManager.Get<double>("OnScreenDisplayRefreshRate"), false);
        SettingsManager_SettingValueChanged("OnScreenDisplayLevel", ManagerFactory.settingsManager.Get<string>("OnScreenDisplayLevel"), false);
        SettingsManager_SettingValueChanged("OnScreenDisplayOrder", ManagerFactory.settingsManager.Get<string>("OnScreenDisplayOrder"), false);
        SettingsManager_SettingValueChanged("OnScreenDisplayTimeLevel", ManagerFactory.settingsManager.Get<string>("OnScreenDisplayTimeLevel"), false);
        SettingsManager_SettingValueChanged("OnScreenDisplayFPSLevel", ManagerFactory.settingsManager.Get<string>("OnScreenDisplayFPSLevel"), false);
        SettingsManager_SettingValueChanged("OnScreenDisplayCPULevel", ManagerFactory.settingsManager.Get<string>("OnScreenDisplayCPULevel"), false);
        SettingsManager_SettingValueChanged("OnScreenDisplayRAMLevel", ManagerFactory.settingsManager.Get<string>("OnScreenDisplayRAMLevel"), false);
        SettingsManager_SettingValueChanged("OnScreenDisplayGPULevel", ManagerFactory.settingsManager.Get<string>("OnScreenDisplayGPULevel"), false);
        SettingsManager_SettingValueChanged("OnScreenDisplayVRAMLevel", ManagerFactory.settingsManager.Get<string>("OnScreenDisplayVRAMLevel"), false);
        SettingsManager_SettingValueChanged("OnScreenDisplayBATTLevel", ManagerFactory.settingsManager.Get<string>("OnScreenDisplayBATTLevel"), false);
    }

    public static void Stop()
    {
        if (!IsInitialized)
            return;

        RefreshTimer.Stop();

        // unhook all processes
        foreach (var osd in onScreenDisplays)
            RTSS_Unhooked(osd.Key);

        onScreenDisplays.Clear();

        // manage events
        ManagerFactory.settingsManager.SettingValueChanged -= SettingsManager_SettingValueChanged;
        ManagerFactory.settingsManager.Initialized -= SettingsManager_Initialized;
        PlatformManager.RTSS.Hooked -= RTSS_Hooked;
        PlatformManager.RTSS.Unhooked -= RTSS_Unhooked;

        IsInitialized = false;

        LogManager.LogInformation("{0} has stopped", "OSDManager");
    }

    private static void RTSS_Unhooked(int processId)
    {
        try
        {
            // clear previous display
            if (onScreenDisplays.TryRemove(processId, out var osd))
            {
                osd.Update(string.Empty);
                osd.Dispose();
            }
        }
        catch { }
    }

    private static void RTSS_Hooked(AppEntry appEntry)
    {
        if (appEntry is null)
            return;

        try
        {
            // update foreground id
            osdAppEntry = appEntry;

            // only create a new OSD if needed
            //
            if (!onScreenDisplays.TryGetValue(appEntry.ProcessId, out var osd))
                onScreenDisplays[osdAppEntry.ProcessId] = new OSD(osdAppEntry.Name);
        }
        catch { }
    }

    private static void UpdateOSD(object? sender, ElapsedEventArgs e)
    {
        if (OverlayLevel == OverlayDisplayLevel.Disabled)
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
            catch { }
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

                    using OverlayEntry FPSentry = new("<APP>", "FF0000");
                    FPSentry.elements.Add(new OverlayEntryElement("<FR>", "FPS"));
                    FPSentry.elements.Add(new OverlayEntryElement("<FT>", "ms"));
                    row1.entries.Add(FPSentry);

                    // add header to row1
                    Content.Add(row1.ToString());
                }
                break;

            case OverlayDisplayLevel.Extended: // Extended
                {

                    using OverlayRow rowGpu = new();
                    using OverlayRow rowCpu = new();

                    using OverlayRow rowBatt = new();
                    using OverlayRow rowRam = new();
                    using OverlayRow rowFps = new();

                    using OverlayEntry GPUentry = new("GPU", "50E000");
                    AddElementIfNotNull(GPUentry, PlatformManager.LibreHardware.GetGPULoad(), "%");
                    AddElementIfNotNull(GPUentry, PlatformManager.LibreHardware.GetGPUPower(), "W");
                    rowGpu.entries.Add(GPUentry);

                    using OverlayEntry CPUentry = new("CPU", "80FF");
                    AddElementIfNotNull(CPUentry, PlatformManager.LibreHardware.GetCPULoad(), "%");
                    AddElementIfNotNull(CPUentry, PlatformManager.LibreHardware.GetCPUPower(), "W");
                    rowCpu.entries.Add(CPUentry);

                    using OverlayEntry RAMentry = new("RAM", "FF80C0");
                    AddElementIfNotNull(RAMentry, PlatformManager.LibreHardware.GetMemoryUsage() / 1024, "GB", "{0:00.0}");
                    rowRam.entries.Add(RAMentry);

                    using OverlayEntry BATTentry = new("BATT", "FF8000");
                    AddElementIfNotNull(BATTentry, PlatformManager.LibreHardware.GetBatteryLevel(), "%");
                    AddElementIfNotNull(BATTentry, PlatformManager.LibreHardware.GetBatteryTimeSpan()?.ToString(@"hh\:mm"), "");
                    rowBatt.entries.Add(BATTentry);

                    using OverlayEntry FPSentry = new("FPS", "C6");
                    FPSentry.elements.Add(new OverlayEntryElement("<FR>", "fps"));
                    FPSentry.elements.Add(new OverlayEntryElement("<FT>", "ms"));
                    rowFps.entries.Add(FPSentry);

                    // add header to row1
                    Content.AddRange([
                        rowFps.ToString(),
                        rowGpu.ToString(),
                        rowCpu.ToString(),
                        rowRam.ToString(),
                        rowBatt.ToString()
                        ]);
                }
                break;

            case OverlayDisplayLevel.Full: // Full
                {
                    using OverlayRow rowGpu = new();
                    using OverlayRow rowCpu = new();
                    using OverlayRow rowFan = new();
                    using OverlayRow rowBatt = new();
                    using OverlayRow rowTdp = new();
                    using OverlayRow rowFps = new();

                    using OverlayEntry GPUentry = new("GPU", "50E000");
                    AddElementIfNotNull(GPUentry, PlatformManager.LibreHardware.GetGPUClock() / 1000, "GHz", "{0:0.0}");
                    AddElementIfNotNull(GPUentry, PlatformManager.LibreHardware.GetGPULoad(), "%");
                    AddElementIfNotNull(GPUentry, PlatformManager.LibreHardware.GetGPUTemperature(), "°C");
                    AddElementIfNotNull(GPUentry, PlatformManager.LibreHardware.GetGPUPower(), "W");
                    AddElementIfNotNull(GPUentry, PlatformManager.LibreHardware.GetGPUMemoryDedicated() / 1024, "GB", "{0:00.0}", "FF80FF");
                    rowGpu.entries.Add(GPUentry);

                    using OverlayEntry CPUentry = new("CPU", "80FF");
                    AddElementIfNotNull(CPUentry, PlatformManager.LibreHardware.GetCPUClock() / 1000, "GHz", "{0:0.0}");
                    AddElementIfNotNull(CPUentry, PlatformManager.LibreHardware.GetCPULoad(), "%");
                    AddElementIfNotNull(CPUentry, PlatformManager.LibreHardware.GetCPUTemperature(), "°C");
                    AddElementIfNotNull(CPUentry, PlatformManager.LibreHardware.GetCPUPower(), "W");
                    AddElementIfNotNull(CPUentry, PlatformManager.LibreHardware.GetMemoryUsage() / 1024, "GB", "{0:00.0}", "FF80C0");
                    rowCpu.entries.Add(CPUentry);

                    using OverlayEntry BATTentry = new("BATT", "FF8000");
                    AddElementIfNotNull(BATTentry, PlatformManager.LibreHardware.GetBatteryLevel(), "%", "{0:00}");
                    AddElementIfNotNull(BATTentry, PlatformManager.LibreHardware.GetBatteryPower(), "W", "{0:00}");
                    AddElementIfNotNull(BATTentry, PlatformManager.LibreHardware.GetBatteryTimeSpan()?.ToString(@"hh\:mm"), "");
                    BATTentry.elements.Add(new OverlayEntryElement("<TIME=%I:%M:%S>", "<TIME=%p>"));
                    rowBatt.entries.Add(BATTentry);

                    /*
                    using OverlayEntry TIMEentry = new("", "FF80C0");
                    AddElementIfNotNull(TIMEentry, PlatformManager.LibreHardware.CPUFanSpeed, "rpm");
                    TIMEentry.elements.Add(new OverlayEntryElement("<TIME=%I:%M:%S>", "<TIME=%p>"));

                    //TIMEentry.elements.Add(new OverlayEntryElement("<TIME=%X>", ""));
                    //TIMEentry.elements.Add(new OverlayEntryElement("<TIME=%a %d/%m/%Y>", ""));
                    //TIMEentry.elements.Add(new OverlayEntryElement("<TIME=%I:%M:%S>", "<TIME=%p>"));
                    //TIMEentry.elements.Add(new OverlayEntryElement("<TIME=%I:%M>", "<TIME=%p>"));
                    rowTime.entries.Add(TIMEentry);
                    */

                    using OverlayEntry FPSentry = new("FPS", "FF0000");
                    FPSentry.elements.Add(new OverlayEntryElement("<FR>", "fps"));
                    FPSentry.elements.Add(new OverlayEntryElement("<FT>", "ms"));
                    rowFps.entries.Add(FPSentry);

                    using OverlayEntry TDPentry = new("TDP", "363D8B");
                    AddElementIfNotNull(TDPentry, PerformanceManager.CurrentTDP, "W");
                    AddElementIfNotNull(TDPentry, PlatformManager.LibreHardware.CPUFanSpeed, "rpm");

                    rowTdp.entries.Add(TDPentry);
                    Content.AddRange(
                        [
                            rowFps.ToString(),
                            rowGpu.ToString(),
                            rowCpu.ToString(),
                            rowTdp.ToString(),
                            rowBatt.ToString()
                        ]
                        );

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
                    //if (Content.Count > 0) Content[0] = Content[0];
                }
                break;

        }


        return Header + string.Format(
            OverlayOrientation == 0
            ? HorizontalHypertext
            : VerticalHyperText, string.Join(
                OverlayOrientation == 0 ? "<C=FF8000>   <C>" : " \n ", Content));
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
                        AddElementIfNotNull(entry, PlatformManager.LibreHardware.GetCPUClock() / 1000, "GHz", "{0:0.0}");
                        AddElementIfNotNull(entry, PlatformManager.LibreHardware.GetCPULoad(), "%");
                        AddElementIfNotNull(entry, PlatformManager.LibreHardware.GetCPUTemperature(), "°C");
                        AddElementIfNotNull(entry, PlatformManager.LibreHardware.GetCPUPower(), "W");
                        break;
                    case OverlayEntryLevel.Minimal:
                        AddElementIfNotNull(entry, PlatformManager.LibreHardware.GetCPULoad(), "%");
                        AddElementIfNotNull(entry, PlatformManager.LibreHardware.GetCPUPower(), "W");
                        break;
                }
                break;
            case "RAM":
                switch (OverlayRAMLevel)
                {
                    case OverlayEntryLevel.Full:
                    case OverlayEntryLevel.Minimal:
                        AddElementIfNotNull(entry, PlatformManager.LibreHardware.GetMemoryUsage() / 1024, "GB", "{0:00.0}");
                        break;
                }
                break;
            case "GPU":
                switch (OverlayGPULevel)
                {
                    case OverlayEntryLevel.Full:
                        AddElementIfNotNull(entry, PlatformManager.LibreHardware.GetGPUClock() / 1000, "GHz", "{0:0.0}");
                        AddElementIfNotNull(entry, PlatformManager.LibreHardware.GetGPULoad(), "%");
                        AddElementIfNotNull(entry, PlatformManager.LibreHardware.GetGPUTemperature(), "°C");
                        AddElementIfNotNull(entry, PlatformManager.LibreHardware.GetGPUPower(), "W");
                        break;
                    case OverlayEntryLevel.Minimal:
                        AddElementIfNotNull(entry, PlatformManager.LibreHardware.GetGPULoad(), "%");
                        AddElementIfNotNull(entry, PlatformManager.LibreHardware.GetGPUPower(), "W");
                        break;
                }
                break;
            case "VRAM":
                switch (OverlayVRAMLevel)
                {
                    case OverlayEntryLevel.Full:
                    case OverlayEntryLevel.Minimal:
                        AddElementIfNotNull(entry, PlatformManager.LibreHardware.GetGPUMemoryDedicated() / 1024, "GB", "{0:00.0}");
                        break;
                }
                break;
            case "BATT":
                switch (OverlayBATTLevel)
                {
                    case OverlayEntryLevel.Full:
                        AddElementIfNotNull(entry, PlatformManager.LibreHardware.GetBatteryLevel(), "%");
                        AddElementIfNotNull(entry, PlatformManager.LibreHardware.GetBatteryPower(), "W");
                        break;
                    case OverlayEntryLevel.Minimal:
                        AddElementIfNotNull(entry, PlatformManager.LibreHardware.GetBatteryLevel(), "%");

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
        // C1: GPU // 50E000
        // C2: CPU // 80FF
        // C3: RAM // FF80C0
        // C4: VRAM // FF80FF
        // C5: BATT // FF8000
        // C6: FPS // FF0000
        return name.ToUpper() switch
        {
            "FPS" => "FF0000",
            "CPU" => "80FF",
            "GPU" => "8040",
            "RAM" => "FF80C0",
            "VRAM" => "FF80FF",
            "BATT" => "FF8000",
            _ => "FFFFFF",
        };
    }

    private static void AddElementIfNotNull(OverlayEntry entry, object? value, string unit, string format = "{0:00}", string colorScheme = "")
    {
        switch (value)
        {
            case float fl when !float.IsNaN(fl):
                entry.elements.Add(new OverlayEntryElement(fl, unit, format, colorScheme));
                break;
            case string str:
                entry.elements.Add(new OverlayEntryElement(str, unit));
                break;
        }
    }

    private static void SettingsManager_SettingValueChanged(string name, object value, bool temporary)
    {
        switch (name)
        {
            case "OnScreenDisplayRefreshRate":
                {
                    RefreshInterval = Convert.ToInt32(value);
                    RefreshTimer.Interval = RefreshInterval;
                    if (RefreshTimer.Enabled)
                    {
                        RefreshTimer.Stop();
                        RefreshTimer.Start();
                    }
                }
                break;

            case "OnScreenDisplayLevel":
                {
                    OverlayLevel = EnumUtils<OverlayDisplayLevel>.Parse(Convert.ToInt16(value));

                    // set OSD toggle hotkey state
                    ManagerFactory.settingsManager.Set("OnScreenDisplayToggle", value);

                    if (OverlayLevel > 0)
                    {
                        // set lastOSDLevel to be used in OSD toggle hotkey
                        ManagerFactory.settingsManager.Set("LastOnScreenDisplayLevel", value);

                        if (OverlayLevel == OverlayDisplayLevel.External)
                        {
                            // No need to update OSD in External
                            RefreshTimer.Stop();

                            // Remove previous UI in External
                            foreach (var pair in onScreenDisplays)
                                pair.Value.Update(string.Empty);

                        }
                        else
                        {
                            // Other modes need the refresh timer to update OSD
                            if (!RefreshTimer.Enabled)
                                RefreshTimer.Start();
                        }
                    }
                    else
                    {
                        RefreshTimer.Stop();

                        // clear UI on stop
                        foreach (var pair in onScreenDisplays)
                            pair.Value.Update(string.Empty);

                    }
                }
                break;

            case "OnScreenDisplayOrder":
                OverlayOrder = value.ToString().Split(",");
                OverlayCount = OverlayOrder.Length;
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
        }
    }
}

public struct OverlayEntryElement
{
    public string Value { get; set; }
    public string SzUnit { get; set; }

    public override string ToString() => string.Format("<C0>{0:00}<C><C0><S1>{1}<S><C>", Value, SzUnit);

    public OverlayEntryElement(float value, string unit, string format = "{0:00}", string colorScheme = "")
    {
        if (value == 0f)
            Value = string.Format("{0:0}", value);
        else
        {
            var leadingZeroCount = 0;
            var input = string.Format(format, value);
            // Iterate through the string and count leading zeros that are not followed by a '.'
            while (leadingZeroCount < input.Length && input[leadingZeroCount] == '0')
            {
                if (leadingZeroCount + 1 < input.Length && input[leadingZeroCount + 1] == '.')
                {
                    break;  // Stop if the next character is a '.'
                }
                leadingZeroCount++;
            }
            //Value = string.Format(format, value).TrimStart('0').PadLeft(string.Format(format, value).Length, ' ');
            //Value = string.Format(format, value);
            // Replace the leading zeros with spaces
            Value = input[leadingZeroCount..].PadLeft(input.Length, ' ');
        }

        SzUnit = unit;
        if (colorScheme != null && colorScheme.Length > 0)
        {
            Value = "<C=" + colorScheme + ">" + Value + "<C>";
            SzUnit = "<C=" + colorScheme + ">" + unit + "<C>";
        }
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
    public List<OverlayEntryElement> elements = [];

    public OverlayEntry(string name, string colorScheme = "", bool indent = false)
    {
        Name = indent ? (name != null && name.Length > 0 ? (name + ":").PadRight(5) + "\t" : "") : (name != null && name.Length > 0 ? (name + ":").PadRight(5) : "");

        if (colorScheme != null && colorScheme.Length > 0)
            Name = "<C=" + colorScheme + ">" + Name + "<C>";
    }

    ~OverlayEntry()
    {
        Dispose();
    }

    public string Name { get; set; }

    public void Dispose()
    {
        elements?.Clear();
        elements = null;
    }
}

public class OverlayRow : IDisposable
{
    public List<OverlayEntry> entries = [];

    ~OverlayRow()
    {
        Dispose();
    }

    public void Dispose()
    {
        entries?.Clear();
        entries = null;
    }

    public override string ToString()
    {
        List<string> rowStr = [];

        foreach (var entry in entries)
        {
            if (entry.elements is null || entry.elements.Count == 0)
                continue;

            var entryStr = new List<string>();

            foreach (var element in entry.elements)
                entryStr.Add(element.ToString());

            var itemStr = entry.Name + "  " + string.Join(" ", entryStr);
            rowStr.Add(itemStr);
        }

        return string.Join("<C1>   <C>", rowStr);
    }
}