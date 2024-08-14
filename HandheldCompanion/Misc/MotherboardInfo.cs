using HandheldCompanion.Devices;
using HandheldCompanion.Views;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;

namespace HandheldCompanion;

public static class MotherboardInfo
{
    private static readonly ManagementObjectSearcher baseboardSearcher = new("root\\CIMV2", "SELECT * FROM Win32_BaseBoard");
    private static ManagementObjectCollection? baseboardCollection;

    private static readonly ManagementObjectSearcher motherboardSearcher = new("root\\CIMV2", "SELECT * FROM Win32_MotherboardDevice");
    private static ManagementObjectCollection? motherboardCollection;

    private static readonly ManagementObjectSearcher processorSearcher = new("root\\CIMV2", "SELECT * FROM Win32_Processor");
    private static ManagementObjectCollection? processorCollection;

    private static readonly ManagementObjectSearcher displaySearcher = new("root\\CIMV2", "SELECT * FROM Win32_DisplayConfiguration");
    private static ManagementObjectCollection? displayCollection;

    private static readonly ManagementObjectSearcher videoControllerSearcher = new("root\\CIMV2", "SELECT * FROM Win32_VideoController");
    private static ManagementObjectCollection? videoControllerCollection;

    private static readonly ManagementObjectSearcher computerSearcher = new("root\\CIMV2", "SELECT * FROM Win32_ComputerSystem");
    private static ManagementObjectCollection? computerCollection;

    private static readonly ManagementObjectSearcher memorySearcher = new("root\\CIMV2", "SELECT * FROM Win32_PhysicalMemory");
    private static ManagementObjectCollection? memoryCollection;

    private static readonly ManagementObjectSearcher biosSearcher = new("root\\CIMV2", "SELECT * FROM Win32_BIOS");
    private static ManagementObjectCollection? biosCollection;

    private static object cacheLock = new();
    private static SortedDictionary<string, object> cache = new();

    private static readonly string cacheDirectory;
    private const string fileName = "motherboard.json";

    static MotherboardInfo()
    {
        cacheDirectory = Path.Combine(MainWindow.SettingsPath, "cache");
        if (!Directory.Exists(cacheDirectory))
            Directory.CreateDirectory(cacheDirectory);
    }

    private static Dictionary<string, KeyValuePair<ManagementObjectCollection, ManagementObjectSearcher>> collections = new()
    {
        { "baseboard", new(baseboardCollection, baseboardSearcher) },
        { "motherboard", new(motherboardCollection, motherboardSearcher) },
        { "processor", new(processorCollection, processorSearcher) },
        { "display", new(displayCollection, displaySearcher) },
        { "video", new(videoControllerCollection, videoControllerSearcher) },
        { "computer", new(computerCollection, computerSearcher) },
        { "memory", new(memoryCollection, memorySearcher) },
        { "bios", new(biosCollection, biosSearcher) }
    };

    // unused
    public static string Availability
    {
        get
        {
            string result = Convert.ToString(queryCacheValue("motherboard", "Availability"));
            return int.TryParse(result, out var value) ? GetAvailability(value) : result;
        }
    }

    // unused
    public static List<string> DisplayDescription => (List<string>)queryCacheValue("display", "Description");

    // unused
    public static bool HostingBoard => Convert.ToBoolean(queryCacheValue("baseboard", "HostingBoard"));

    // unused
    public static string InstallDate
    {
        get
        {
            string result = Convert.ToString(queryCacheValue("baseboard", "InstallDate"));
            return !string.IsNullOrEmpty(result) ? ConvertToDateTime(result) : result;
        }
    }

    public static string Manufacturer => Convert.ToString(queryCacheValue("baseboard", "Manufacturer"));

    // unused
    public static string Model => Convert.ToString(queryCacheValue("baseboard", "Model"));

    // unused
    public static string SystemManufacturer => Convert.ToString(queryCacheValue("computer", "Manufacturer"));

    // unused
    public static string SystemModel => Convert.ToString(queryCacheValue("computer", "Model"));

    public static int NumberOfCores => Convert.ToInt32(queryCacheValue("processor", "NumberOfCores"));

    public static int NumberOfLogicalProcessors => Convert.ToInt32(queryCacheValue("processor", "NumberOfLogicalProcessors"));

    // unused
    public static string PartNumber => Convert.ToString(queryCacheValue("baseboard", "PartNumber"));

    // unused
    public static string PNPDeviceID => Convert.ToString(queryCacheValue("motherboard", "PNPDeviceID"));

    // unused
    public static string PrimaryBusType => Convert.ToString(queryCacheValue("motherboard", "PrimaryBusType"));

    public static string ProcessorID => Convert.ToString(queryCacheValue("processor", "processorID")).TrimEnd();

    public static string ProcessorName => Convert.ToString(queryCacheValue("processor", "Name")).TrimEnd();

    public static string ProcessorManufacturer => Convert.ToString(queryCacheValue("processor", "Manufacturer")).TrimEnd();

    // unused
    public static uint ProcessorMaxClockSpeed => Convert.ToUInt32(queryCacheValue("processor", "MaxClockSpeed"));

    private static uint _ProcessorMaxTurboSpeed = 0;
    public static uint ProcessorMaxTurboSpeed
    {
        get
        {
            if (_ProcessorMaxTurboSpeed != 0)
                return _ProcessorMaxTurboSpeed;

            _ProcessorMaxTurboSpeed = IDevice.GetCurrent().CpuClock;

            return _ProcessorMaxTurboSpeed;
        }
    }

    public static string Product => Convert.ToString(queryCacheValue("baseboard", "Product"));

    // unused
    public static bool Removable => Convert.ToBoolean(queryCacheValue("baseboard", "Removable"));

    // unused
    public static bool Replaceable => Convert.ToBoolean(queryCacheValue("baseboard", "Replaceable"));

    // unused
    public static string RevisionNumber => Convert.ToString(queryCacheValue("motherboard", "RevisionNumber"));

    // unused
    public static string SecondaryBusType => Convert.ToString(queryCacheValue("motherboard", "SecondaryBusType"));

    // unused
    public static string SerialNumber => Convert.ToString(queryCacheValue("baseboard", "SerialNumber"));

    // unused
    public static string Status => Convert.ToString(queryCacheValue("baseboard", "Status"));

    public static string SystemName => Convert.ToString(queryCacheValue("motherboard", "SystemName"));

    public static string Version => Convert.ToString(queryCacheValue("baseboard", "Version"));

    public static string MemoryProduct => Convert.ToString(queryCacheValue("memory", "Manufacturer"));

    public static string MemoryModel => Convert.ToString(queryCacheValue("memory", "PartNumber"));

    public static double MemoryCapacity => Convert.ToDouble(queryCacheValue("memory", "Capacity"));

    public static int MemorySpeed => Convert.ToInt32(queryCacheValue("memory", "ConfiguredClockSpeed"));

    public static int MemoryType => Convert.ToInt32(queryCacheValue("memory", "SMBIOSMemoryType"));

    public static string BiosVersion => Convert.ToString(queryCacheValue("bios", "Version"));

    public static string BiosManufacturer => Convert.ToString(queryCacheValue("bios", "Manufacturer"));

    public static string BiosName => Convert.ToString(queryCacheValue("bios", "Name"));

    public static string BiosReleaseDate
    {
        get
        {
            string result = Convert.ToString(queryCacheValue("bios", "ReleaseDate"));
            return !string.IsNullOrEmpty(result) ? ConvertToDateTime(result) : result;
        }
    }


    public static string GraphicName => Convert.ToString(queryCacheValue("video", "Name"));
    public static string GraphicDriverVersion => Convert.ToString(queryCacheValue("video", "DriverVersion"));

    private static object queryCacheValue(string collectionName, string query)
    {
        bool hasvalue = false;
        object returnValue = string.Empty;
        // pull value if it exsts and check if correct
        if (cache.TryGetValue($"{collectionName}-{query}", out object? result))
        {
            switch (result)
            {
                case string[] a when a.Length > 0:
                    returnValue = string.Join(", ", a);
                    hasvalue = true;
                    break;
                case string s when !string.IsNullOrEmpty(s):
                case double d when !double.IsNaN(d):
                case int i when i != 0:
                case uint ui when ui != 0:
                    returnValue = result;
                    hasvalue = true;
                    break;
            }
        }

        if (!hasvalue)
        {
            ManagementObjectCollection collection = collections[collectionName].Key;
            ManagementObjectSearcher searcher = collections[collectionName].Value;

            // use searcher if collection is null
            collection ??= searcher.Get();

            // set or update result
            if (collectionName == "memory" && query == "Capacity")
                result = collection.Cast<ManagementObject>().Select(queryObj => queryObj[query]).Sum(result => Convert.ToDouble(result ?? 0));
            else
                result = collection.Cast<ManagementObject>().Select(queryObj => queryObj[query]).FirstOrDefault(result => result != null);
            if (result != null)
            {
                // update cache
                cache[$"{collectionName}-{query}"] = result;
                writeCache();
                switch (result)
                {
                    case string[] a when a.Length > 0:
                        returnValue = string.Join(", ", a);
                        break;
                    case string s when !string.IsNullOrEmpty(s):
                    case double d when !double.IsNaN(d):
                    case int i when i != 0:
                    case uint ui when ui != 0:
                        returnValue = result;
                        break;
                }
            }
            else return string.Empty;
        }

        return returnValue;
    }

    private static string GetAvailability(int availability)
    {
        switch (availability)
        {
            case 1: return "Other";
            case 2: return "Unknown";
            case 3: return "Running or Full Power";
            case 4: return "Warning";
            case 5: return "In Test";
            case 6: return "Not Applicable";
            case 7: return "Power Off";
            case 8: return "Off Line";
            case 9: return "Off Duty";
            case 10: return "Degraded";
            case 11: return "Not Installed";
            case 12: return "Install Error";
            case 13: return "Power Save - Unknown";
            case 14: return "Power Save - Low Power Mode";
            case 15: return "Power Save - Standby";
            case 16: return "Power Cycle";
            case 17: return "Power Save - Warning";
            default: return "Unknown";
        }
    }

    private static string ConvertToDateTime(string unconvertedTime)
    {
        var convertedTime = "";
        var year = int.Parse(unconvertedTime.Substring(0, 4));
        var month = int.Parse(unconvertedTime.Substring(4, 2));
        var date = int.Parse(unconvertedTime.Substring(6, 2));
        var hours = int.Parse(unconvertedTime.Substring(8, 2));
        var minutes = int.Parse(unconvertedTime.Substring(10, 2));
        var seconds = int.Parse(unconvertedTime.Substring(12, 2));
        var meridian = "AM";
        if (hours > 12)
        {
            hours -= 12;
            meridian = "PM";
        }

        convertedTime = date + "/" + month + "/" + year;
        if (hours != 0 || minutes != 0 || seconds != 0)
            convertedTime += " " + hours + ":" + minutes + ":" + seconds + " " + meridian;
        return convertedTime;
    }

    public static bool Collect()
    {
        lock (cacheLock)
        {
            string cacheFile = Path.Combine(cacheDirectory, fileName);
            if (File.Exists(cacheFile))
            {
                string cacheJSON = File.ReadAllText(cacheFile);

                SortedDictionary<string, object>? cache = JsonConvert.DeserializeObject<SortedDictionary<string, object>>(cacheJSON, new JsonSerializerSettings
                {
                    TypeNameHandling = TypeNameHandling.All
                });

                if (cache is not null)
                {
                    MotherboardInfo.cache = cache;
                    return true;
                }
            }

            return false;
        }
    }

    private static void writeCache()
    {
        lock (cacheLock)
        {
            string cacheFile = Path.Combine(cacheDirectory, fileName);

            string jsonString = JsonConvert.SerializeObject(cache, Formatting.Indented, new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.All
            });

            File.WriteAllText(cacheFile, jsonString);
        }
    }
}