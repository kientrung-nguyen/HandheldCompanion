using HandheldCompanion.Devices;
using HandheldCompanion.Misc;
using HandheldCompanion.Shared;
using HandheldCompanion.Utils;
using HandheldCompanion.Views;
using HandheldCompanion.Views.Windows;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace HandheldCompanion.Managers
{
    static class PowerProfileManager
    {
        private static PowerProfile currentProfile;

        public static Dictionary<Guid, PowerProfile> profiles = [];

        private static string ProfilesPath;

        public static bool IsInitialized;

        static PowerProfileManager()
        {
            // initialize path(s)
            ProfilesPath = Path.Combine(MainWindow.SettingsPath, "powerprofiles");
            if (!Directory.Exists(ProfilesPath))
                Directory.CreateDirectory(ProfilesPath);
        }

        public static async Task Start()
        {
            if (IsInitialized)
                return;

            // process existing profiles
            var fileEntries = Directory.GetFiles(ProfilesPath, "*.json", SearchOption.AllDirectories);
            foreach (var fileName in fileEntries)
                ProcessProfile(fileName);

            foreach (var devicePowerProfile in IDevice.GetCurrent().DevicePowerProfiles)
            {
                if (!profiles.ContainsKey(devicePowerProfile.Guid))
                    UpdateOrCreateProfile(devicePowerProfile, UpdateSource.Serializer);
            }

            // manage events
            PlatformManager.LibreHardwareMonitor.CPUTemperatureChanged += LibreHardwareMonitor_CpuTemperatureChanged;
            ProfileManager.Applied += ProfileManager_Applied;
            ProfileManager.Discarded += ProfileManager_Discarded;
            SystemManager.PowerLineStatusChanged += SystemManager_PowerLineStatusChanged;

            // raise events
            if (ProfileManager.IsInitialized)
            {
                ProfileManager_Applied(ProfileManager.GetCurrent(), UpdateSource.Background);
            }

            IsInitialized = true;
            Initialized?.Invoke();

            LogManager.LogInformation("{0} has started", "PowerProfileManager");
            return;
        }

        public static void Stop()
        {
            if (!IsInitialized)
                return;

            // manage events
            PlatformManager.LibreHardwareMonitor.CPUTemperatureChanged -= LibreHardwareMonitor_CpuTemperatureChanged;
            ProfileManager.Applied -= ProfileManager_Applied;
            ProfileManager.Discarded -= ProfileManager_Discarded;
            SystemManager.PowerLineStatusChanged -= SystemManager_PowerLineStatusChanged;

            IsInitialized = false;

            LogManager.LogInformation("{0} has stopped", "PowerProfileManager");
        }

        private static void LibreHardwareMonitor_CpuTemperatureChanged(object? value)
        {
            if (currentProfile is null || currentProfile.FanProfile is null || value is null)
                return;

            // update fan profile
            currentProfile.FanProfile.SetTemperature((float)value);

            switch (currentProfile.FanProfile.FanMode)
            {
                default:
                case FanMode.Hardware:
                    return;
                case FanMode.Software:
                    double fanSpeed = currentProfile.FanProfile.GetFanSpeed();
                    IDevice.GetCurrent().SetFanDuty(fanSpeed);
                    return;
            }
        }

        private static void SystemManager_PowerLineStatusChanged(PowerLineStatus powerLineStatus)
        {
            // Get current profile
            Profile profile = ProfileManager.GetCurrent();

            ProfileManager_Applied(profile, UpdateSource.Background);
        }

        private static void ProfileManager_Applied(Profile profile, UpdateSource source)
        {
            var powerProfile = GetProfile(profile.PowerProfiles[(int)SystemInformation.PowerStatus.PowerLineStatus]);
            if (powerProfile is null)
                return;

            // update current profile
            //currentProfile = powerProfile;

            ApplyProfile(powerProfile, source);
        }

        private static void ProfileManager_Discarded(Profile profile, bool swapped)
        {
            // reset current profile
            currentProfile = null;

            var powerProfile = GetProfile(profile.PowerProfiles[(int)SystemInformation.PowerStatus.PowerLineStatus]);
            if (powerProfile is null)
                return;

            // don't bother discarding settings, new one will be enforce shortly
            if (!swapped)
                Discarded?.Invoke(powerProfile);
        }

        private static void ProcessProfile(string fileName)
        {
            PowerProfile profile = null;

            try
            {
                string rawName = Path.GetFileNameWithoutExtension(fileName);
                if (string.IsNullOrEmpty(rawName))
                    throw new Exception("Profile has an incorrect file name.");

                string outputraw = File.ReadAllText(fileName);
                JObject jObject = JObject.Parse(outputraw);

                // latest pre-versionning release
                Version version = new("0.15.0.4");
                if (jObject.TryGetValue("Version", out var value))
                    version = new Version(value.ToString());

                profile = JsonConvert.DeserializeObject<PowerProfile>(outputraw, new JsonSerializerSettings
                {
                    TypeNameHandling = TypeNameHandling.All
                });
            }
            catch (Exception ex)
            {
                LogManager.LogError("Could not parse power profile {0}. {1}", fileName, ex.Message);
            }

            // failed to parse
            if (profile is null || profile.Name is null)
            {
                LogManager.LogError("Failed to parse power profile: {0}", fileName);
                return;
            }

            UpdateOrCreateProfile(profile, UpdateSource.Serializer);
        }

        public static void UpdateOrCreateProfile(PowerProfile profile, UpdateSource source)
        {
            switch (source)
            {
                case UpdateSource.Serializer:
                    LogManager.LogInformation($"Loaded power profile: {profile.Name}");
                    break;

                default:
                    LogManager.LogInformation($"Attempting to update/create power profile: {profile.Name}");
                    break;
            }

            // update database
            profiles[profile.Guid] = profile;

            // raise event
            Updated?.Invoke(profile, source);

            if (source == UpdateSource.Serializer)
                return;

            // warn owner
            bool isCurrent = profile.Guid == currentProfile?.Guid || source == UpdateSource.PowerStatusChange;

            if (isCurrent)
                ApplyProfile(profile, source);

            // serialize profile
            SerializeProfile(profile);
        }

        private static void ApplyProfile(PowerProfile profile, UpdateSource source = UpdateSource.Background, bool announce = true)
        {
            try
            {
                // might not be the same anymore if disabled
                profile = GetProfile(profile.Guid);

                // we've already announced this profile
                if (currentProfile is not null)
                    if (currentProfile.Guid == profile.Guid)
                        announce = false;

                // update current profile before invoking event
                currentProfile = profile;

                // raise event
                Applied?.Invoke(profile, source);

                // todo: localize me
                if (announce)
                {
                    string announcement = $"Power Profile {profile.Name} applied";
                    // push announcement
                    LogManager.LogInformation(announcement);
                    ToastManager.RunToast($"{profile.Name}",
                        SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Online
                        ? ToastIcons.Charger
                        : ToastIcons.Battery);
                }
            }
            catch
            {
            }
        }

        public static bool Contains(Guid guid)
        {
            return profiles.ContainsKey(guid);
        }

        public static bool Contains(PowerProfile profile)
        {
            return profiles.ContainsValue(profile);
        }

        public static PowerProfile GetProfile(Guid guid)
        {
            if (profiles.TryGetValue(guid, out var profile))
                return profile;

            return GetDefault();
        }

        private static bool HasDefault(bool AC = true)
        {
            return profiles.Values.Any(a => a.Default && a.Guid == (AC ? Guid.Empty : new Guid("00000000-0000-0000-0000-010000000000")));
        }

        public static PowerProfile GetDefault(bool AC = true)
        {
            if (HasDefault(AC))
                return profiles.Values.First(a => a.Default && a.Guid == (AC ? Guid.Empty : new Guid("00000000-0000-0000-0000-010000000000")));
            return new PowerProfile();
        }

        public static PowerProfile GetCurrent()
        {
            if (currentProfile is not null)
                return currentProfile;

            return GetDefault();
        }

        public static void SerializeProfile(PowerProfile profile)
        {
            // update profile version to current build
            profile.Version = new Version(MainWindow.fileVersionInfo.FileVersion);

            var jsonString = JsonConvert.SerializeObject(profile, Formatting.Indented, new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.All
            });

            // prepare for writing
            var profilePath = Path.Combine(ProfilesPath, profile.GetFileName());

            try
            {
                if (FileUtils.IsFileWritable(profilePath))
                    File.WriteAllText(profilePath, jsonString);
            }
            catch { }
        }

        public static void DeleteProfile(PowerProfile profile)
        {
            string profilePath = Path.Combine(ProfilesPath, profile.GetFileName());

            if (profiles.Remove(profile.Guid))
            {
                // warn owner
                bool isCurrent = profile.Guid == currentProfile?.Guid;

                // raise event
                Discarded?.Invoke(profile);

                // raise event(s)
                Deleted?.Invoke(profile);

                // send toast
                // todo: localize me
                ToastManager.SendToast("Power Profile", $"{profile.FileName} deleted");

                LogManager.LogInformation("Deleted power profile {0}", profilePath);
            }

            FileUtils.FileDelete(profilePath);
        }

        #region events
        public static event DeletedEventHandler Deleted;
        public delegate void DeletedEventHandler(PowerProfile profile);

        public static event UpdatedEventHandler Updated;
        public delegate void UpdatedEventHandler(PowerProfile profile, UpdateSource source);

        public static event AppliedEventHandler Applied;
        public delegate void AppliedEventHandler(PowerProfile profile, UpdateSource source);

        public static event DiscardedEventHandler Discarded;
        public delegate void DiscardedEventHandler(PowerProfile profile);

        public static event InitializedEventHandler Initialized;
        public delegate void InitializedEventHandler();
        #endregion
    }
}
