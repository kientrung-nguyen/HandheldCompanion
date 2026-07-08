using HandheldCompanion.Shared;
using HandheldCompanion.Utils;
using Microsoft.Extensions.Logging.Abstractions;
using RTSSSharedMemoryNET;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Timers;
using System.Windows.Navigation;
using Windows.Media.AppBroadcasting;

namespace HandheldCompanion.Managers;

public static class OSDManager
{
    public delegate void InitializedEventHandler();
    public static event InitializedEventHandler? Initialized;

    private const string COLOR_STEAM_BLUE = "5858F2";
    private const string COLOR_GRAY = "d7d7d7";
    // C1: GPU
    // C2: CPU
    // C3: RAM
    // C4: VRAM
    // C5: BATT
    // C6: FPS
    private const string Header = $"<C0={COLOR_GRAY}><C1={COLOR_STEAM_BLUE}><A0=-4><S0=-80><S1=-80>";
    private const string Row = $"<P1><L0><C=80000000><B=0,0>\b<C>{{0}}<C><S>";
    private const string Column = $"<P0><L0><C=80000000><B=0,0>\b<C>{{0}}<C><S>";

    private static bool IsInitialized;
    public static string[] OverlayOrder = [];
    public static int OverlayCount;
    public static short OverlayLevel;
    public static short OverlayDirection;
    public static short OverlayTimeLevel;
    public static short OverlayFPSLevel;
    public static short OverlayCPULevel;
    public static short OverlayRAMLevel;
    public static short OverlayGPULevel;
    public static short OverlayVRAMLevel;
    public static short OverlayBATTLevel;

    private static readonly Timer RefreshTimer;
    private static int RefreshInterval = 100;

    private static readonly ConcurrentDictionary<int, OSD> OnScreenDisplays = new();
    public static OSD? OnScreenDisplay => OnScreenAppEntry is not null && OnScreenDisplays.TryGetValue(OnScreenAppEntry.ProcessId, out OSD? osd) ? osd : null;
    public static AppEntry? OnScreenAppEntry;
    private static List<string> Content = new();
    private static readonly OverlayManager _overlayManager = new();

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

        AppEntry? appEntry = PlatformManager.RTSS.GetAppEntry();
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
        SettingsManager_SettingValueChanged("OnScreenDisplayRefreshRate", ManagerFactory.settingsManager.GetString("OnScreenDisplayRefreshRate"), false);
        SettingsManager_SettingValueChanged("OnScreenDisplayLevel", ManagerFactory.settingsManager.GetString("OnScreenDisplayLevel"), false);
        SettingsManager_SettingValueChanged("OnScreenDisplayDirection", ManagerFactory.settingsManager.GetString("OnScreenDisplayDirection"), false);
        SettingsManager_SettingValueChanged("OnScreenDisplayOrder", ManagerFactory.settingsManager.GetString("OnScreenDisplayOrder"), false);
        SettingsManager_SettingValueChanged("OnScreenDisplayTimeLevel", ManagerFactory.settingsManager.GetString("OnScreenDisplayTimeLevel"), false);
        SettingsManager_SettingValueChanged("OnScreenDisplayFPSLevel", ManagerFactory.settingsManager.GetString("OnScreenDisplayFPSLevel"), false);
        SettingsManager_SettingValueChanged("OnScreenDisplayCPULevel", ManagerFactory.settingsManager.GetString("OnScreenDisplayCPULevel"), false);
        SettingsManager_SettingValueChanged("OnScreenDisplayRAMLevel", ManagerFactory.settingsManager.GetString("OnScreenDisplayRAMLevel"), false);
        SettingsManager_SettingValueChanged("OnScreenDisplayGPULevel", ManagerFactory.settingsManager.GetString("OnScreenDisplayGPULevel"), false);
        SettingsManager_SettingValueChanged("OnScreenDisplayVRAMLevel", ManagerFactory.settingsManager.GetString("OnScreenDisplayVRAMLevel"), false);
        SettingsManager_SettingValueChanged("OnScreenDisplayBATTLevel", ManagerFactory.settingsManager.GetString("OnScreenDisplayBATTLevel"), false);
    }

    public static void Stop()
    {
        if (!IsInitialized)
            return;

        RefreshTimer.Stop();

        // unhook all processes
        foreach (var processId in OnScreenDisplays.Keys)
            RTSS_Unhooked(processId);

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
            if (OnScreenDisplays.TryGetValue(processId, out OSD? OSD))
            {
                if (OSD is not null)
                {
                    OSD.Update(string.Empty);
                    OSD.Dispose();
                }

                OnScreenDisplays.Remove(processId, out _);
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
            OnScreenAppEntry = appEntry;

            // only create a new OSD if needed
            if (OnScreenDisplays.ContainsKey(appEntry.ProcessId))
                return;

            OnScreenDisplays[OnScreenAppEntry.ProcessId] = new OSD(OnScreenAppEntry.Name);
        }
        catch { }
    }

    private static void UpdateOSD(object? sender, ElapsedEventArgs e)
    {
        if (OverlayLevel == 0)
            return;

        foreach (var pair in OnScreenDisplays)
        {
            int processId = pair.Key;
            OSD processOSD = pair.Value;

            try
            {
                if (OnScreenAppEntry is not null && processId == OnScreenAppEntry.ProcessId)
                {
                    string content = Draw(processId);
                    processOSD.Update(content);
                }
                else
                {
                    processOSD.Update(string.Empty);
                }
            }
            catch { }
        }
    }

    private static string Draw(int processId)
    {
        Content.Clear();
        try
        {
            var config = _overlayManager.GetConfig(OverlayLevel, OverlayDirection);
            if (config is null)
            {
                goto Exit;
            }

            Content.Add(Header + string.Format(OverlayDirection == 0 ? Row : Column, config));
        }
        catch (NotImplementedException)
        {
        }

    Exit:
        return string.Join("\n", Content);
    }

    public static void AddElementIfNotNull(OverlayEntry entry, float? value, string unit)
    {
        //if (value is not null)
        //    entry.elements.Add(new OverlayEntryElement((float)value, unit));
        if (unit == "MHz" && value > 1000f)
        { 
            value /= 1000f;
            unit = "GHz";
        }

        if (unit == "MB" && value > 1000f)
        {
            value /= 1024f;
            unit = "GB";
        }
        entry.elements.Add(new OverlayEntryElement(value, unit));
    }

    public static void AddElementIfNotNull(OverlayEntry entry, float? value, float? available, string unit)
    {
        if (value is not null && available is not null)
            entry.elements.Add(new OverlayEntryElement((float)value, (float)available, unit));
    }

    private static void SettingsManager_SettingValueChanged(string name, object? value, bool temporary)
    {
        switch (name)
        {
            case "OnScreenDisplayRefreshRate":
                {
                    RefreshInterval = Convert.ToInt32(value);

                    if (RefreshTimer.Enabled)
                    {
                        RefreshTimer.Stop();
                        RefreshTimer.Interval = RefreshInterval;
                        RefreshTimer.Start();
                    }
                }
                break;
            case "OnScreenDisplayDirection":
                {
                    try
                    {
                        OverlayDirection = Convert.ToInt16(value);
                        if (OverlayLevel > 0)
                        {
                            if (OverlayLevel == 5)
                            {
                                // No need to update OSD in External
                                RefreshTimer.Stop();

                                // Remove previous UI in External
                                foreach (var pair in OnScreenDisplays)
                                {
                                    var processOSD = pair.Value;
                                    processOSD.Update("");
                                }
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
                            foreach (var pair in OnScreenDisplays)
                            {
                                var processOSD = pair.Value;
                                processOSD.Update("");
                            }
                        }
                    }
                    catch
                    {
                        ManagerFactory.settingsManager.SetProperty("OnScreenDisplayDirection", 0);
                        OverlayDirection = 0;
                    }
                    break;
                }
            case "OnScreenDisplayLevel":
                {
                    OverlayLevel = Convert.ToInt16(value);

                    // set OSD toggle hotkey state
                    ManagerFactory.settingsManager.SetProperty("OnScreenDisplayToggle", OverlayLevel != 0);

                    if (OverlayLevel > 0)
                    {
                        // set lastOSDLevel to be used in OSD toggle hotkey
                        ManagerFactory.settingsManager.SetProperty("LastOnScreenDisplayLevel", value);

                        if (OverlayLevel == 5)
                        {
                            // No need to update OSD in External
                            RefreshTimer.Stop();

                            // Remove previous UI in External
                            foreach (var pair in OnScreenDisplays)
                            {
                                var processOSD = pair.Value;
                                processOSD.Update("");
                            }
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
                        foreach (var pair in OnScreenDisplays)
                        {
                            var processOSD = pair.Value;
                            processOSD.Update("");
                        }
                    }
                }
                break;

            case "OnScreenDisplayOrder":
                OverlayOrder = Convert.ToString(value)?.Split(",") ?? new string[0];
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

    public override string ToString()
    {
        if (Value == "--")
            return string.Format("<C0>{0:00}<C>", Value);
        return string.Format("<C0>{0:00}<S1>{1}<S><C>", Value, SzUnit);
    }

    public OverlayEntryElement(float? value, string unit)
    {
        Value = FormatValue(value, unit, true);
        SzUnit = unit;
    }

    public OverlayEntryElement(float value, float available, string unit)
    {
        Value = FormatValue(value, unit, true) + "/" + FormatValue(available, unit, false);
        SzUnit = unit;
    }

    public OverlayEntryElement(string value, string unit = "")
    {
        Value = value;
        SzUnit = unit;
    }

    public static string FormatValue(float? value, string unit, bool? padLeft = null)
    {
        string format = unit switch
        {
            "GB" => "0.0", // One decimal
            "W" => "F1",   // Two digits forced, no decimal
            "%" => "00",   // Two digits forced, no decimal
            "°C" or "°" or "C" => "0.0", // Two digits forced, no decimal
            "h" => "00", // Two digits forced, no decimal
            "min" or "mins" or "m" => "00", // Two digits forced, no decimal
            "MB" => "0",   // No leading zeros, no decimal
            "MHz" => "000",
            "GHz" => "0.0",
            "rpm" => "000",
            _ => "0.##"    // Default format (no leading zeros, up to 2 decimals)
        };

        var input = value?.ToString(format, CultureInfo.InvariantCulture) ?? "--";
        if (padLeft is null || input == "--") return input;
        // Count leading zeros (but stop before decimal point)
        int leadingZeroCount = 0;
        while (leadingZeroCount < input.Length && input[leadingZeroCount] == '0')
        {
            if (leadingZeroCount + 1 > input.Length - 1 ||
                leadingZeroCount + 1 < input.Length && input[leadingZeroCount + 1] == '.')
                break;

            leadingZeroCount++;

        }
        if (padLeft.Value)
            return input[leadingZeroCount..].PadLeft(input.Length, ' ');
        return input[leadingZeroCount..].PadRight(input.Length, ' ');
    }
}

public class OverlayEntry : IDisposable
{
    public List<OverlayEntryElement> elements = [];

    public OverlayEntry(string name, string colorScheme = "", bool indent = false)
    {
        Name = BuildName(name, indent);// indent ? name + "\t" : name;

        if (!string.IsNullOrEmpty(colorScheme) && !string.IsNullOrEmpty(name))
            Name = "<C=" + colorScheme + ">" + Name + "<C>";
        Name = "<S0>" + Name + "<S>";
    }

    private static string BuildName(string name, bool indent)
    {
        if (string.IsNullOrEmpty(name))
            return string.Empty;

        var formatted = name.PadRight(Math.Max(0, 7 - name.Length));
        return indent ? formatted : name;
    }

    ~OverlayEntry()
    {
        Dispose();
    }

    public string Name { get; set; }

    public void Dispose()
    {
        elements?.Clear();
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
        entries.Clear();
        GC.SuppressFinalize(this);
    }

    public override string ToString()
    {
        List<string> rowStr = [];

        foreach (var entry in entries)
        {
            if (entry.elements is null || entry.elements.Count == 0)
                continue;

            List<string> entriesStr = [entry.Name];

            foreach (var element in entry.elements)
                entriesStr.Add(element.ToString());

            var ItemStr = string.Join(" ", entriesStr.Where(v => v != null && v.Length > 0));
            rowStr.Add(ItemStr);
        }

        return string.Join("<C1> | <C>", rowStr);
    }
}