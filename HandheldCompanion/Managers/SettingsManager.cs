using HandheldCompanion.Devices;
using HandheldCompanion.Processors;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Timers;

namespace HandheldCompanion.Managers;

public static class Settings
{
    public static readonly string ConfigurableTDPOverrideDown = "ConfigurableTDPOverrideDown";
    public static readonly string ConfigurableTDPOverrideUp = "ConfigurableTDPOverrideUp";
    public static readonly string OnScreenDisplayLevel = "OnScreenDisplayLevel";
    public static readonly string OnScreenDisplayTimeLevel = "OnScreenDisplayTimeLevel";
    public static readonly string OnScreenDisplayFPSLevel = "OnScreenDisplayFPSLevel";
    public static readonly string OnScreenDisplayCPULevel = "OnScreenDisplayCPULevel";
    public static readonly string OnScreenDisplayGPULevel = "OnScreenDisplayGPULevel";
    public static readonly string OnScreenDisplayRAMLevel = "OnScreenDisplayRAMLevel";
    public static readonly string OnScreenDisplayVRAMLevel = "OnScreenDisplayVRAMLevel";
    public static readonly string OnScreenDisplayBATTLevel = "OnScreenDisplayBATTLevel";
}

public static class SettingsManager
{
    static string configFileName = "user.json";
    static string configFilePath;
    static string configSettingPath;

    public delegate void InitializedEventHandler();

    public delegate void SettingValueChangedEventHandler(string name, object value);

    private static readonly Dictionary<string, object> Settings = new();

    private static ConcurrentDictionary<string, object> config = new();
    private static ConcurrentDictionary<string, object> setting = new();
    private static Timer timer = new(1000)
    {
        Enabled = false,
        AutoReset = false
    };

    static SettingsManager()
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

    public static bool IsInitialized { get; internal set; }

    public static event SettingValueChangedEventHandler SettingValueChanged;

    public static event InitializedEventHandler Initialized;

    public static void Start()
    {
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
            SettingValueChanged?.Invoke(property.Key, GetInternal(property.Key));
        }

        if (Get<bool>("FirstStart"))
            Set("FirstStart", false);

        timer.Elapsed += Timer_Elapsed;
        timer.Start();

        IsInitialized = true;
        Initialized?.Invoke();

        LogManager.LogInformation("{0} has started", "SettingsManager");
    }

    private static void Timer_Elapsed(object? sender, ElapsedEventArgs e)
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

    public static void Stop()
    {
        if (!IsInitialized)
            return;

        IsInitialized = false;

        LogManager.LogInformation("{0} has stopped", "SettingsManager");
    }

    public static bool Is(string name)
    {
        return config.ContainsKey(name);
    }

    public static void Set(string name, object value, bool save = true)
    {
        try
        {
            var valueBefore = GetInternal(name);
            if (value.Equals(valueBefore) || (valueBefore != null && value.ToString() == valueBefore.ToString()))
                return;

            setting[name] = value;
            if (save)
            {
                config[name] = value;
                timer.Start();
            }
            // raise event
            SettingValueChanged?.Invoke(name, value);
        }
        catch (Exception ex)
        {
            LogManager.LogError($"Error setting config value {name}: {value} {ex.Message} {ex.StackTrace}");
        }
    }


    public static T Get<T>(string name, T defaultValue = default)
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

    private static object GetInternal(string name)
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
            case "HasBrightnessSupport":
                return MultimediaManager.HasBrightnessSupport();

            case "HasVolumeSupport":
                return MultimediaManager.HasVolumeSupport();
            default:
                {
                    if (setting.TryGetValue(name, out var returnValue))
                        return returnValue;

                    if (Is(name))
                        return config[name];

                    return default;
                }
        }
    }

}