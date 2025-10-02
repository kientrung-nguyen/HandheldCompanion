using HandheldCompanion.Managers;
using System;
using System.IO;
using System.Text.RegularExpressions;

namespace HandheldCompanion.Misc
{
    [Serializable]
    public class PowerProfile
    {
        public static Guid DefaultAC = new("00000000-0000-0000-0000-010000000000");
        public static Guid DefaultDC = new("00000000-0000-0000-0000-000000000000");

        public string Name;
        public string Description;

        public string FileName { get; set; }
        public bool Default { get; set; }
        public bool DeviceDefault { get; set; }

        public Version Version { get; set; } = new();
        public Guid Guid { get; set; } = Guid.NewGuid();

        public bool TDPOverrideEnabled { get; set; }
        public double[] TDPOverrideValues { get; set; }

        public bool CPUOverrideEnabled { get; set; }
        public double CPUOverrideValue { get; set; }

        public bool GPUOverrideEnabled { get; set; }
        public double GPUOverrideValue { get; set; }

        public bool AutoTDPEnabled { get; set; }
        public float AutoTDPRequestedFPS { get; set; } = 30.0f;

        [Obsolete("This property is deprecated and will be removed in future versions.")]
        public bool EPPOverrideEnabled { get; set; }

        [Obsolete("This property is deprecated and will be removed in future versions.")]
        public uint EPPOverrideValue { get; set; } = 50;

        public bool CPUCoreEnabled { get; set; }
        public int CPUCoreCount { get; set; } = MotherboardInfo.NumberOfCores;

        public CoreParkingMode CPUParkingMode { get; set; } = CoreParkingMode.AllCoresAuto;

        public CPUBoostLevel CPUBoostLevel { get; set; } = CPUBoostLevel.Enabled;

        public FanProfile FanProfile { get; set; } = new();

        public bool IntelEnduranceGamingEnabled { get; set; } = false;
        public int IntelEnduranceGamingPreset { get; set; } = 0;

        public int OEMPowerMode { get; set; } = 0xFF;
        public Guid OSPowerMode { get; set; } = Managers.OSPowerMode.BetterPerformance;

        public PowerProfile() { }

        public PowerProfile(string name, string description, string fileName = "")
        {
            Name = name;
            Description = description;

            if (fileName.Length == 0)
                fileName = name;

            // Remove any invalid characters from the input
            string invalidChars = Regex.Escape(new string(Path.GetInvalidFileNameChars()));
            string output = Regex.Replace(fileName, "[" + invalidChars + "]", string.Empty);
            output = output.Trim();

            FileName = output;
        }

        public string GetFileName()
        {
            return $"{FileName}.json";
        }

        public bool IsDefault()
        {
            return Default/* && Guid == Guid.Empty*/;
        }

        public bool IsDeviceDefault()
        {
            return DeviceDefault;
        }

        public override string ToString()
        {
            return $"{Name} - {Description}";
        }
    }
}
