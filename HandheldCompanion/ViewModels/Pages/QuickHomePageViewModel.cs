using GongSolutions.Wpf.DragDrop;
using HandheldCompanion.Devices;
using HandheldCompanion.Managers;
using HandheldCompanion.Misc;
using HandheldCompanion.Processors;
using HandheldCompanion.Shared;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Data;
using static HandheldCompanion.Processors.IntelProcessor;

namespace HandheldCompanion.ViewModels
{
    public class QuickHomePageViewModel : BaseViewModel, IDropTarget
    {
        public ObservableCollection<HotkeyViewModel> HotkeysList { get; set; } = [];

        public QuickHomePageViewModel()
        {
            // Enable thread-safe access to the collection
            BindingOperations.EnableCollectionSynchronization(HotkeysList, _collectionLock);

            // manage events
            ManagerFactory.hotkeysManager.Updated += HotkeysManager_Updated;
            ManagerFactory.hotkeysManager.Deleted += HotkeysManager_Deleted;

            if (PerformanceManager.IsInitialized && PerformanceManager.GetProcessor() is Processor processor)
                PerformanceManager_Initialized(processor.CanChangeTDP, processor.CanChangeGPU);
            else
                PerformanceManager.Initialized += PerformanceManager_Initialized;


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

            PropertyChanged += (sender, e) =>
            {
                LogManager.LogInformation("{0} PropertyChanged '{1}'", "QuickHome", e.PropertyName ?? string.Empty);
                if (SelectedPreset is null || SelectedPreset.Name is null)
                    return;

                // skip PropertyChanged updates for specific properties
                switch (e.PropertyName)
                {
                    case "ModifyPresetName":
                    case "ModifyPresetDescription":
                    case "AutoTDPMaximum":
                    case "ConfigurableTDPOverride":
                    case "ConfigurableTDPOverrideDown":
                    case "ConfigurableTDPOverrideUp":
                    case "SupportsTDP":
                        return;
                }

                // No need to update 

                // trigger power profile update but don't freeze UI
                // todo: implement proper debounce
                SubmitSelectedPreset();
            };
        }

        private void PowerProfileManager_Initialized()
        {
            QueryPowerProfile();
        }


        private void QueryPowerProfile()
        {
            // manage events
            OnPropertyChanged(nameof(PL1OverrideValue));
            OnPropertyChanged(nameof(PL2OverrideValue));
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

        public PowerProfile SelectedPreset
        {
            get => ManagerFactory.powerProfileManager.GetCurrent();
        }

        public bool SupportsTDP => PerformanceManager.GetProcessor()?.CanChangeTDP ?? false;

        public double ConfigurableTDPOverrideDown
        {
            get => ManagerFactory.settingsManager.GetDouble(Settings.ConfigurableTDPOverrideDown);
        }

        public double ConfigurableTDPOverrideUp
        {
            get => ManagerFactory.settingsManager.GetDouble(Settings.ConfigurableTDPOverrideUp);
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


        // PL1 = Long/Sustained
        // On AMD also = STAPM ?
        public double PL1OverrideValue
        {
            get
            {
                double[] tdp = SelectedPreset?.TDPQuickValues ?? IDevice.GetCurrent().nTDP;
                if (tdp is not null)
                    return tdp[(int)PowerType.Slow];

                return PerformanceManager.GetProcessor()?.GetTDPLimit(PowerType.Slow) ?? IDevice.GetCurrent().nTDP[(int)PowerType.Slow];
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

                double[] tdpOverrideValues = selectedPreset.TDPQuickValues ??= (double[])IDevice.GetCurrent().nTDP.Clone();

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
                double[] tdp = SelectedPreset?.TDPQuickValues ?? IDevice.GetCurrent().nTDP;
                if (tdp is not null)
                    return tdp[(int)PowerType.Fast];

                return PerformanceManager.GetProcessor()?.GetTDPLimit(PowerType.Fast) ?? IDevice.GetCurrent().nTDP[(int)PowerType.Fast];
            }
            set
            {
                if (Math.Abs(value - PL2OverrideValue) < double.Epsilon) return;

                double minPl2 = PL1OverrideValue + RequiredDelta;
                double clamped = Math.Max(minPl2, Math.Min(value, ConfigurableTDPOverrideUp));

                var selectedPreset = SelectedPreset;
                if (selectedPreset is null)
                    return;

                double[] tdpOverrideValues = selectedPreset.TDPQuickValues ??= (double[])IDevice.GetCurrent().nTDP.Clone();

                if (tdpOverrideValues[(int)PowerType.Fast] != clamped)
                {
                    tdpOverrideValues[(int)PowerType.Fast] = clamped;
                    OnPropertyChanged(nameof(PL2OverrideValue));
                }
            }
        }


        private void HotkeysManager_Initialized()
        {
            QueryHotkeys();
        }

        private void QueryHotkeys()
        {
            foreach (Hotkey hotkey in ManagerFactory.hotkeysManager.GetHotkeys())
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
                        HotkeysList.Insert(index, new HotkeyViewModel(hotkey));
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
    }
}
