using HandheldCompanion.Devices;
using HandheldCompanion.Processors;
using HandheldCompanion.Shared;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Timers;

namespace HandheldCompanion.Managers;

public static class Settings
{
    public static readonly string ConfigurableTDPOverrideDown = "ConfigurableTDPOverrideDown";
    public static readonly string ConfigurableTDPOverrideUp = "ConfigurableTDPOverrideUp";

    public static readonly string OnScreenDisplayRefreshRate = "OnScreenDisplayRefreshRate";
    public static readonly string OnScreenDisplayLevel = "OnScreenDisplayLevel";
    public static readonly string OnScreenDisplayTimeLevel = "OnScreenDisplayTimeLevel";
    public static readonly string OnScreenDisplayFPSLevel = "OnScreenDisplayFPSLevel";
    public static readonly string OnScreenDisplayCPULevel = "OnScreenDisplayCPULevel";
    public static readonly string OnScreenDisplayGPULevel = "OnScreenDisplayGPULevel";
    public static readonly string OnScreenDisplayRAMLevel = "OnScreenDisplayRAMLevel";
    public static readonly string OnScreenDisplayVRAMLevel = "OnScreenDisplayVRAMLevel";
    public static readonly string OnScreenDisplayBATTLevel = "OnScreenDisplayBATTLevel";

    /// <summary>
    /// First version that implemented the new Hotkey manager
    /// </summary>
    public static readonly string VersionHotkeyManager = "0.21.5.0";

    /// <summary>
    /// First version that implemented Library manager
    /// </summary>
    public static readonly string VersionLibraryManager = "0.24.0.0";
}

public enum LayoutModes
{
    Gamepad = 0,
    Desktop = 1,
    Auto = 2
}

public class SettingsManager : IManager
{
    static string configFileName = "user.json";
    static string configFilePath;
    static string configSettingPath;

    public delegate void InitializedEventHandler();

    public delegate void SettingValueChangedEventHandler(string name, object value, bool temporary);
    public event SettingValueChangedEventHandler SettingValueChanged;

    private readonly Dictionary<string, object> Settings = [];

    private ConcurrentDictionary<string, object> config = new();
    private ConcurrentDictionary<string, object> current = new();
    private Timer timer = new(1000)
    {
        Enabled = false,
        AutoReset = false
    };

    public SettingsManager()
    {

        configSettingPath = Path.Combine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "HandheldCompanion"), "config");
        configFilePath = Path.Combine(configSettingPath, configFileName);
        if (!Directory.Exists(configSettingPath))
            Directory.CreateDirectory(configSettingPath);

        if (!File.Exists(configFilePath))
            File.WriteAllText(configFilePath, "");

        var jsonString = File.ReadAllText(configFilePath);
        if (jsonString == null || jsonString.Length == 0 || !jsonString.Contains('}'))
            config = new ConcurrentDictionary<string, object>
            {
                ["FirstStart"] = true
            };
        else
            config = JsonConvert.DeserializeObject<ConcurrentDictionary<string, object>>(jsonString) ?? new ConcurrentDictionary<string, object>
            {
                ["FirstStart"] = true
            };
    }

    public override void Start()
    {
        if (Status.HasFlag(ManagerStatus.Initializing) || Status.HasFlag(ManagerStatus.Initialized))
            return;

        base.PrepareStart();

        foreach (var property in Properties.Settings
            .Default
            .Properties
            .Cast<SettingsProperty>()
            .OrderBy(s => s.Name))
        {
            if (!config.ContainsKey(property.Name))
                config[property.Name] = Properties.Settings.Default[property.Name];
        }

        foreach (var property in config.ToImmutableSortedDictionary())
        {
            SettingValueChanged?.Invoke(property.Key, GetInternal(property.Key), false);
        }

        if (Get<bool>("FirstStart"))
            Set("FirstStart", false);

        timer.Elapsed += Timer_Elapsed;
        timer.Start();

        LogManager.LogInformation("{0} has started", "SettingsManager");

        base.Start();
    }

    public override void Stop()
    {
        if (Status.HasFlag(ManagerStatus.Halting) || Status.HasFlag(ManagerStatus.Halted))
            return;

        base.PrepareStop();
        base.Stop();
    }

    private void Timer_Elapsed(object? sender, ElapsedEventArgs e)
    {
        try
        {
            timer.Stop();
            File.WriteAllText(configFilePath,
                JsonConvert.SerializeObject(config.ToImmutableSortedDictionary(),
                Formatting.Indented));

        }
        catch (Exception ex)
        {
            LogManager.LogError($"{nameof(SettingsManager)} config write error {ex.Message} {ex.StackTrace}");
        }
    }

    private bool Is(string name)
    {
        return config.ContainsKey(name);
    }

    private bool HasProperty(string name)
    {
        return Properties.Settings
            .Default
            .Properties
            .Cast<SettingsProperty>().Any(v => v.Name == name);
    }

    public void Set(string name, object value, bool save = true)
    {
        try
        {
            var valueBefore = GetInternal(name);
            if (value.Equals(valueBefore) || (valueBefore != null && value.ToString() == valueBefore.ToString()))
                return;

            current[name] = value;
            if (save)
            {
                config[name] = value;
                timer.Start();
            }

            if (Status.HasFlag(ManagerStatus.Initialized))
            {
                LogManager.LogInformation($"SettingValueChanged {name} {value}");
                // raise event
                SettingValueChanged?.Invoke(name, value, !save);
            }
        }
        catch (Exception) { }
    }


    public T Get<T>(string name, T defaultValue = default)
    {
        try
        {
            var returnValue = GetInternal(name);
            if (returnValue is not null)
            {
                if (returnValue is T)
                    return (T)returnValue;

                if (typeof(IConvertible).IsAssignableFrom(typeof(T)))
                    return (T)Convert.ChangeType(returnValue, typeof(T));
            }
            return defaultValue;

        }
        catch (Exception ex)
        {
            LogManager.LogError($"Error getting config value {name} {ex.Message} {ex.StackTrace}");
            return defaultValue;
        }
    }

    private object GetInternal(string name)
    {
        // used to handle cases
        switch (name)
        {
            case "ConfigurableTDPOverrideDown":
                {
                    var TDPoverride = Get<bool>("ConfigurableTDPOverride");
                    return TDPoverride
                        ? config.TryGetValue("ConfigurableTDPOverrideDown", out var TDPvalue)
                            ? TDPvalue
                            : IDevice.GetCurrent().cTDP[0]
                        : IDevice.GetCurrent().cTDP[0];
                }

            case "ConfigurableTDPOverrideUp":
                {
                    var TDPoverride = Get<bool>("ConfigurableTDPOverride");
                    return TDPoverride
                        ? config.TryGetValue("ConfigurableTDPOverrideUp", out var TDPvalue)
                            ? TDPvalue
                            : IDevice.GetCurrent().cTDP[1]
                        : IDevice.GetCurrent().cTDP[1];
                }

            case "QuickToolsPerformanceTDPValue":
                {
                    var TDPoverride = Get<bool>("QuickToolsPerformanceTDPEnabled");
                    return TDPoverride
                        ? config.TryGetValue("QuickToolsPerformanceTDPValue", out var TDPvalue)
                            ? TDPvalue
                            : IDevice.GetCurrent().nTDP[(int)PowerType.Slow]
                        : IDevice.GetCurrent().nTDP[(int)PowerType.Slow];
                }

            case "QuickToolsPerformanceTDPBoostValue":
                {
                    var TDPoverride = Get<bool>("QuickToolsPerformanceTDPEnabled");
                    return TDPoverride
                        ? config.TryGetValue("QuickToolsPerformanceTDPBoostValue", out var TDPvalue)
                            ? TDPvalue
                            : IDevice.GetCurrent().nTDP[(int)PowerType.Slow]
                        : IDevice.GetCurrent().nTDP[(int)PowerType.Fast];
                }

            case "QuickToolsPerformanceGPUValue":
                {
                    var GPUoverride = Get<bool>("QuickToolsPerformanceGPUEnabled");

                    var GPUvalue = Convert.ToDouble(Properties.Settings.Default["QuickToolsPerformanceGPUValue"]);
                    return GPUvalue;
                }

            case "HasBrightnessSupport":
                return ManagerFactory.multimediaManager.HasBrightnessSupport();

            case "HasVolumeSupport":
                return ManagerFactory.multimediaManager.HasVolumeSupport();
            default:
                {
                    if (current.TryGetValue(name, out var returnValue))
                        return returnValue;

                    if (Is(name))
                        return config[name];

                    if (HasProperty(name))
                        return Properties.Settings.Default[name];

                    return default;
                }
        }
    }

}