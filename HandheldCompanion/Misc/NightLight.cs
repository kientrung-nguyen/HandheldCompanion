using HandheldCompanion.Managers;
using HandheldCompanion.Utils;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Win32;
using System;
using System.Management;

namespace HandheldCompanion.Misc;

public static class NightLight
{
    private static string key =
        "Software\\Microsoft\\Windows\\CurrentVersion\\CloudStore\\Store\\DefaultAccount\\Current\\default$windows.data.bluelightreduction.bluelightreductionstate\\windows.data.bluelightreduction.bluelightreductionstate";

    private static RegistryKey? registryKey = Registry.CurrentUser.OpenSubKey(key, true);

    private static readonly RegistryWatcher watcher = new(WatchedRegistry.CurrentUser,
        @"SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\CloudStore\\" +
        @"Store\\DefaultAccount\\Current\\default$windows.data.bluelightreduction.bluelightreductionstate\\" +
        @"windows.data.bluelightreduction.bluelightreductionstate", "Data");

    public static int Get()
    {
        if (!Supported) return -1;

        byte[] data = new byte[43];
        if (registryKey is not null &&
            registryKey.GetValue("Data") is byte[] registry)
        {
            if (registry.Length > data.Length)
                data = new byte[registry.Length];

            Array.Copy(registry, 0, data, 0, registry.Length); // copy the second array into the first array starting at index 5
            return data[18] == 0x15 ? 1 : 0;
        }
        return 0;
    }

    public static bool? Set(bool value)
    {
        if (!Supported) return null;

        var wasEnabled = Get() == 1;
        if (wasEnabled == value) return null;

        byte[] data = new byte[43];
        byte[] newData = new byte[43];
        if (registryKey is not null &&
            registryKey.GetValue("Data") is byte[] registry)
        {
            if (registry.Length > data.Length)
                data = new byte[registry.Length];

            Array.Copy(registry, 0, data, 0, registry.Length); // copy the second array into the first array starting at index 5

            if (wasEnabled)
            {
                newData = new byte[41];
                Array.Copy(data, 0, newData, 0, 22);
                Array.Copy(data, 25, newData, 23, 43 - 25);
                newData[18] = 0x13;
            }
            else
            {
                Array.Copy(data, 0, newData, 0, 22);
                Array.Copy(data, 23, newData, 25, 41 - 23);
                newData[18] = 0x15;
                newData[23] = 0x10;
                newData[24] = 0x00;
            }

            for (int i = 10; i < 15; i++)
            {
                if (newData[i] != 0xff)
                {
                    newData[i]++;
                    break;
                }
            }

            registryKey.SetValue("Data", newData, RegistryValueKind.Binary);
            registryKey.Flush();
        }
        return value;
    }

    public static bool? Toggle()
    {
        if (!Supported) return null;
        var isEnabled = Get() == 1;
        return Set(!isEnabled);
    }

    public static bool Supported
    {
        get => registryKey != null;
    }

    public static void SubscribeToEvents(Action<object?, RegistryChangedEventArgs> EventHandler)
    {
        try
        {
            watcher.RegistryChanged += new EventHandler<RegistryChangedEventArgs>(EventHandler);
            watcher.StartWatching();
        }
        catch
        {
            LogManager.LogError("Cannot connect to Night Light registry");
            throw;
        }
    }

    public static void Unsubscribe()
    {
        watcher.StopWatching();
    }
}
