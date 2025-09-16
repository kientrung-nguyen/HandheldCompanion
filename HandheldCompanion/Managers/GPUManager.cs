using HandheldCompanion.ADLX;
using HandheldCompanion.Controls;
using HandheldCompanion.GraphicsProcessingUnit;
using HandheldCompanion.IGCL;
using HandheldCompanion.Misc;
using HandheldCompanion.Shared;
using SharpDX.Direct3D9;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using WindowsDisplayAPI;

namespace HandheldCompanion.Managers
{
    public static class GPUManager
    {
        #region events
        public static event InitializedEventHandler? Initialized;
        public delegate void InitializedEventHandler(bool HasIGCL, bool HasADLX);

        public static event HookedEventHandler? Hooked;
        public delegate void HookedEventHandler(GPU GPU);

        public static event UnhookedEventHandler? Unhooked;
        public delegate void UnhookedEventHandler(GPU GPU);
        #endregion

        public static bool IsInitialized = false;
        public static bool IsLoaded_IGCL = false;
        public static bool IsLoaded_ADLX = false;

        private static GPU currentGPU = null;
        private static ConcurrentDictionary<AdapterInformation, GPU> DisplayGPU = new();

        public static async Task Start()
        {
            if (IsInitialized)
                return;

            if (!IsLoaded_IGCL && GPU.HasIntelGPU())
            {
                // try to initialized IGCL
                IsLoaded_IGCL = IGCLBackend.Initialize();

                if (IsLoaded_IGCL)
                    LogManager.LogInformation("IGCL was successfully initialized", "GPUManager");
                else
                    LogManager.LogError("Failed to initialize IGCL", "GPUManager");
            }

            if (!IsLoaded_ADLX && GPU.HasAMDGPU())
            {
                // try to initialized ADLX
                IsLoaded_ADLX = ADLXBackend.SafeIntializeAdlx();

                if (IsLoaded_ADLX)
                    LogManager.LogInformation("ADLX {0} was successfully initialized", ADLXBackend.GetVersion(), "GPUManager");
                else
                    LogManager.LogError("Failed to initialize ADLX", "GPUManager");
            }

            // todo: check if usefull on resume
            // it could be DeviceManager_DisplayAdapterArrived is called already, making this redundant
            currentGPU?.Start();

            // manage events
            ProfileManager.Applied += ProfileManager_Applied;
            ProfileManager.Discarded += ProfileManager_Discarded;
            ProfileManager.Updated += ProfileManager_Updated;
            DeviceManager.DisplayAdapterArrived += DeviceManager_DisplayAdapterArrived;
            DeviceManager.DisplayAdapterRemoved += DeviceManager_DisplayAdapterRemoved;
            MultimediaManager.PrimaryScreenChanged += MultimediaManager_PrimaryScreenChanged;
            MultimediaManager.Initialized += MultimediaManager_Initialized;

            // raise events
            if (ProfileManager.IsInitialized)
            {
                ProfileManager_Applied(ProfileManager.GetCurrent(), UpdateSource.Background);
            }

            if (DeviceManager.IsInitialized)
            {
                foreach (AdapterInformation displayAdapter in DeviceManager.displayAdapters.Values)
                    DeviceManager_DisplayAdapterArrived(displayAdapter);
            }

            if (MultimediaManager.IsInitialized && ScreenControl.PrimaryDisplay is not null)
            {
                MultimediaManager_PrimaryScreenChanged(ScreenControl.PrimaryDisplay);
            }

            IsInitialized = true;
            Initialized?.Invoke(IsLoaded_IGCL, IsLoaded_ADLX);

            LogManager.LogInformation("{0} has started", "GPUManager");
            return;
        }

        public static void Stop()
        {
            if (!IsInitialized)
                return;

            // manage events
            ProfileManager.Applied -= ProfileManager_Applied;
            ProfileManager.Discarded -= ProfileManager_Discarded;
            ProfileManager.Updated -= ProfileManager_Updated;
            DeviceManager.DisplayAdapterArrived -= DeviceManager_DisplayAdapterArrived;
            DeviceManager.DisplayAdapterRemoved -= DeviceManager_DisplayAdapterRemoved;
            MultimediaManager.PrimaryScreenChanged -= MultimediaManager_PrimaryScreenChanged;
            MultimediaManager.Initialized -= MultimediaManager_Initialized;

            foreach (GPU gpu in DisplayGPU.Values)
                gpu.Stop();

            lock (GPU.functionLock)
            {
                if (IsLoaded_IGCL)
                {
                    IGCLBackend.Terminate();
                    IsLoaded_IGCL = false;
                }

                if (IsLoaded_ADLX)
                {
                    ADLXBackend.CloseAdlx();
                    IsLoaded_ADLX = false;
                }
            }

            IsInitialized = false;

            LogManager.LogInformation("{0} has stopped", "GPUManager");
        }

        private static void GPUConnect(GPU GPU)
        {
            // update current GPU
            currentGPU = GPU;

            GPU.ImageSharpeningChanged += CurrentGPU_ImageSharpeningChanged;
            GPU.GPUScalingChanged += CurrentGPU_GPUScalingChanged;
            GPU.IntegerScalingChanged += CurrentGPU_IntegerScalingChanged;

            if (GPU is AmdGpu amdGpu)
            {
                amdGpu.RSRStateChanged += CurrentGPU_RSRStateChanged;
                amdGpu.AFMFStateChanged += CurrentGPU_AFMFStateChanged;
            }
            else if (GPU is IntelGpu)
            {
                // do something
            }

            if (GPU.IsInitialized)
            {
                GPU.Start();
                Hooked?.Invoke(GPU);

                LogManager.LogInformation("Hooked DisplayAdapter: {0}", GPU.ToString());
            }
        }

        private static void GPUDisconnect(GPU gpu)
        {
            if (currentGPU == gpu)
                Unhooked?.Invoke(gpu);

            gpu.ImageSharpeningChanged -= CurrentGPU_ImageSharpeningChanged;
            gpu.GPUScalingChanged -= CurrentGPU_GPUScalingChanged;
            gpu.IntegerScalingChanged -= CurrentGPU_IntegerScalingChanged;

            if (gpu is AmdGpu amdGpu)
            {
                amdGpu.RSRStateChanged -= CurrentGPU_RSRStateChanged;
                amdGpu.AFMFStateChanged -= CurrentGPU_AFMFStateChanged;
            }
            else if (gpu is IntelGpu)
            {
                // do something
            }

            gpu.Stop();
        }

        private static void MultimediaManager_PrimaryScreenChanged(Display screen)
        {
            var key = DisplayGPU.Keys.FirstOrDefault(gpu => gpu.Details.DeviceName == screen.DisplayScreen.ToPathDisplaySource().DisplayName);
            if (key is not null && DisplayGPU.TryGetValue(key, out var gpu))
            {
                LogManager.LogError("Retrieved DisplayAdapter: {0} for screen: {1}", gpu.ToString(), screen.DisplayScreen.ToPathDisplaySource().DisplayName);

                // a new GPU was connected, disconnect from current gpu
                if (currentGPU is not null && currentGPU != gpu)
                    GPUDisconnect(currentGPU);

                // connect to new gpu
                GPUConnect(gpu);
            }
            else
            {
                LogManager.LogError("Failed to retrieve DisplayAdapter for screen: {0}", screen.DisplayScreen.ToPathDisplaySource().DisplayName);
            }
        }

        private static void MultimediaManager_Initialized()
        {
            if (ScreenControl.PrimaryDisplay is not null)
                MultimediaManager_PrimaryScreenChanged(ScreenControl.PrimaryDisplay);
        }

        private static void DeviceManager_DisplayAdapterArrived(AdapterInformation adapterInformation)
        {
            // GPU is already part of the dictionary
            if (DisplayGPU.ContainsKey(adapterInformation))
                return;
            GPU? newGPU = null;

            if ((adapterInformation.Details.Description.Contains("Advanced Micro Devices") || adapterInformation.Details.Description.Contains("AMD")) && IsLoaded_ADLX)
            {
                newGPU = new AmdGpu(adapterInformation);
            }
            else if (adapterInformation.Details.Description.Contains("Intel") && IsLoaded_IGCL)
            {
                newGPU = new IntelGpu(adapterInformation);
            }

            if (newGPU is null)
            {
                LogManager.LogError("Unsupported DisplayAdapter: {0}, VendorID:{1}, DeviceId:{2}", adapterInformation.Details.Description, adapterInformation.Details.VendorId, adapterInformation.Details.DeviceId);
                return;
            }

            if (!newGPU.IsInitialized)
            {
                LogManager.LogError("Failed to initialize DisplayAdapter: {0}, VendorID:{1}, DeviceId:{2}", adapterInformation.Details.Description, adapterInformation.Details.VendorId, adapterInformation.Details.DeviceId);
                return;
            }

            LogManager.LogInformation("Detected DisplayAdapter: {0}, VendorID:{1}, DeviceId:{2}", adapterInformation.Details.Description, adapterInformation.Details.VendorId, adapterInformation.Details.DeviceId);

            // add to dictionary
            DisplayGPU.TryAdd(adapterInformation, newGPU);
        }

        private static void DeviceManager_DisplayAdapterRemoved(AdapterInformation adapterInformation)
        {
            if (DisplayGPU.TryRemove(adapterInformation, out var gpu))
            {
                GPUDisconnect(gpu);
                gpu.Dispose();
            }
        }

        public static GPU GetCurrent()
        {
            return currentGPU;
        }

        private static void CurrentGPU_RSRStateChanged(bool Supported, bool Enabled, int Sharpness)
        {
            if (!IsInitialized)
                return;

            // todo: use ProfileMager events
            Profile profile = ProfileManager.GetCurrent();

            if (currentGPU is AmdGpu amdGpu)
            {
                if (Enabled != profile.RSREnabled)
                    profile.RSREnabled = Enabled;
                if (Sharpness != profile.RSRSharpness)
                    profile.RSRSharpness = Sharpness;
                ProfileManager.UpdateOrCreateProfile(profile);
            }

        }

        private static void CurrentGPU_AFMFStateChanged(bool Supported, bool Enabled)
        {
            if (!IsInitialized)
                return;

            // todo: use ProfileMager events
            Profile profile = ProfileManager.GetCurrent();

            if (profile.AFMFEnabled)
                profile.AFMFEnabled = Enabled;
            ProfileManager.UpdateOrCreateProfile(profile);
        }


        private static void CurrentGPU_IntegerScalingChanged(bool Supported, bool Enabled)
        {
            if (!IsInitialized)
                return;

            // todo: use ProfileMager events
            Profile profile = ProfileManager.GetCurrent();

            if (Enabled != profile.IntegerScalingEnabled)
                profile.IntegerScalingEnabled = Enabled;

            ProfileManager.UpdateOrCreateProfile(profile);
        }

        private static void CurrentGPU_GPUScalingChanged(bool Supported, bool Enabled, int Mode)
        {
            if (!IsInitialized)
                return;

            // todo: use ProfileMager events
            Profile profile = ProfileManager.GetCurrent();

            if (Enabled != profile.GPUScaling)
                profile.GPUScaling = Enabled;
            if (Mode != profile.ScalingMode)
                profile.ScalingMode = Mode;
            ProfileManager.UpdateOrCreateProfile(profile);
        }

        private static void CurrentGPU_ImageSharpeningChanged(bool Enabled, int Sharpness)
        {
            if (!IsInitialized)
                return;

            // todo: use ProfileMager events
            Profile profile = ProfileManager.GetCurrent();

            if (Enabled != profile.RISEnabled)
                profile.RISEnabled = Enabled;
            if (Sharpness != profile.RISSharpness)
                profile.RISSharpness = Sharpness;
            ProfileManager.UpdateOrCreateProfile(profile);
        }

        private static void ProfileManager_Applied(Profile profile, UpdateSource source)
        {
            if (!IsInitialized || currentGPU is null)
                return;

            try
            {
                // apply profile GPU Scaling
                // apply profile scaling mode
                if (profile.GPUScaling)
                {
                    if (!currentGPU.GetGPUScaling())
                        currentGPU.SetGPUScaling(true);

                    if (currentGPU.GetScalingMode() != profile.ScalingMode)
                        currentGPU.SetScalingMode(profile.ScalingMode);
                }
                else if (currentGPU.GetGPUScaling())
                {
                    currentGPU.SetGPUScaling(false);
                }

                // apply profile RSR / AFMF
                if (currentGPU is AmdGpu amdGpu)
                {
                    if (profile.RSREnabled)
                    {
                        if (!amdGpu.GetRSR())
                            amdGpu.SetRSR(true);

                        if (amdGpu.GetRSRSharpness() != profile.RSRSharpness)
                            amdGpu.SetRSRSharpness(profile.RSRSharpness);
                    }
                    else if (amdGpu.GetRSR())
                    {
                        amdGpu.SetRSR(false);
                    }

                    if (profile.AFMFEnabled)
                    {
                        if (!amdGpu.GetAFMF())
                            amdGpu.SetAFMF(true);

                        if (!amdGpu.GetAntiLag())
                            amdGpu.SetAntiLag(true);
                    }
                    else if (amdGpu.GetAFMF())
                    {
                        amdGpu.SetAFMF(false);
                    }
                }

                // apply profile Integer Scaling
                if (profile.IntegerScalingEnabled)
                {
                    if (!currentGPU.GetIntegerScaling())
                        currentGPU.SetIntegerScaling(true, profile.IntegerScalingType);
                }
                else if (currentGPU.GetIntegerScaling())
                {
                    currentGPU.SetIntegerScaling(false, 0);
                }

                // apply profile image sharpening
                if (profile.RISEnabled)
                {
                    if (!currentGPU.GetImageSharpening())
                        currentGPU.SetImageSharpening(profile.RISEnabled);

                    if (currentGPU.GetImageSharpeningSharpness() != profile.RISSharpness)
                        currentGPU.SetImageSharpeningSharpness(profile.RISSharpness);
                }
                else if (currentGPU.GetImageSharpening())
                {
                    currentGPU.SetImageSharpening(false);
                }
            }
            catch { }
        }

        private static void ProfileManager_Discarded(Profile profile, bool swapped)
        {
            if (!IsInitialized || currentGPU is null)
                return;

            // don't bother discarding settings, new one will be enforce shortly
            if (swapped)
                return;

            try
            {
                /*
                // restore default GPU Scaling
                if (profile.GPUScaling && currentGPU.GetGPUScaling())
                    currentGPU.SetGPUScaling(false);
                */

                // restore default RSR
                if (currentGPU is AmdGpu amdGpu)
                {
                    if (profile.RSREnabled && amdGpu.GetRSR())
                        amdGpu.SetRSR(false);
                }

                // restore default integer scaling
                if (profile.IntegerScalingEnabled && currentGPU.GetIntegerScaling())
                    currentGPU.SetIntegerScaling(false, 0);

                // restore default image sharpening
                if (profile.RISEnabled && currentGPU.GetImageSharpening())
                    currentGPU.SetImageSharpening(false);
            }
            catch { }
        }

        // todo: moveme
        private static void ProfileManager_Updated(Profile profile, UpdateSource source, bool isCurrent)
        {
            ProcessEx.SetAppCompatFlag(profile.Path, ProcessEx.DisabledMaximizedWindowedValue, !profile.FullScreenOptimization);
            ProcessEx.SetAppCompatFlag(profile.Path, ProcessEx.HighDPIAwareValue, !profile.HighDPIAware);
        }
    }
}
