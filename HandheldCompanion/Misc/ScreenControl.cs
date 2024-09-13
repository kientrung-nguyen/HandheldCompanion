using HandheldCompanion.Managers;
using HandheldCompanion.Managers.Desktop;
using HandheldCompanion.Views.Windows;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using WindowsDisplayAPI;
using WindowsDisplayAPI.DisplayConfig;

namespace HandheldCompanion.Misc;

public static class ScreenControl
{
    private static Dictionary<string, Action<Display>> eventHandlers;
    private static IEnumerable<Display> allDisplays = [];
    private static Display primaryDisplay;
    private static Display internalDisplay;


    private static readonly Dictionary<int, IEnumerable<int?>> cachedFrameLimits = new();

    public static Display PrimaryDisplay => primaryDisplay;
    public static IEnumerable<Display> AllDisplays => allDisplays;

    public static bool IsExternalDisplayConnected()
    {
        try
        {
            if (internalDisplay == null)
                return false;

            var pathInfos = PathInfo.GetActivePaths();
            foreach (var pathInfo in pathInfos)
                foreach (var targetInfo in pathInfo.TargetsInfo)
                    if (targetInfo.OutputTechnology != WindowsDisplayAPI.Native.DisplayConfig.DisplayConfigVideoOutputTechnology.Internal &&
                        targetInfo.OutputTechnology != WindowsDisplayAPI.Native.DisplayConfig.DisplayConfigVideoOutputTechnology.DisplayPortEmbedded &&
                        targetInfo.DisplayTarget.FriendlyName != internalDisplay.ToPathDisplayTarget().FriendlyName)
                    {
                        return true;
                    }

        }
        catch (Exception ex)
        {
            LogManager.LogError($"{nameof(IsExternalDisplayConnected)} {ex}");
        }

        return false;
    }



    // A function that takes an int as a parameter and returns the closest multiple of 10
    private static int RoundToEven(int num)
    {
        if (num % 2 == 0)
            return num;

        return num + 1;
    }

    // A function that takes a screen frequency int value and returns a list of integer values that are the quotient of the frequency and the closest divisor
    public static IEnumerable<int?> GetFramelimits(Display display)
    {
        // A list to store the quotients
        List<int?> limits = [0]; // (Comparer<int>.Create((x, y) => y.CompareTo(x)));

        // A variable to store the divider value, rounded to nearest even number
        int divider = 1;
        int dmDisplayFrequency = RoundToEven(display.DisplayScreen.CurrentSetting.Frequency);
        int maxDisplayFrequency = display.DisplayScreen.GetPossibleSettings()
            .Where(setting =>
                setting.Resolution.Width == display.DisplayScreen.CurrentSetting.Resolution.Width &&
                setting.Resolution.Height == display.DisplayScreen.CurrentSetting.Resolution.Height &&
                setting.ColorDepth == display.DisplayScreen.CurrentSetting.ColorDepth)
            .DistinctBy(setting => setting.Frequency)
            .OrderByDescending(setting => setting.Frequency).First().Frequency;

        if (cachedFrameLimits.TryGetValue(dmDisplayFrequency, out IEnumerable<int?>? value)) return value;

        int lowestFPS = dmDisplayFrequency;

        HashSet<int> fpsLimits = new();

        // A loop to find the lowest possible fps limit option and limits from division
        do
        {
            // If the frequency is divisible by the divider, add the quotient to the list
            if (maxDisplayFrequency % divider == 0)
            {
                int frequency = maxDisplayFrequency / divider;
                if (frequency < 20)
                {
                    break;
                }
                if (frequency <= dmDisplayFrequency)
                    fpsLimits.Add(frequency);
                lowestFPS = frequency;
            }

            // Increase the divider by 1
            divider++;
        } while (true);

        // loop to fill all possible fps limit options from lowest fps limit (e.g. getting 40FPS or 60Hz)
        int nrOptions = maxDisplayFrequency / lowestFPS;
        for (int i = 1; i < nrOptions; i++)
        {
            if (lowestFPS * i <= dmDisplayFrequency)
                fpsLimits.Add(lowestFPS * i);
        }

        // Fill limits

        var orderedFpsLimits = fpsLimits.OrderByDescending(f => f);
        for (int i = 0; i < orderedFpsLimits.Count(); i++)
            limits.Add(orderedFpsLimits.ElementAt(i));

        cachedFrameLimits.TryAdd(dmDisplayFrequency, limits);

        // Return the list of quotients
        return limits;
    }

    public static void Auto()
    {
        if (!SettingsManager.IsInitialized) return;
        if (!SettingsManager.Get<bool>("ScreenFrequencyAuto")) return;
        if (internalDisplay is null || internalDisplay.DisplayScreen is null || !internalDisplay.DisplayScreen.IsPrimary) return;

        if (SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Online)
        {
            if (internalDisplay.DisplayScreen.GetPossibleSettings()
                .Where(setting =>
                    setting.Resolution.Width == internalDisplay.DisplayScreen.CurrentSetting.Resolution.Width &&
                    setting.Resolution.Height == internalDisplay.DisplayScreen.CurrentSetting.Resolution.Height &&
                    setting.ColorDepth == internalDisplay.DisplayScreen.CurrentSetting.ColorDepth &&
                    setting.Frequency > internalDisplay.DisplayScreen.CurrentSetting.Frequency)
                .DistinctBy(setting => setting.Frequency)
                .OrderByDescending(setting => setting.Frequency).FirstOrDefault() is DisplayPossibleSetting higherFrequency)
            {
                if (Set(internalDisplay, higherFrequency))
                    ScreenBrightness.Set(100);
            }
        }
        else
        {
            if (internalDisplay.DisplayScreen.GetPossibleSettings()
                .Where(setting =>
                    setting.Resolution.Width == internalDisplay.DisplayScreen.CurrentSetting.Resolution.Width &&
                    setting.Resolution.Height == internalDisplay.DisplayScreen.CurrentSetting.Resolution.Height &&
                    setting.ColorDepth == internalDisplay.DisplayScreen.CurrentSetting.ColorDepth &&
                    setting.Frequency < internalDisplay.DisplayScreen.CurrentSetting.Frequency)
                .DistinctBy(setting => setting.Frequency)
                .OrderByDescending(setting => setting.Frequency).FirstOrDefault() is DisplayPossibleSetting lowerFrequency)
            {
                if (Set(internalDisplay, lowerFrequency))
                    ScreenBrightness.Set(85);
            }
        }
    }

    public static bool Set(Display display, DisplayPossibleSetting setting)
    {
        if (display is null) return false;

        var currSetting = display.DisplayScreen.CurrentSetting;

        if (currSetting.Resolution.Width == setting.Resolution.Width &&
            currSetting.Resolution.Height == setting.Resolution.Height &&
            currSetting.ColorDepth == setting.ColorDepth &&
            currSetting.Frequency == setting.Frequency)
            return false;
        try
        {
            display.DisplayScreen.SetSettings(new DisplaySetting(setting), true);
            Thread.Sleep(2000);
            ToastManager.RunToast(
                $"{setting.Resolution.Width} x {setting.Resolution.Height} @ {setting.Frequency}Hz", ToastIcons.Laptop);
            return true;
        }
        catch (Exception ex)
        {
            LogManager.LogError($"{nameof(ScreenControl)} Set failed {ex}");
            return false;
        }
    }

    public static void SubscribeToEvents(Dictionary<string, Action<Display>> EventHandlers)
    {
        eventHandlers = EventHandlers;
        SystemEvents.DisplaySettingsChanged += SystemEvents_DisplaySettingsChanged;
        HandleEvents();
    }

    private static void HandleEvents()
    {
        var _allDisplays = Display.GetDisplays();
        var _primaryDisplay = _allDisplays.FirstOrDefault(v => v.DisplayScreen.IsPrimary);
        var _internalDisplay = _allDisplays.FirstOrDefault(v => PathInfo.GetActivePaths().Any(pathInfo =>
                                                                                                pathInfo.DisplaySource.DisplayName.Equals(v.ScreenName) &&
                                                                                                pathInfo.TargetsInfo.Any(targetInfo =>
                                                                                                    (targetInfo.OutputTechnology == WindowsDisplayAPI.Native.DisplayConfig.DisplayConfigVideoOutputTechnology.Internal ||
                                                                                                    targetInfo.OutputTechnology == WindowsDisplayAPI.Native.DisplayConfig.DisplayConfigVideoOutputTechnology.DisplayPortEmbedded) &&
                                                                                                    targetInfo.DisplayTarget.FriendlyName.Equals(v.ToPathDisplayTarget().FriendlyName))));
        if (_primaryDisplay is null)
        {
            LogManager.LogError("Failed to detect primary display.");
            return;
        }

        if (_internalDisplay is null)
        {
            LogManager.LogError("Failed to detect internal display.");
            return;
        }

        internalDisplay = _internalDisplay;

        if (primaryDisplay is null || !primaryDisplay.ToPathDisplayTarget().FriendlyName.Equals(_primaryDisplay.ToPathDisplayTarget().FriendlyName))
        {
            primaryDisplay = _primaryDisplay;
            if (eventHandlers.TryGetValue("PrimaryScreenChanged", out var PrimaryScreenChanged))
                PrimaryScreenChanged(_primaryDisplay);
        }

        foreach (var _display in _allDisplays.Where(_v => !allDisplays.Any(v => v.DevicePath == _v.DevicePath)))
            if (eventHandlers.TryGetValue("ScreenConnected", out var ScreenConnected))
                ScreenConnected(_display);

        foreach (var _display in allDisplays.Where(v => !_allDisplays.Any(_v => _v.DevicePath == v.DevicePath)))
            if (eventHandlers.TryGetValue("ScreenDisconnected", out var ScreenDisconnected))
                ScreenDisconnected(_display);

        allDisplays = [];
        allDisplays = _allDisplays;

        if (primaryDisplay is not null)
            if (eventHandlers.TryGetValue("DisplaySettingsChanged", out var DisplaySettingsChanged))
                DisplaySettingsChanged(primaryDisplay);
    }

    public static void Unsubscribe()
    {
        SystemEvents.DisplaySettingsChanged -= SystemEvents_DisplaySettingsChanged;
    }

    private static void SystemEvents_DisplaySettingsChanged(object? sender, EventArgs e)
    {
        HandleEvents();
    }
}
