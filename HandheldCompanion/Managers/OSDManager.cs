using HandheldCompanion.Platforms.Misc;
using HandheldCompanion.Shared;
using HandheldCompanion.Utils;
using RTSSSharedMemoryNET;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.NetworkInformation;
using System.Text;
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

    // Steam Deck colors (RTSS hex format - BGR)
    private const string COLOR_STEAM_BLUE = "5858F2";
    private const string COLOR_GREEN = "86DC66";
    private const string COLOR_ORANGE = "47B3FF";
    private const string COLOR_RED = "de3450";
    private const string COLOR_GRAY = "d7d7d7";

    private const string GPU_COLOR = "00ac38";
    private const string RAM_COLOR = "b86fe5";
    private const string CPU_COLOR = "e0d35b";
    private const string FPS_COLOR = "fb3d60";


    private const string HEADER =
        $"<LI=Plugins\\Client\\Overlays\\sample.png>" +
        $"<C0={COLOR_GRAY}><C1={COLOR_STEAM_BLUE}><A0=-4><S0=-50><S1=100><S2=60>";

    private const string HORIZONTAL_HYPERTEXT = "<P=0,0><L0><C=80000000><B=0,0>\b<C><E=-168,-2,3> {0} <C><S>";
    private const string VERTICAL_HYPERTEXT = "<P=0,0><L0><C=80000000><B=0,0>\b<C>\n {0} \n <C><S>";
    private const string HORIZONTAL_SEPARATOR = "<C1> | <C>";
    private const string VERTICAL_SEPARATOR = " \n ";

    private const int DEFAULT_REFRESH_INTERVAL_MS = 100;
    private const int MB_TO_BYTES = 1024 * 1024;

    private static bool IsInitialized;

    // Configuration
    public static OverlayDisplayLevel OverlayLevel = OverlayDisplayLevel.Disabled;
    public static string[] OverlayOrder = Array.Empty<string>();
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
    private static int RefreshInterval = DEFAULT_REFRESH_INTERVAL_MS;

    private static readonly ConcurrentDictionary<int, OSD> onScreenDisplays = new();
    private static AppEntry osdAppEntry;

    // Cached hardware metrics (protected by metricsLock)
    private static readonly object metricsLock = new object();
    private static HardwareMetrics currentMetrics = new();

    // Reusable StringBuilder to reduce allocations
    private static readonly StringBuilder contentBuilder = new StringBuilder(512);

    static OSDManager()
    {
        RefreshTimer = new Timer(RefreshInterval) { AutoReset = true };
        RefreshTimer.Elapsed += UpdateOSD;
    }

    public static void Start()
    {
        if (IsInitialized)
            return;

        if (OverlayLevel != OverlayDisplayLevel.Disabled && !RefreshTimer.Enabled)
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

        PlatformManager.LibreHardware.HardwareMetricsChanged += LibreHardware_HardwareMetricsChanged;
        var appEntry = PlatformManager.RTSS.GetAppEntry();
        if (appEntry is not null)
            RTSS_Hooked(appEntry);
    }

    private static void LibreHardware_HardwareMetricsChanged(HardwareMetrics metrics)
    {
        lock (metricsLock)
        {
            currentMetrics = metrics;
        }
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
        SettingsManager_SettingValueChanged("OnScreenDisplayRefreshRate",
            ManagerFactory.settingsManager.Get<double>("OnScreenDisplayRefreshRate"), false);
        SettingsManager_SettingValueChanged("OnScreenDisplayLevel",
            ManagerFactory.settingsManager.Get<string>("OnScreenDisplayLevel"), false);
        SettingsManager_SettingValueChanged("OnScreenDisplayOrder",
            ManagerFactory.settingsManager.Get<string>("OnScreenDisplayOrder"), false);
        SettingsManager_SettingValueChanged("OnScreenDisplayTimeLevel",
            ManagerFactory.settingsManager.Get<string>("OnScreenDisplayTimeLevel"), false);
        SettingsManager_SettingValueChanged("OnScreenDisplayFPSLevel",
            ManagerFactory.settingsManager.Get<string>("OnScreenDisplayFPSLevel"), false);
        SettingsManager_SettingValueChanged("OnScreenDisplayCPULevel",
            ManagerFactory.settingsManager.Get<string>("OnScreenDisplayCPULevel"), false);
        SettingsManager_SettingValueChanged("OnScreenDisplayRAMLevel",
            ManagerFactory.settingsManager.Get<string>("OnScreenDisplayRAMLevel"), false);
        SettingsManager_SettingValueChanged("OnScreenDisplayGPULevel",
            ManagerFactory.settingsManager.Get<string>("OnScreenDisplayGPULevel"), false);
        SettingsManager_SettingValueChanged("OnScreenDisplayVRAMLevel",
            ManagerFactory.settingsManager.Get<string>("OnScreenDisplayVRAMLevel"), false);
        SettingsManager_SettingValueChanged("OnScreenDisplayBATTLevel",
            ManagerFactory.settingsManager.Get<string>("OnScreenDisplayBATTLevel"), false);
    }

    public static void Stop()
    {
        if (!IsInitialized)
            return;

        RefreshTimer?.Stop();

        // unhook all processes
        foreach (var osd in onScreenDisplays)
            RTSS_Unhooked(osd.Key);

        onScreenDisplays.Clear();

        // manage events
        ManagerFactory.settingsManager.SettingValueChanged -= SettingsManager_SettingValueChanged;
        ManagerFactory.settingsManager.Initialized -= SettingsManager_Initialized;
        ManagerFactory.platformManager.Initialized -= PlatformManager_Initialized;

        PlatformManager.RTSS.Hooked -= RTSS_Hooked;
        PlatformManager.RTSS.Unhooked -= RTSS_Unhooked;

        PlatformManager.LibreHardware.HardwareMetricsChanged -= LibreHardware_HardwareMetricsChanged;

        // Dispose timer
        RefreshTimer?.Dispose();

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
        catch (Exception ex)
        {
            LogManager.LogError("Error unhooking RTSS process {0}: {1}", processId, ex.Message);
        }
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
            if (!onScreenDisplays.TryGetValue(appEntry.ProcessId, out var osd))
                onScreenDisplays[appEntry.ProcessId] = new OSD(appEntry.Name);
        }
        catch (Exception ex)
        {
            LogManager.LogError("Error hooking RTSS to process {0}: {1}", appEntry.ProcessId, ex.Message);
        }
    }

    private static void UpdateOSD(object? sender, ElapsedEventArgs e)
    {
        if (OverlayLevel == OverlayDisplayLevel.Disabled)
            return;

        try
        {
            var currentApp = osdAppEntry;

            foreach (var osd in onScreenDisplays)
            {
                var processId = osd.Key;
                var processOSD = osd.Value;

                try
                {
                    //processOSD.DrawFrametimeGraph(10, 1, 10, 50);
                    //processOSD.DrawFrametimeGraph(10, 2, 10, 50);
                    string content = (currentApp is not null && currentApp.ProcessId == processId)
                        ? Draw(processId)
                        : string.Empty;

                    processOSD.Update(content);
                }
                catch (Exception ex)
                {
                    LogManager.LogError("Error updating OSD for process {0}: {1}", processId, ex.ToString());
                }
            }
        }
        catch (Exception ex)
        {
            LogManager.LogError("Error in OSD update cycle: {0}", ex.Message);
        }
    }

    private static string Draw(int processId)
    {
        // Use StringBuilder for efficient string building
        contentBuilder.Clear();

        var rows = new List<string>();

        if (onScreenDisplays.TryGetValue(processId, out var osd) && osd != null)
        {
            switch (OverlayLevel)
            {
                default:
                case OverlayDisplayLevel.Disabled:
                case OverlayDisplayLevel.External:
                    // Intended to simply allow RTSS/HWINFO to run
                    break;

                case OverlayDisplayLevel.Minimal:
                    rows.Add(osd.BuildMinimalOverlay());
                    break;

                case OverlayDisplayLevel.Extended:
                    rows.AddRange(osd.BuildExtendedOverlay(currentMetrics));
                    break;

                case OverlayDisplayLevel.Full:
                    rows.AddRange(osd.BuildFullOverlay(currentMetrics));
                    break;

                case OverlayDisplayLevel.Custom:
                    rows.AddRange(osd.BuildCustomOverlay(currentMetrics));
                    break;
            }
        }
        if (rows.Count == 0)
            return string.Empty;

        var separator = OverlayOrientation == 0 ? HORIZONTAL_SEPARATOR : VERTICAL_SEPARATOR;
        var template = OverlayOrientation == 0 ? HORIZONTAL_HYPERTEXT : VERTICAL_HYPERTEXT;

        return HEADER + string.Format(template, string.Join(separator, rows));
    }

    private static string BuildMinimalOverlay(this OSD osd)
    {
        using OverlayRow row = new();
        using OverlayEntry fpsEntry = new("<APP>", "FF0000");
        fpsEntry.elements.Add(new OverlayEntryElement("<FR>", "FPS"));
        fpsEntry.elements.Add(new OverlayEntryElement("<FT>", "ms"));
        row.entries.Add(fpsEntry);
        return row.ToString();
    }

    private static List<string> BuildExtendedOverlay(this OSD osd, HardwareMetrics metrics)
    {
        var rows = new List<string>();

        using (OverlayRow rowFps = new())
        {
            using OverlayEntry fpsEntry = new("FPS", "C6");
            fpsEntry.elements.Add(new OverlayEntryElement("<FR>", "fps"));
            fpsEntry.elements.Add(new OverlayEntryElement("<FT>", "ms"));
            rowFps.entries.Add(fpsEntry);
            rows.Add(rowFps.ToString());
        }

        using (OverlayRow rowGpu = new())
        {
            using OverlayEntry gpuEntry = new("GPU", "50E000");
            AddElementIfNotNull(gpuEntry, metrics.GpuLoad, "%");
            AddElementIfNotNull(gpuEntry, metrics.GpuPower, "W");
            rowGpu.entries.Add(gpuEntry);
            rows.Add(rowGpu.ToString());
        }

        using (OverlayRow rowCpu = new())
        {
            using OverlayEntry cpuEntry = new("CPU", "80FF");
            AddElementIfNotNull(cpuEntry, metrics.CpuLoad, "%");
            AddElementIfNotNull(cpuEntry, metrics.CpuPower, "W");
            rowCpu.entries.Add(cpuEntry);
            rows.Add(rowCpu.ToString());
        }

        using (OverlayRow rowRam = new())
        {
            using OverlayEntry ramEntry = new("RAM", "FF80C0");
            AddElementIfNotNull(ramEntry, NormalizeBytes(metrics.MemUsed * MB_TO_BYTES));
            rowRam.entries.Add(ramEntry);
            rows.Add(rowRam.ToString());
        }

        using (OverlayRow rowBatt = new())
        {
            using OverlayEntry battEntry = new("BATT", "FF8000");
            AddElementIfNotNull(battEntry, metrics.BattCapacity, "%");
            AddElementIfNotNull(battEntry, PlatformManager.LibreHardware.GetBatteryTimeSpan().ToString(@"hh\:mm"), "");
            rowBatt.entries.Add(battEntry);
            rows.Add(rowBatt.ToString());
        }

        return rows;
    }

    private static uint DrawGraph(this OSD osd, ref uint dwObjectOffset, float[]? lpBuffer, uint dwBufferPos, uint dwBufferSize, int dwWidth, int dwHeight, float fltMin, float fltMax, EMBEDDED_OBJECT_GRAPH dwFlags = EMBEDDED_OBJECT_GRAPH.FLAG_DEFAULT)
    {
        uint dwObjectSize = osd.EmbedGraph(dwObjectOffset, lpBuffer, dwBufferPos, dwBufferSize, -dwWidth, -dwHeight, 1, fltMin, fltMax, dwFlags);
        if (dwObjectSize > 0)
        {
            uint dwObjectUint = dwObjectOffset;
            //print embedded object
            dwObjectOffset += dwObjectSize;
            return dwObjectUint;
        }
        return dwObjectOffset;
    }

    private static List<string> BuildFullOverlay(this OSD osd, HardwareMetrics metrics)
    {
        var rows = new List<string>();
        uint dwObjectOffset = 0;
        using (OverlayRow rowFps = new())
        {
            using OverlayEntry fpsEntry = new("<APP>", FPS_COLOR);

            //fpsEntry.elements.Add(new OverlayEntryElement($"<OBJ={osd.DrawGraph(ref dwObjectOffset, null, 0, 0, 10, 1, 0.0f, 65.0f, EMBEDDED_OBJECT_GRAPH.FLAG_FRAMERATE):X8}><FR>", "fps"));
            //fpsEntry.elements.Add(new OverlayEntryElement($"<OBJ={osd.DrawGraph(ref dwObjectOffset, null, 0, 0, 10, 1, 0.0f, 50000.0f, EMBEDDED_OBJECT_GRAPH.FLAG_FRAMETIME):X8}><FT>", "ms"));
            fpsEntry.elements.Add(new OverlayEntryElement($"<FR>", ""));
            rowFps.entries.Add(fpsEntry);
            rows.Add(rowFps.ToString());
        }


        using (OverlayRow rowCpu = new())
        {
            using OverlayEntry cpuEntry = new("CPU", CPU_COLOR);
            AddElementIfNotNull(cpuEntry,
                metrics.CpuLoad,
                metrics.CpuLoadMax, "%",
                metrics.CpuLoad < 70 ? "" : metrics.CpuLoad < 85 ? COLOR_ORANGE : COLOR_RED);
            AddElementIfNotNull(cpuEntry, $"<OBJ={osd.DrawGraph(ref dwObjectOffset, currentMetrics.CpuLoadCores, (uint)currentMetrics.HISTORY_POSITION, (uint)HardwareMetrics.MAX_CORES, 10, 1, 0.0f, 100.0f, EMBEDDED_OBJECT_GRAPH.FLAG_FILLED):X8}>", "");
            AddElementIfNotNull(cpuEntry,
                NormalizeClock(metrics.CpuClock),
                NormalizeClock(metrics.CpuClockMax));
            AddElementIfNotNull(cpuEntry, metrics.CpuTemp, "°C",
                metrics.CpuTemp < 60 ? "" : metrics.CpuTemp < 70 ? COLOR_ORANGE : COLOR_RED);
            AddElementIfNotNull(cpuEntry, metrics.CpuPower, "W");
            AddElementIfNotNull(cpuEntry,
                NormalizeBytes(metrics.MemUsed * MB_TO_BYTES),
                NormalizeBytes((metrics.MemUsed + metrics.MemAvailable) * MB_TO_BYTES), COLOR_STEAM_BLUE);
            rowCpu.entries.Add(cpuEntry);
            rows.Add(rowCpu.ToString());
        }

        using (OverlayRow rowGpu = new())
        {
            using OverlayEntry gpuEntry = new("GPU", GPU_COLOR);
            AddElementIfNotNull(gpuEntry, metrics.GpuLoad, "%",
                metrics.GpuLoad < 75 ? "" : metrics.GpuLoad < 85 ? COLOR_ORANGE : COLOR_RED);
            AddElementIfNotNull(gpuEntry,
                NormalizeClock(metrics.GpuClock));
            AddElementIfNotNull(gpuEntry, metrics.GpuTemp, "°C",
                metrics.GpuTemp < 60 ? "" : metrics.GpuTemp < 70 ? COLOR_ORANGE : COLOR_RED);
            AddElementIfNotNull(gpuEntry, metrics.GpuPower, "W");
            AddElementIfNotNull(gpuEntry,
                NormalizeBytes((metrics.GpuMemDedicated + currentMetrics.GpuMemShared) * MB_TO_BYTES),
                NormalizeBytes((metrics.GpuMemDedicated + currentMetrics.GpuMemDedicatedAvailable) * MB_TO_BYTES), COLOR_STEAM_BLUE);
            rowGpu.entries.Add(gpuEntry);
            rows.Add(rowGpu.ToString());
        }


        using (OverlayRow rowTdp = new())
        {
            using OverlayEntry tdpEntry = new("TDP", "363D8B");
            AddElementIfNotNull(tdpEntry, PerformanceManager.CurrentTDP <= 0 ? float.NaN : PerformanceManager.CurrentTDP, "W");
            AddElementIfNotNull(tdpEntry, currentMetrics.FanSpeed, "rpm");
            rowTdp.entries.Add(tdpEntry);
            rows.Add(rowTdp.ToString());
        }

        using (OverlayRow rowBatt = new())
        {
            using OverlayEntry battEntry = new("BATT", "FF8000");
            AddElementIfNotNull(battEntry, metrics.BattCapacity, "%",
                metrics.BattCapacity >= 30 ? COLOR_GREEN : metrics.BattCapacity > 20 ? COLOR_ORANGE : COLOR_RED);
            AddElementIfNotNull(battEntry, metrics.BattPower, "W");
            AddElementIfNotNull(battEntry, !float.IsNaN(metrics.BattTime) ? TimeSpan.FromMinutes(metrics.BattTime).ToString("hh\\h\\ mm\\m") : "", "");
            battEntry.elements.Add(new OverlayEntryElement("<TIME=%I:%M:%S>", "<TIME=%p>"));
            rowBatt.entries.Add(battEntry);
            rows.Add(rowBatt.ToString());
        }

        return rows;
    }

    private static List<string> BuildCustomOverlay(this OSD osd, HardwareMetrics metrics)
    {
        var rows = new List<string>();

        for (int i = 0; i < OverlayCount; i++)
        {
            var name = OverlayOrder[i];
            var content = EntryContent(name, metrics);
            if (!string.IsNullOrEmpty(content))
                rows.Add(content);
        }

        return rows;
    }

    private static string EntryContent(string name, HardwareMetrics metrics)
    {
        using OverlayRow row = new();
        using OverlayEntry entry = new(name, EntryColor(name), true);

        switch (name.ToUpper())
        {
            case "TIME":
                if (OverlayTimeLevel != OverlayEntryLevel.Disabled)
                    entry.elements.Add(new OverlayEntryElement("<TIME=%X>", ""));
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
                        AddElementIfNotNull(entry, metrics.CpuClock / 1000, "GHz", "{0:0.0}");
                        AddElementIfNotNull(entry, metrics.CpuLoad, "%");
                        AddElementIfNotNull(entry, metrics.CpuTemp, "°C");
                        AddElementIfNotNull(entry, metrics.CpuPower, "W");
                        break;
                    case OverlayEntryLevel.Minimal:
                        AddElementIfNotNull(entry, metrics.CpuLoad, "%");
                        AddElementIfNotNull(entry, metrics.CpuPower, "W");
                        break;
                }
                break;

            case "RAM":
                if (OverlayRAMLevel != OverlayEntryLevel.Disabled)
                    AddElementIfNotNull(entry, metrics.MemUsed / 1024, "GB", "{0:00.0}");
                break;

            case "GPU":
                switch (OverlayGPULevel)
                {
                    case OverlayEntryLevel.Full:
                        AddElementIfNotNull(entry, metrics.GpuClock / 1000, "GHz", "{0:0.0}");
                        AddElementIfNotNull(entry, metrics.GpuLoad, "%");
                        AddElementIfNotNull(entry, metrics.GpuTemp, "°C");
                        AddElementIfNotNull(entry, metrics.GpuPower, "W");
                        break;
                    case OverlayEntryLevel.Minimal:
                        AddElementIfNotNull(entry, metrics.GpuLoad, "%");
                        AddElementIfNotNull(entry, metrics.GpuPower, "W");
                        break;
                }
                break;

            case "VRAM":
                if (OverlayVRAMLevel != OverlayEntryLevel.Disabled)
                    AddElementIfNotNull(entry, NormalizeBytes((currentMetrics.GpuMemDedicated + currentMetrics.GpuMemShared) * MB_TO_BYTES), "{0:00.0}");
                break;

            case "BATT":
                switch (OverlayBATTLevel)
                {
                    case OverlayEntryLevel.Full:
                        AddElementIfNotNull(entry, metrics.BattCapacity, "%");
                        AddElementIfNotNull(entry, metrics.BattPower, "W");
                        break;
                    case OverlayEntryLevel.Minimal:
                        AddElementIfNotNull(entry, metrics.BattCapacity, "%");
                        break;
                }
                break;
        }

        // Skip empty rows
        if (entry.elements.Count == 0)
            return string.Empty;

        row.entries.Add(entry);
        return row.ToString();
    }

    private static string EntryColor(string name)
    {
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

    private static void AddElementIfNotNull(OverlayEntry entry, float value, string unit, string colorScheme = "")
    {
        if (!float.IsNaN(value))
            entry.elements.Add(new OverlayEntryElement(value, unit, colorScheme));
    }

    private static void AddElementIfNotNull(OverlayEntry entry, float value, float available, string unit, string colorScheme = "")
    {
        if (!float.IsNaN(value) && !float.IsNaN(available))
            entry.elements.Add(new OverlayEntryElement(
                OverlayEntryElement.FormatValue(value, unit) + "/" + OverlayEntryElement.FormatValue(available, unit).Trim(), unit, colorScheme));
    }


    private static void AddElementIfNotNull(OverlayEntry entry, (float, string) value, (float, string) available, string colorScheme = "")
    {
        if (!float.IsNaN(value.Item1) && !float.IsNaN(available.Item1))
            entry.elements.Add(new OverlayEntryElement(
                $"{OverlayEntryElement.FormatValue(value.Item1, value.Item2)}" +
                $"{(value.Item2 == available.Item2 ? "" : $"<S2>{value.Item2}<S>")}" +
                $"/{OverlayEntryElement.FormatValue(available.Item1, available.Item2).Trim()}",
                available.Item2, colorScheme));
    }

    private static void AddElementIfNotNull(OverlayEntry entry, string value, string unit, string colorScheme = "")
    {
        if (value != null && value.Length > 0)
            entry.elements.Add(new OverlayEntryElement(value, unit, colorScheme));
    }

    private static void AddElementIfNotNull(OverlayEntry entry, (float value, string unit) valueAndUnit, string colorScheme = "")
    {
        AddElementIfNotNull(entry, valueAndUnit.value, valueAndUnit.unit, colorScheme: colorScheme);
    }

    public static (float, string) NormalizeSpeed(float bytes)
    {
        if (float.IsNaN(bytes))
            return (float.NaN, string.Empty);

        bytes /= 1024f;
        string unit = "Kb/s";

        if (bytes > 100000)
        {
            bytes /= 1024f * 1024f;
            unit = "Gb/s";
        }
        else if (bytes > 100)
        {
            bytes /= 1024f;
            unit = "Mb/s";
        }

        return (bytes, unit);
    }

    public static (float, string) NormalizeBytes(float bytes)
    {
        if (float.IsNaN(bytes))
            return (float.NaN, string.Empty);

        string[] sizes = ["B", "KB", "MB", "GB"];
        int order = 0;

        while (bytes >= 1024 && order < sizes.Length - 1)
        {
            order++;
            bytes /= 1024;
        }

        return (bytes, sizes[order]);
    }

    public static (float, string) NormalizeClock(float clock)
    {
        if (float.IsNaN(clock))
            return (float.NaN, string.Empty);

        string[] sizes = ["MHz", "GHz"];
        int order = 0;

        while (clock >= 1000 && order < sizes.Length - 1)
        {
            order++;
            clock /= 1000;
        }

        return (clock, sizes[order]);
    }

    private static void SettingsManager_SettingValueChanged(string name, object value, bool temporary)
    {
        switch (name)
        {
            case "OnScreenDisplayRefreshRate":
                {
                    RefreshInterval = Convert.ToInt32(value);

                    if (RefreshTimer != null && RefreshTimer.Enabled)
                    {
                        RefreshTimer.Stop();
                        RefreshTimer.Interval = RefreshInterval;
                        RefreshTimer.Start();
                    }
                }
                break;

            case "OnScreenDisplayLevel":
                {
                    OverlayLevel = EnumUtils<OverlayDisplayLevel>.Parse(Convert.ToInt16(value));

                    // set OSD toggle hotkey state
                    ManagerFactory.settingsManager.Set("OnScreenDisplayToggle", value);

                    if (OverlayLevel != OverlayDisplayLevel.Disabled)
                    {
                        // set lastOSDLevel to be used in OSD toggle hotkey
                        ManagerFactory.settingsManager.Set("LastOnScreenDisplayLevel", value);

                        if (OverlayLevel == OverlayDisplayLevel.External)
                        {
                            // No need to update OSD in External
                            RefreshTimer?.Stop();

                            // Remove previous UI in External
                            foreach (var pair in onScreenDisplays)
                            {
                                try
                                {
                                    pair.Value.Update(string.Empty);
                                }
                                catch (Exception ex)
                                {
                                    LogManager.LogError("Error clearing OSD for process {0}: {1}", pair.Key, ex.Message);
                                }
                            }
                        }
                        else
                        {
                            // Other modes need the refresh timer to update OSD
                            if (RefreshTimer != null && !RefreshTimer.Enabled)
                                RefreshTimer.Start();
                        }
                    }
                    else
                    {
                        RefreshTimer?.Stop();

                        // clear UI on stop
                        foreach (var pair in onScreenDisplays)
                        {
                            try
                            {
                                pair.Value.Update(string.Empty);
                            }
                            catch (Exception ex)
                            {
                                LogManager.LogError("Error clearing OSD for process {0}: {1}", pair.Key, ex.Message);
                            }
                        }
                    }
                }
                break;

            case "OnScreenDisplayOrder":
                OverlayOrder = value?.ToString()?.Split(",") ?? Array.Empty<string>();
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
    public string ColorScheme { get; set; }

    public override string ToString()
    {
        var value = Value;
        var unit = SzUnit;
        if (!string.IsNullOrEmpty(ColorScheme))
        {
            value = $"<C={ColorScheme}>{Value}<C>";
            unit = $"<C={ColorScheme}>{SzUnit}<C>";
        }
        return string.Format("<C0>{0}<S2>{1}<S><C>", value, unit);
    }

    public OverlayEntryElement(float value, string unit, string colorScheme = "")
    {
        var input = FormatValue(value, unit);

        // Replace leading zeros with spaces
        ColorScheme = colorScheme;
        Value = input;
        SzUnit = unit;
    }

    public static string FormatValue(float value, string unit, string defaultFormat = "00.0")
    {
        string format = unit switch
        {
            "GB" => "0.0",
            "W" => "00",
            "%" => "00",
            "°C" => "00",
            "min" => "00",
            "h" => "00",
            "mins" => "000",
            "MB" => "0",
            "MHz" => "000",
            "GHz" => "0.0",
            "rpm" => "0000",
            _ => defaultFormat
        };

        var input = value.ToString(format);
        // Count leading zeros (but stop before decimal point)
        int leadingZeroCount = 0;
        while (leadingZeroCount < input.Length && input[leadingZeroCount] == '0')
        {
            if (leadingZeroCount + 1 > input.Length - 1 ||
                leadingZeroCount + 1 < input.Length && input[leadingZeroCount + 1] == '.')
                break;

            leadingZeroCount++;

        }
        return input[leadingZeroCount..].PadLeft(input.Length, ' ');
    }

    public OverlayEntryElement(string value, string unit, string colorScheme = "")
    {
        Value = value;
        SzUnit = unit;
        ColorScheme = colorScheme;
    }
}

public class OverlayEntry : IDisposable
{
    public List<OverlayEntryElement> elements = [];
    public string Name { get; set; }

    public OverlayEntry(string name, string colorScheme = "", bool indent = false)
    {
        Name = BuildName(name, indent);

        if (!string.IsNullOrEmpty(colorScheme))
            Name = $"<C={colorScheme}>{Name}<C>";
    }

    private static string BuildName(string name, bool indent)
    {
        if (string.IsNullOrEmpty(name))
            return string.Empty;

        var formatted = (name + ":").PadRight(3);
        return indent ? formatted : name;
    }

    ~OverlayEntry()
    {
        Dispose();
    }

    public void Dispose()
    {
        elements?.Clear();
        elements = null;
        GC.SuppressFinalize(this);
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
        if (entries != null)
        {
            foreach (var entry in entries)
                entry?.Dispose();

            entries.Clear();
            entries = null;
        }
        GC.SuppressFinalize(this);
    }

    public override string ToString()
    {
        if (entries == null || entries.Count == 0)
            return string.Empty;

        var rowParts = new List<string>(entries.Count);

        foreach (var entry in entries)
        {
            if (entry.elements is null || entry.elements.Count == 0)
                continue;

            var elementStrings = new string[entry.elements.Count];
            for (int i = 0; i < entry.elements.Count; i++)
                elementStrings[i] = entry.elements[i].ToString();

            var itemStr = entry.Name + " " + string.Join(" ", elementStrings);
            rowParts.Add(itemStr);
        }

        return string.Join("<C1> | <C>", rowParts);
    }
}