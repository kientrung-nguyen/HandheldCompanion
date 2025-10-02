using HandheldCompanion.Devices;
using HandheldCompanion.Misc;
using HandheldCompanion.Shared;
using HandheldCompanion.Utils;
using HandheldCompanion.Views;
using HandheldCompanion.Views.Windows;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace HandheldCompanion.Managers
{
    public class PowerProfileManager : IManager
    {
        private object profileLock = new();
        private PowerProfile currentProfile;

        public ConcurrentDictionary<Guid, PowerProfile> profiles = [];
        private Dictionary<int, Guid> Defaults = new()
        {
            { (int)PowerLineStatus.Offline, PowerProfile.DefaultDC },
            { (int)PowerLineStatus.Online, PowerProfile.DefaultAC }
        };


        public PowerProfileManager()
        {
            // initialize path
            ManagerPath = Path.Combine(App.SettingsPath, "powerprofiles");

            // create path
            if (!Directory.Exists(ManagerPath))
                Directory.CreateDirectory(ManagerPath);
        }

        public override void Start()
        {
            if (Status.HasFlag(ManagerStatus.Initializing) || Status.HasFlag(ManagerStatus.Initialized))
                return;

            base.PrepareStart();

            // process existing profiles
            string[] fileEntries = Directory.GetFiles(ManagerPath, "*.json", SearchOption.AllDirectories);
            foreach (var fileName in fileEntries)
                ProcessProfile(fileName);

            foreach (PowerProfile profile in IDevice.GetCurrent().DevicePowerProfiles)
            {
                // we want the OEM power profiles to be always up-to-date
                if (profile.DeviceDefault || (profile.Default && !profiles.ContainsKey(profile.Guid)))
                    UpdateOrCreateProfile(profile, UpdateSource.Serializer);
            }

            // manage events
            ManagerFactory.profileManager.Applied += ProfileManager_Applied;
            ManagerFactory.profileManager.Discarded += ProfileManager_Discarded;
            SystemManager.PowerLineStatusChanged += SystemManager_PowerLineStatusChanged;

            // raise events
            switch (ManagerFactory.profileManager.Status)
            {
                default:
                case ManagerStatus.Initializing:
                    ManagerFactory.profileManager.Initialized += ProfileManager_Initialized;
                    break;
                case ManagerStatus.Initialized:
                    QueryProfile();
                    break;
            }

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

            base.Start();
        }

        private void QueryPlatforms()
        {
            // manage events
            PlatformManager.LibreHardware.CPUTemperatureChanged += LibreHardwareMonitor_CpuTemperatureChanged;
        }

        private void PlatformManager_Initialized()
        {
            QueryPlatforms();
        }

        private void QueryProfile()
        {
            ProfileManager_Applied(ManagerFactory.profileManager.GetCurrent(), UpdateSource.Background);
        }

        private void ProfileManager_Initialized()
        {
            QueryProfile();
        }

        private void SettingsManager_Initialized()
        {
            QuerySettings();
        }

        private void QuerySettings()
        {
            // manage events
            ManagerFactory.settingsManager.SettingValueChanged += SettingsManager_SettingValueChanged;

            // raise events
            SettingsManager_SettingValueChanged("ConfigurableTDPOverrideDown", ManagerFactory.settingsManager.Get<string>("ConfigurableTDPOverrideDown"), false);
            SettingsManager_SettingValueChanged("ConfigurableTDPOverrideUp", ManagerFactory.settingsManager.Get<string>("ConfigurableTDPOverrideUp"), false);
        }

        public override void Stop()
        {
            if (Status.HasFlag(ManagerStatus.Halting) || Status.HasFlag(ManagerStatus.Halted))
                return;

            base.PrepareStop();

            // manage events
            PlatformManager.LibreHardware.CPUTemperatureChanged -= LibreHardwareMonitor_CpuTemperatureChanged;
            ManagerFactory.profileManager.Applied -= ProfileManager_Applied;
            ManagerFactory.profileManager.Discarded -= ProfileManager_Discarded;
            ManagerFactory.profileManager.Initialized -= ProfileManager_Initialized;
            SystemManager.PowerLineStatusChanged -= SystemManager_PowerLineStatusChanged;
            ManagerFactory.settingsManager.SettingValueChanged -= SettingsManager_SettingValueChanged;

            base.Stop();
        }

        private void SettingsManager_SettingValueChanged(string name, object value, bool temporary)
        {
            // Only process relevant setting names.
            if (name != "ConfigurableTDPOverrideDown" && name != "ConfigurableTDPOverrideUp")
                return;

            double threshold = Convert.ToDouble(value);

            // Define the condition based on the setting name.
            Func<double, bool> shouldUpdate = name == "ConfigurableTDPOverrideDown"
                ? (current => current < threshold)
                : (current => current > threshold);

            foreach (PowerProfile profile in profiles.Values)
            {
                bool updated = false;

                // Prevent null reference if TDPOverrideValues is null.
                if (profile.TDPOverrideValues == null)
                    continue;

                if (profile.IsDeviceDefault())
                    continue;

                // Loop through all override values
                for (int i = 0; i < profile.TDPOverrideValues.Length; i++)
                {
                    if (shouldUpdate(profile.TDPOverrideValues[i]))
                    {
                        profile.TDPOverrideValues[i] = threshold;
                        updated = true;
                    }
                }

                if (updated)
                    UpdateOrCreateProfile(profile, UpdateSource.Background);
            }
        }

        private void LibreHardwareMonitor_CpuTemperatureChanged(float? value)
        {
            lock (profileLock)
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
        }
        private void SystemManager_PowerLineStatusChanged(PowerLineStatus powerLineStatus)
        {
            // Get current profile
            Profile profile = ManagerFactory.profileManager.GetCurrent();

            ProfileManager_Applied(profile, UpdateSource.Background);
        }

        private void ProfileManager_Applied(Profile profile, UpdateSource source)
        {
            lock (profileLock)
            {
                var powerProfile = GetProfile(profile.PowerProfiles[(int)SystemInformation.PowerStatus.PowerLineStatus]);
                if (powerProfile is null)
                    return;

                // update current profile
                //currentProfile = powerProfile;

                ApplyProfile(powerProfile, source);
            }
        }

        private void ProfileManager_Discarded(Profile profile, bool swapped, Profile nextProfile)
        {
            lock (profileLock)
            {
                // reset current profile
                currentProfile = null;

                var powerProfile = GetProfile(profile.PowerProfiles[(int)SystemInformation.PowerStatus.PowerLineStatus]);
                if (powerProfile is null)
                    return;

                // don't bother discarding settings, new one will be enforce shortly
                Discarded?.Invoke(powerProfile, swapped);
            }
        }

        private void ProcessProfile(string fileName)
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
                LogManager.LogError("Could not parse power profile: {0}. {1}", fileName, ex.Message);
            }

            // failed to parse
            if (profile is null || profile.Name is null)
            {
                LogManager.LogError("Failed to parse power profile: {0}", fileName);
                return;
            }

            UpdateOrCreateProfile(profile, UpdateSource.Serializer);
        }

        public void UpdateOrCreateProfile(PowerProfile profile, UpdateSource source)
        {
            switch (source)
            {
                case UpdateSource.Serializer:
                    LogManager.LogInformation("Loaded power profile: {0}", profile.Name);
                    break;

                default:
                    LogManager.LogInformation("Attempting to update/create power profile: {0}", profile.Name);
                    break;
            }

            // update database
            profiles[profile.Guid] = profile;

            // raise event
            Updated?.Invoke(profile, source);

            if (source == UpdateSource.Serializer)
                return;

            lock (profileLock)
            {
                // warn owner
                bool isCurrent = profile.Guid == currentProfile?.Guid;

                if (isCurrent)
                    ApplyProfile(profile, source);

                // serialize profile
                SerializeProfile(profile);
            }
        }

        private void ApplyProfile(PowerProfile profile, UpdateSource source = UpdateSource.Background, bool announce = true)
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
            catch { }
        }

        public bool Contains(Guid guid)
        {
            return profiles.ContainsKey(guid);
        }

        public bool Contains(PowerProfile profile)
        {
            return profiles.ContainsKey(profile.Guid);
        }

        public PowerProfile GetProfile(Guid guid, PowerLineStatus powerLineStatus = PowerLineStatus.Offline)
        {
            if (profiles.TryGetValue(guid, out var profile))
                return profile;

            return GetDefault(powerLineStatus);
        }

        private bool HasDefault(PowerLineStatus powerLineStatus = PowerLineStatus.Offline)
        {
            return profiles.Values.Count(a => a.Default && a.Guid == Defaults[(int)powerLineStatus]) != 0;
        }

        public PowerProfile GetDefault(PowerLineStatus powerLineStatus = PowerLineStatus.Offline)
        {
            if (HasDefault(powerLineStatus))
                return profiles.Values.First(a => a.Default && a.Guid == Defaults[(int)powerLineStatus]);
            return new PowerProfile();
        }

        public PowerProfile GetCurrent()
        {
            lock (profileLock)
            {
                if (currentProfile is not null)
                    return currentProfile;

                return GetDefault();
            }
        }

        public void SerializeProfile(PowerProfile profile)
        {
            // update profile version to current build
            profile.Version = MainWindow.CurrentVersion;

            var jsonString = JsonConvert.SerializeObject(profile, Formatting.Indented, new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.All
            });

            // prepare for writing
            var profilePath = Path.Combine(ManagerPath, profile.GetFileName());

            try
            {
                if (FileUtils.IsFileWritable(profilePath))
                    File.WriteAllText(profilePath, jsonString);
            }
            catch { }
        }

        public void DeleteProfile(PowerProfile profile)
        {
            string profilePath = Path.Combine(ManagerPath, profile.GetFileName());

            if (profiles.Remove(profile.Guid, out _))
            {
                lock (profileLock)
                {
                    // warn owner
                    bool isCurrent = profile.Guid == currentProfile?.Guid;
                }
                // raise event
                Discarded?.Invoke(profile, false);

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
        public event DeletedEventHandler Deleted;
        public delegate void DeletedEventHandler(PowerProfile profile);

        public event UpdatedEventHandler Updated;
        public delegate void UpdatedEventHandler(PowerProfile profile, UpdateSource source);

        public event AppliedEventHandler Applied;
        public delegate void AppliedEventHandler(PowerProfile profile, UpdateSource source);

        public event DiscardedEventHandler Discarded;
        public delegate void DiscardedEventHandler(PowerProfile profile, bool swapped);
        #endregion
    }
}
