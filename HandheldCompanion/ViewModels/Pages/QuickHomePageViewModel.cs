using GongSolutions.Wpf.DragDrop;
using HandheldCompanion.Devices;
using HandheldCompanion.GraphicsProcessingUnit;
using HandheldCompanion.Managers;
using HandheldCompanion.Misc;
using HandheldCompanion.Processors;
using HandheldCompanion.Shared;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Timers;
using System.Windows.Data;
using System.Windows.Input;
using static HandheldCompanion.Processors.IntelProcessor;

namespace HandheldCompanion.ViewModels
{
    public class QuickHomePageViewModel : BaseViewModel, IDropTarget
    {
        public ObservableCollection<HotkeyViewModel> HotkeysList { get; set; } = [];
        public bool IsRunningLHM => ManagerFactory.platformManager.IsReady && PlatformManager.LibreHardware.IsInstalled;

        private Timer updateTimer;
        private int updateInterval = 1000;

        public ICommand FanPresetSilentCommand { get; private set; } = new DelegateCommand(() => { });
        public ICommand FanPresetHardwareCommand { get; private set; } = new DelegateCommand(() => { });
        public ICommand FanPresetSoftwareCommand { get; private set; } = new DelegateCommand(() => { });
        public ICommand FanPresetTurboCommand { get; private set; } = new DelegateCommand(() => { });

        public QuickHomePageViewModel()
        {
            // Enable thread-safe access to the collection
            BindingOperations.EnableCollectionSynchronization(HotkeysList, _collectionLock);

            CPUName = IDevice.GetCurrent().Processor;

            updateTimer = new Timer(updateInterval) { Enabled = false };
            updateTimer.Elapsed += UpdateTimer_Elapsed;

            // manage events
            if (PerformanceManager.IsInitialized && PerformanceManager.GetProcessor() is Processor processor)
                PerformanceManager_Initialized(processor.CanChangeTDP, processor.CanChangeGPU);
            else
                PerformanceManager.Initialized += PerformanceManager_Initialized;

            switch (ManagerFactory.gpuManager.Status)
            {
                default:
                case ManagerStatus.Initializing:
                    ManagerFactory.gpuManager.Initialized += GpuManager_Initialized;
                    break;
                case ManagerStatus.Initialized:
                    QueryGPU();
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

            ManagerFactory.powerProfileManager.Applied += PowerProfileManager_Applied;
            // raise events
            switch (ManagerFactory.powerProfileManager.Status)
            {
                default:
                case ManagerStatus.Initializing:
                    ManagerFactory.powerProfileManager.Initialized += PowerProfileManager_Initialized;
                    break;
                case ManagerStatus.Initialized:
                    QueryPowerProfile();
                    break;
            }

            // raise events
            switch (ManagerFactory.hotkeysManager.Status)
            {
                default:
                case ManagerStatus.Initializing:
                    ManagerFactory.hotkeysManager.Initialized += HotkeysManager_Initialized;
                    break;
                case ManagerStatus.Initialized:
                    QueryHotkeys();
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

            FanPresetHardwareCommand = new DelegateCommand(() =>
            {
                SelectedPreset.FanProfile.fanMode = FanMode.Hardware;
                // Temporary until view dependencies could be removed
                OnPropertyChanged(nameof(FanSpeedOverrideValue));
                OnPropertyChanged(nameof(IsFanModeBalance));
                OnPropertyChanged(nameof(IsFanModeHardware));
                OnPropertyChanged(nameof(SupportsFanMode));
            });

            FanPresetSoftwareCommand = new DelegateCommand(() =>
            {
                SelectedPreset.FanProfile.fanMode = FanMode.Software;
                SelectedPreset.FanProfile.fanModePreset = FanModePreset.Balance;
                for (int idx = 0; idx < SelectedPreset.FanProfile.fanSpeeds.Length; idx++)
                    SelectedPreset.FanProfile.fanSpeeds[idx] = IDevice.GetCurrent().fanPresets[1][idx];
                // Temporary until view dependencies could be removed
                OnPropertyChanged(nameof(FanSpeedOverrideValue));
                OnPropertyChanged(nameof(IsFanModeBalance));
                OnPropertyChanged(nameof(IsFanModeHardware));
                OnPropertyChanged(nameof(SupportsFanMode));
            });

            FanPresetSilentCommand = new DelegateCommand(() =>
            {
                SelectedPreset.FanProfile.fanMode = FanMode.Software;
                SelectedPreset.FanProfile.fanModePreset = FanModePreset.Quiet;
                for (int idx = 0; idx < SelectedPreset.FanProfile.fanSpeeds.Length; idx++)
                    SelectedPreset.FanProfile.fanSpeeds[idx] = IDevice.GetCurrent().fanPresets[0][idx];
                // Temporary until view dependencies could be removed
                OnPropertyChanged(nameof(FanSpeedOverrideValue));
                OnPropertyChanged(nameof(IsFanModeBalance));
                OnPropertyChanged(nameof(IsFanModeHardware));
                OnPropertyChanged(nameof(SupportsFanMode));
            });

            FanPresetTurboCommand = new DelegateCommand(() =>
            {
                SelectedPreset.FanProfile.fanMode = FanMode.Software;
                SelectedPreset.FanProfile.fanModePreset = FanModePreset.Turbo;
                for (int idx = 0; idx < SelectedPreset.FanProfile.fanSpeeds.Length; idx++)
                    SelectedPreset.FanProfile.fanSpeeds[idx] = IDevice.GetCurrent().fanPresets[2][idx];
                // Temporary until view dependencies could be removed
                OnPropertyChanged(nameof(FanSpeedOverrideValue));
                OnPropertyChanged(nameof(IsFanModeBalance));
                OnPropertyChanged(nameof(IsFanModeHardware));
                OnPropertyChanged(nameof(SupportsFanMode));
            });

            PropertyChanged += (sender, e) =>
            {
                if (SelectedPreset is null || SelectedPreset.Name is null)
                    return;

                // skip PropertyChanged updates for specific properties
                switch (e.PropertyName)
                {
                    case "FanSpeedOverrideValue":
                    case "PL1OverrideValue":
                    case "PL2OverrideValue":
                        // trigger power profile update but don't freeze UI
                        // todo: implement proper debounce
                        SubmitSelectedPreset();
                        break;

                    case "":
                    case "SelectedPreset":
                    default:
                        return;
                }

            };
        }

        private void UpdateTimer_Elapsed(object? sender, ElapsedEventArgs e)
        {
            GPU? gpu = GPUManager.GetCurrent();
            if (gpu is not null)
            {
                if (gpu.HasPower())
                    GPUPower = OverlayEntryElement.FormatValue((float)gpu.GetPower(), "W");

                if (gpu.HasLoad())
                    GPULoad = OverlayEntryElement.FormatValue((float)gpu.GetLoad(), "%");

                if (gpu.HasTemperature())
                    GPUTemperature = OverlayEntryElement.FormatValue((float)gpu.GetTemperature(), "°");
            }
        }

        private void QueryGPU()
        {
            // manage events
            ManagerFactory.gpuManager.Hooked += GPUManager_Hooked;

            GPU? gpu = GPUManager.GetCurrent();
            if (gpu is not null)
                GPUManager_Hooked(gpu);
        }

        private void QueryPlatforms()
        {
            // manage events
            PlatformManager.LibreHardware.CPUPowerChanged += LibreHardwareMonitor_CPUPowerChanged;
            PlatformManager.LibreHardware.CPUTemperatureChanged += LibreHardwareMonitor_CPUTemperatureChanged;
            PlatformManager.LibreHardware.CPULoadChanged += LibreHardwareMonitor_CPULoadChanged;
            PlatformManager.LibreHardware.CPUFanSpeedChanged += LibreHardware_CPUFanSpeedChanged;
            PlatformManager.LibreHardware.CPUClockChanged += LibreHardware_CPUClockChanged;

            OnPropertyChanged(nameof(IsRunningLHM));
        }

        private void PlatformManager_Initialized()
        {
            QueryPlatforms();
        }

        private void GpuManager_Initialized()
        {
            QueryGPU();
        }

        private async void GPUManager_Hooked(GPU GPU)
        {
            // localize me
            GPUName = GPU is not null ? GPU.adapterInformation.Details.Description : "No GPU detected";

            HasGPUPower = GPU is not null && GPU.HasPower();
            HasGPUTemperature = GPU is not null && GPU.HasTemperature();
            HasGPULoad = GPU is not null && GPU.HasLoad();

            if (IDevice.GetCurrent().GpuMonitor && (!HasGPUPower || !HasGPUTemperature || !HasGPULoad))
            {
                // wait until Platform Manager (LibreHardware) is ready, not ideal ?
                while (!ManagerFactory.platformManager.IsReady)
                    await Task.Delay(250).ConfigureAwait(false);

                if (!HasGPUPower) PlatformManager.LibreHardware.GPUPowerChanged += LibreHardwareMonitor_GPUPowerChanged;
                if (!HasGPUTemperature) PlatformManager.LibreHardware.GPUTemperatureChanged += LibreHardwareMonitor_GPUTemperatureChanged;
                if (!HasGPULoad) PlatformManager.LibreHardware.GPULoadChanged += LibreHardwareMonitor_GPULoadChanged;
            }
        }

        private void LibreHardware_CPUFanSpeedChanged(float? value)
        {
            if (value is null)
                return;

            CPUFanSpeed = OverlayEntryElement.FormatValue((float)value, "rpm");
            CPUFanLoad = OverlayEntryElement.FormatValue((float)IDevice.GetCurrent().ReadFanDuty(), "%");
        }

        private void LibreHardware_CPUClockChanged(float? value)
        {
            if (value is null)
                return;
            CPUClock = value > 1000
                ? OverlayEntryElement.FormatValue((float)value / 1000f, CPUClockUnit = "GHz")
                : OverlayEntryElement.FormatValue((float)value, CPUClockUnit = "MHz");
            CPUClockMaximum = value > 1000
                ? IDevice.GetCurrent().CpuClock / 1000f
                : IDevice.GetCurrent().CpuClock;
        }

        private void LibreHardwareMonitor_CPULoadChanged(float? value)
        {
            if (value is null)
                return;

            CPULoad = OverlayEntryElement.FormatValue((float)value, "%");
        }

        private void LibreHardwareMonitor_CPUTemperatureChanged(float? value)
        {
            if (value is null)
                return;

            CPUTemperature = OverlayEntryElement.FormatValue((float)value, "°C");
        }

        private void LibreHardwareMonitor_CPUPowerChanged(float? value)
        {
            if (value is null)
                return;

            CPUPower = OverlayEntryElement.FormatValue((float)value, "W");
        }

        private void LibreHardwareMonitor_GPULoadChanged(float? value)
        {
            if (value is null)
                return;

            // todo: improve me
            if (!HasGPULoad)
                HasGPULoad = value != 0.0f;

            GPULoad = OverlayEntryElement.FormatValue((float)value, "%");
        }

        private void LibreHardwareMonitor_GPUTemperatureChanged(float? value)
        {
            if (value is null)
                return;

            // todo: improve me
            if (!HasGPUTemperature)
                HasGPUTemperature = value != 0.0f;

            GPUTemperature = OverlayEntryElement.FormatValue((float)value, "°C");
        }

        private void LibreHardwareMonitor_GPUPowerChanged(float? value)
        {
            if (value is null)
                return;

            // todo: improve me
            if (!HasGPUPower)
                HasGPUPower = value != 0.0f;

            GPUPower = OverlayEntryElement.FormatValue((float)value, "W");
        }


        private void PowerProfileManager_Initialized()
        {
            QueryPowerProfile();
        }


        private void QueryPowerProfile()
        {
            ManagerFactory.powerProfileManager.Updated += PowerProfileManager_Updated;
            ManagerFactory.powerProfileManager.Deleted += PowerProfileManager_Deleted;
            SelectedPreset = ManagerFactory.powerProfileManager.GetCurrent();
        }


        private void SubmitSelectedPreset()
        {
            Task.Run(() =>
            {
                ManagerFactory.powerProfileManager.UpdateOrCreateProfile(SelectedPreset, UpdateSource.QuickProfilesPage);
            });
        }

        private void PerformanceManager_Initialized(bool CanChangeTDP, bool CanChangeGPU)
        {
            OnPropertyChanged(nameof(SupportsTDP));
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
            OnPropertyChanged("ConfigurableTDPOverride");
            OnPropertyChanged("ConfigurableTDPOverrideDown");
            OnPropertyChanged("ConfigurableTDPOverrideUp");
        }


        private void SettingsManager_SettingValueChanged(string name, object? value, bool temporary)
        {
            if (value is null)
                return;

            switch (name)
            {
                case "ConfigurableTDPOverride":
                case "ConfigurableTDPOverrideDown":
                case "ConfigurableTDPOverrideUp":
                    OnPropertyChanged(name);
                    break;
            }
        }

        private PowerProfile _selectedProfile;

        private void PowerProfileManager_Applied(PowerProfile profile, UpdateSource source)
        {
            SelectedPreset = profile;
        }

        private void PowerProfileManager_Updated(PowerProfile preset, UpdateSource source)
        {
            if (SelectedPreset?.Guid != preset.Guid)
                return;

            OnPropertyChanged(string.Empty);
        }

        private void PowerProfileManager_Deleted(PowerProfile preset)
        {
        }

        public PowerProfile SelectedPreset
        {
            get => _selectedProfile;
            set
            {
                if (_selectedProfile != value)
                {
                    _selectedProfile = value;
                    OnPropertyChanged(string.Empty);
                }
            }
        }

        public bool IsFanModeHardware
        {
            get => SelectedPreset?.FanProfile.fanMode == FanMode.Hardware;
        }

        public bool IsFanModeBalance
        {
            get => SelectedPreset?.FanProfile.fanMode == FanMode.Software
                && SelectedPreset?.FanProfile.fanModePreset == FanModePreset.Balance;
        }

        public bool IsFanModeQuiet
        {
            get => SelectedPreset?.FanProfile.fanMode == FanMode.Software
                && SelectedPreset?.FanProfile.fanModePreset == FanModePreset.Quiet;
        }

        public bool IsFanModeTurbo
        {
            get => SelectedPreset?.FanProfile.fanMode == FanMode.Software
                && SelectedPreset?.FanProfile.fanModePreset == FanModePreset.Turbo;
        }

        public bool SupportsFanMode => SelectedPreset?.FanProfile.fanMode == FanMode.Software;

        public bool SupportsTDP => PerformanceManager.GetProcessor()?.CanChangeTDP ?? false;

        public double ConfigurableTDPOverrideDown
        {
            get => ManagerFactory.settingsManager.GetBoolean("ConfigurableTDPOverride")
                ? ManagerFactory.settingsManager.GetDouble(Settings.ConfigurableTDPOverrideDown)
                : IDevice.GetCurrent().cTDP[0];
        }

        public double ConfigurableTDPOverrideUp
        {
            get => ManagerFactory.settingsManager.GetBoolean("ConfigurableTDPOverride")
                ? ManagerFactory.settingsManager.GetDouble(Settings.ConfigurableTDPOverrideUp)
                : IDevice.GetCurrent().cTDP[1];
        }

        private bool _coerceGuard;
        private double RequiredDelta
        {
            get
            {
                if (PerformanceManager.GetProcessor() is IntelProcessor ip)
                {
                    // Official specification for Lunar Lake states that PL2 should always be at least 1 W higher than PL1
                    if (ip.MicroArch == IntelMicroArch.LunarLake)
                        return 1.0d;
                }

                return 0.0d;
            }
        }

        public double FanSpeedOverrideValue
        {
            get => Math.Truncate(SelectedPreset?.FanProfile.fanSpeeds.Average() ?? 0);
            set
            {
                if (SelectedPreset?.FanProfile.fanSpeeds is null)
                    return;

                if (value != FanSpeedOverrideValue)
                {
                    var fanSpeeds = SelectedPreset.FanProfile.fanSpeeds
                        .Select(v => v - FanSpeedOverrideValue)
                        .Select(s => Math.Truncate(Math.Clamp(value + s * 1, 0, 100)))
                        .ToArray();
                    SelectedPreset.FanProfile.fanSpeeds = fanSpeeds;
                    OnPropertyChanged(nameof(FanSpeedOverrideValue));
                }
            }
        }

        // PL1 = Long/Sustained
        // On AMD also = STAPM ?
        public double PL1OverrideValue
        {
            get
            {
                double[] tdp = SelectedPreset?.TDPOverrideValues ?? IDevice.GetCurrent().nTDP;
                return tdp[(int)PowerType.Slow];
            }
            set
            {
                if (Math.Abs(value - PL1OverrideValue) < double.Epsilon) return;

                double clamped = Math.Max(ConfigurableTDPOverrideDown,
                                  Math.Min(value, ConfigurableTDPOverrideUp));

                if (SelectedPreset is null)
                    return;

                var selectedPreset = SelectedPreset;
                if (selectedPreset is null)
                    return;

                double[] tdpOverrideValues = selectedPreset.TDPOverrideValues ??= (double[])IDevice.GetCurrent().nTDP.Clone();

                tdpOverrideValues[(int)PowerType.Slow] = clamped;
                tdpOverrideValues[(int)PowerType.Stapm] = clamped;

                // If PL1 crosses PL2, bump PL2 up to maintain PL2 >= PL1 + Δ
                double minPl2 = clamped + RequiredDelta;

                if (!_coerceGuard && PL2OverrideValue < minPl2)
                {
                    try
                    {
                        _coerceGuard = true;
                        tdpOverrideValues[(int)PowerType.Fast] = Math.Min(ConfigurableTDPOverrideUp, minPl2);
                        OnPropertyChanged(nameof(PL2OverrideValue));
                    }
                    finally { _coerceGuard = false; }
                }

                OnPropertyChanged(nameof(PL1OverrideValue));
            }
        }

        // PL2 = Fast/Short
        public double PL2OverrideValue
        {
            get
            {
                double[] tdp = SelectedPreset?.TDPOverrideValues ?? IDevice.GetCurrent().nTDP;
                return tdp[(int)PowerType.Fast];
            }
            set
            {
                if (Math.Abs(value - PL2OverrideValue) < double.Epsilon) return;

                double minPl2 = PL1OverrideValue + RequiredDelta;
                double clamped = Math.Max(minPl2, Math.Min(value, ConfigurableTDPOverrideUp));

                var selectedPreset = SelectedPreset;
                if (selectedPreset is null)
                    return;

                double[] tdpOverrideValues = selectedPreset.TDPOverrideValues ??= (double[])IDevice.GetCurrent().nTDP.Clone();

                if (tdpOverrideValues[(int)PowerType.Fast] != clamped)
                {
                    tdpOverrideValues[(int)PowerType.Fast] = clamped;
                    OnPropertyChanged(nameof(PL2OverrideValue));
                }
            }
        }


        private string _CPUPower;
        public string CPUPower
        {
            get => _CPUPower;
            set
            {
                if (value != CPUPower)
                {
                    _CPUPower = value;
                    OnPropertyChanged(nameof(CPUPower));
                }
            }
        }

        private string _CPUTemperature;
        public string CPUTemperature
        {
            get => _CPUTemperature;
            set
            {
                if (value != CPUTemperature)
                {
                    _CPUTemperature = value;
                    OnPropertyChanged(nameof(CPUTemperature));
                }
            }
        }

        private string _CPULoad;
        public string CPULoad
        {
            get => _CPULoad;
            set
            {
                if (value != CPULoad)
                {
                    _CPULoad = value;
                    OnPropertyChanged(nameof(CPULoad));
                }
            }
        }

        private string _CPUClock;
        public string CPUClock
        {
            get => _CPUClock;
            set
            {
                if (value != CPUClock)
                {
                    _CPUClock = value;
                    OnPropertyChanged(nameof(CPUClock));
                }
            }
        }

        private float _CPUClockMaximum;
        public float CPUClockMaximum
        {
            get => _CPUClockMaximum;
            set
            {
                if (value != CPUClockMaximum)
                {
                    _CPUClockMaximum = value;
                    OnPropertyChanged(nameof(CPUClockMaximum));
                }
            }
        }

        private string _CPUClockUnit;
        public string CPUClockUnit
        {
            get => _CPUClockUnit;
            set
            {
                if (value != CPUClockUnit)
                {
                    _CPUClockUnit = value;
                    OnPropertyChanged(nameof(CPUClockUnit));
                }
            }
        }

        private string _CPUFanSpeed;
        public string CPUFanSpeed
        {
            get => _CPUFanSpeed;
            set
            {
                if (value != CPUFanSpeed)
                {
                    _CPUFanSpeed = value;
                    OnPropertyChanged(nameof(CPUFanSpeed));
                }
            }
        }

        private string _CPUFanLoad;
        public string CPUFanLoad
        {
            get => _CPUFanLoad;
            set
            {
                if (value != CPUFanLoad)
                {
                    _CPUFanLoad = value;
                    OnPropertyChanged(nameof(CPUFanLoad));
                }
            }
        }

        // localize me
        private string _CPUName = "No CPU detected";
        public string CPUName
        {
            get => _CPUName;
            set
            {
                if (value != CPUName)
                {
                    _CPUName = value;
                    OnPropertyChanged(nameof(CPUName));
                }
            }
        }

        // localize me
        private string _GPUName = "No GPU detected";
        public string GPUName
        {
            get => _GPUName;
            set
            {
                if (value != GPUName)
                {
                    _GPUName = value;
                    OnPropertyChanged(nameof(GPUName));
                }
            }
        }

        private bool _HasGPUPower;
        public bool HasGPUPower
        {
            get => _HasGPUPower;
            set
            {
                if (value != HasGPUPower)
                {
                    _HasGPUPower = value;
                    OnPropertyChanged(nameof(HasGPUPower));
                }
            }
        }

        private string _GPUPower;
        public string GPUPower
        {
            get => _GPUPower;
            set
            {
                if (value != GPUPower)
                {
                    _GPUPower = value;
                    OnPropertyChanged(nameof(GPUPower));
                }
            }
        }

        private bool _HasGPUTemperature;
        public bool HasGPUTemperature
        {
            get => _HasGPUTemperature;
            set
            {
                if (value != HasGPUTemperature)
                {
                    _HasGPUTemperature = value;
                    OnPropertyChanged(nameof(HasGPUTemperature));
                }
            }
        }

        private string _GPUTemperature;
        public string GPUTemperature
        {
            get => _GPUTemperature;
            set
            {
                if (value != GPUTemperature)
                {
                    _GPUTemperature = value;
                    OnPropertyChanged(nameof(GPUTemperature));
                }
            }
        }

        private bool _HasGPULoad;
        public bool HasGPULoad
        {
            get => _HasGPULoad;
            set
            {
                if (value != HasGPULoad)
                {
                    _HasGPULoad = value;
                    OnPropertyChanged(nameof(HasGPULoad));
                }
            }
        }

        private string _GPULoad;
        public string GPULoad
        {
            get => _GPULoad;
            set
            {
                if (value != GPULoad)
                {
                    _GPULoad = value;
                    OnPropertyChanged(nameof(GPULoad));
                }
            }
        }

        private void HotkeysManager_Initialized()
        {
            QueryHotkeys();
        }

        private void QueryHotkeys()
        {
            // manage events
            ManagerFactory.hotkeysManager.Updated += HotkeysManager_Updated;
            ManagerFactory.hotkeysManager.Deleted += HotkeysManager_Deleted;

            // raise events
            foreach (Hotkey hotkey in ManagerFactory.hotkeysManager.GetHotkeys().OrderByDescending(hotkey => hotkey.PinIndex != -1).ThenBy(hotkey => hotkey.ButtonFlags))
                HotkeysManager_Updated(hotkey);
        }

        void IDropTarget.DragOver(IDropInfo dropInfo)
        {
            dropInfo.Effects = System.Windows.DragDropEffects.All;
        }

        void IDropTarget.Drop(IDropInfo dropInfo)
        {
            if (dropInfo.Data is HotkeyViewModel source)
            {
                if (dropInfo.TargetItem is HotkeyViewModel target)
                {
                    int sourceIndex = HotkeysList.IndexOf(source);
                    int targetIndex = HotkeysList.IndexOf(target);

                    if (sourceIndex >= 0 && targetIndex >= 0 && sourceIndex != targetIndex)
                    {
                        // Remove the source item from its original position
                        HotkeysList.RemoveAt(sourceIndex);

                        // Insert the source item at the new target position
                        HotkeysList.Insert(targetIndex, source);

                        // Determine the range of affected items and their new indices
                        int start = Math.Min(sourceIndex, targetIndex);
                        int end = Math.Max(sourceIndex, targetIndex);

                        // Update the PinIndex of each affected item
                        for (int i = start; i <= end; i++)
                        {
                            HotkeysList[i].Hotkey.PinIndex = i;
                            ManagerFactory.hotkeysManager.UpdateOrCreateHotkey(HotkeysList[i].Hotkey);
                        }
                    }
                }
            }
        }

        private void HotkeysManager_Updated(Hotkey hotkey)
        {
            if (hotkey.IsInternal)
                return;

            lock (_collectionLock)
            {
                HotkeyViewModel? foundHotkey = HotkeysList.FirstOrDefault(p => p.Hotkey.ButtonFlags == hotkey.ButtonFlags);
                if (foundHotkey is null)
                {
                    if (hotkey.IsPinned)
                    {
                        int index = hotkey.PinIndex;
                        if (index > HotkeysList.Count || index < 0)
                            index = HotkeysList.Count;
                        HotkeysList.Insert(index, new HotkeyViewModel(hotkey, true));
                    }
                }
                else
                {
                    if (hotkey.IsPinned)
                        foundHotkey.Hotkey = hotkey;
                    else
                        HotkeysManager_Deleted(hotkey);
                }
            }
        }

        private void HotkeysManager_Deleted(Hotkey hotkey)
        {
            lock (_collectionLock)
            {
                HotkeyViewModel? foundHotkey = HotkeysList.FirstOrDefault(p => p.Hotkey.ButtonFlags == hotkey.ButtonFlags);
                if (foundHotkey is not null)
                {
                    HotkeysList.Remove(foundHotkey);
                    foundHotkey.Dispose();
                }
            }
        }

        public void OnNavigatedTo()
        {
            PlatformManager.LibreHardware.Resume();
            updateTimer.Start();
            LogManager.LogInformation("Quick Home OnNavigatedTo");
        }

        public void OnNavigatedFrom()
        {
            PlatformManager.LibreHardware.Pause();
            updateTimer.Stop();
            LogManager.LogInformation("Quick Home OnNavigatedFrom");
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                updateTimer.Stop();
                updateTimer.Dispose();
                ManagerFactory.settingsManager.SettingValueChanged -= SettingsManager_SettingValueChanged;
                ManagerFactory.settingsManager.Initialized -= SettingsManager_Initialized;
                ManagerFactory.platformManager.Initialized -= PlatformManager_Initialized;
                ManagerFactory.gpuManager.Hooked -= GPUManager_Hooked;
                ManagerFactory.gpuManager.Initialized -= GpuManager_Initialized;
				ManagerFactory.hotkeysManager.Updated -= HotkeysManager_Updated;
            	ManagerFactory.hotkeysManager.Deleted -= HotkeysManager_Deleted;
                ManagerFactory.hotkeysManager.Initialized -= HotkeysManager_Initialized;
                ManagerFactory.powerProfileManager.Initialized -= PowerProfileManager_Initialized;
                ManagerFactory.powerProfileManager.Applied -= PowerProfileManager_Applied;
                ManagerFactory.powerProfileManager.Updated -= PowerProfileManager_Updated;
            }

            base.Dispose(disposing);
        }
    }
}
