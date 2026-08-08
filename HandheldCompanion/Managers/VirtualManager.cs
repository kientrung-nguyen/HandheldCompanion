using HandheldCompanion.Controllers;
using HandheldCompanion.Helpers;
using HandheldCompanion.Inputs;
using HandheldCompanion.Shared;
using HandheldCompanion.Targets;
using HandheldCompanion.Utils;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using SharpDX.XInput;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using static HandheldCompanion.Managers.ControllerManager;

namespace HandheldCompanion.Managers
{
    public static class VirtualManager
    {
        #region imports
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr LoadLibrary(string lpFileName);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool FreeLibrary(IntPtr hModule);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);
        #endregion

        // controllers vars
        public static ViGEmClient? vClient;
        public static ViGEmTarget? vTarget;

        // dll vars
        private const string dllName = "vigemclient.dll";
        private static IntPtr Module = IntPtr.Zero;

        // drivers vars
        private const string driverName = "ViGEmBus";

        // settings vars
        public static HIDmode HIDmode = HIDmode.NoController;
        private static HIDmode defaultHIDmode = HIDmode.NoController;
        public static HIDstatus HIDstatus = HIDstatus.Disconnected;

        private static readonly SemaphoreSlim controllerLock = new SemaphoreSlim(1, 1);
        private static List<IVirtualGamepad> temporaryControllers = new();

        public static ushort VendorId = 0x45E;
        public static ushort ProductId = 0x28E;

        private static readonly object temporaryControllerLock = new();

        // Sleep state tracking: when the system is in sleep mode, only report meaningful input changes
        // to prevent the virtual controller from waking the device with constant gyro reports.
        private static bool isSystemSleeping = false;

        // Xbox stick noise filter threshold: ignore axis value changes smaller than this
        private const short AxisNoiseThreshold = 150;

        public static bool IsInitialized;

        public static event ControllerSelectedEventHandler? ControllerSelected;
        public delegate void ControllerSelectedEventHandler(HIDmode mode);

        public static event InitializedEventHandler? Initialized;
        public delegate void InitializedEventHandler();

        public static event VibrateEventHandler? Vibrated;
        public delegate void VibrateEventHandler(byte LargeMotor, byte SmallMotor);

        public static event ConnectStatusChangedEventHandler? StatusChanged;
        public delegate void ConnectStatusChangedEventHandler(VirtualManagerStatus status, int attempt, int maxAttempts);

        public static event MasterIntervalOverrideChangedEventHandler? MasterIntervalOverrideChanged;
        public delegate void MasterIntervalOverrideChangedEventHandler(int? overrideHz);

        static VirtualManager()
        {
            // verifying ViGEm is installed
            try
            {
                vClient = new ViGEmClient();
                Module = GetModuleHandle(dllName);
            }
            catch (Exception)
            {
                LogManager.LogCritical("ViGEm is missing. Please get it from: {0}", "https://github.com/ViGEm/ViGEmBus/releases");
                MessageBox.Show("Unable to start Handheld Companion, the ViGEm application is missing.\n\nPlease get it from: https://github.com/ViGEm/ViGEmBus/releases", "Error");
                throw new InvalidOperationException();
            }

            // prepare vJoy SDL mapping
            VJoyTarget.WriteSDLGameControllerMapping();
        }

        public static int? GetMasterIntervalOverrideHz()
        {
            return vTarget?.MasterIntervalOverrideHz;
        }

        private static void NotifyMasterIntervalOverrideChanged()
        {
            MasterIntervalOverrideChanged?.Invoke(GetMasterIntervalOverrideHz());
        }

        public static async void Start()
        {
            if (IsInitialized)
                return;

            // wait until drivers are fully loaded
            using (ServiceController sc = new ServiceController(driverName))
                while (sc.Status != ServiceControllerStatus.Running)
                    await Task.Delay(250).ConfigureAwait(false); // Avoid blocking the synchronization context

            // manage events
            ManagerFactory.profileManager.Applied += ProfileManager_Applied;
            ManagerFactory.profileManager.Discarded += ProfileManager_Discarded;

            // raise events
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

            /*
            if (ManagerFactory.profileManager.IsInitialized)
            {
                ProfileManager_Applied(ManagerFactory.profileManager.GetCurrent(), UpdateSource.Background);
            }
            */

            IsInitialized = true;
            Initialized?.Invoke();

            LogManager.LogInformation("{0} has started", "VirtualManager");
        }

        private static void SettingsManager_Initialized()
        {
            QuerySettings();
        }

        private static void QuerySettings()
        {
            // manage events
            ManagerFactory.settingsManager.SettingValueChanged += SettingsManager_SettingValueChanged;

            // raise events
            // Retrieve the default HID mode from settings
            HIDmode selectedHIDMode = (HIDmode)ManagerFactory.settingsManager.GetInt("HIDmode");

            // Check if ProfileManager is initialized and a valid profile is available
            if (ManagerFactory.profileManager.IsReady)
            {
                Profile currentProfile = ManagerFactory.profileManager.GetCurrent();
                if (currentProfile != null && currentProfile.HID != HIDmode.NotSelected)
                    selectedHIDMode = currentProfile.HID;
            }

            // load a few variables
            HIDstatus = (HIDstatus)ManagerFactory.settingsManager.GetInt("HIDstatus");

            SettingsManager_SettingValueChanged("DSUport", ManagerFactory.settingsManager.GetInt("DSUport"), false, true);
            SettingsManager_SettingValueChanged("DSUEnabled", ManagerFactory.settingsManager.GetString("DSUEnabled"), false, true);
            SettingsManager_SettingValueChanged("HIDmode", selectedHIDMode, false, true);
            SettingsManager_SettingValueChanged("HIDstatus", HIDstatus, false, true);

            SetControllerModeCore(defaultHIDmode);
        }

        public static void Stop()
        {
            if (!IsInitialized)
                return;

            Suspend(true);

            // manage events
            ManagerFactory.settingsManager.SettingValueChanged -= SettingsManager_SettingValueChanged;
            ManagerFactory.settingsManager.Initialized -= SettingsManager_Initialized;
            ManagerFactory.profileManager.Applied -= ProfileManager_Applied;
            ManagerFactory.profileManager.Discarded -= ProfileManager_Discarded;

            IsInitialized = false;

            LogManager.LogInformation("{0} has stopped", "VirtualManager");
        }

        public static void Resume(bool OS)
        {
            if (!controllerLock.Wait(3000))
                return;

            try
            {
                if (Module == IntPtr.Zero)
                    Module = LoadLibrary(dllName);

                // Create a new ViGEm client if needed
                if (vClient is null)
                    vClient = new ViGEmClient();

                if (OS)
                {
                    // Update DSU status
                    SetDSUStatus(ManagerFactory.settingsManager.GetBoolean("DSUEnabled"));
                }
            }
            catch { }
            finally
            {
                controllerLock.Release();
            }

            // Run on a thread-pool thread so callers on the UI thread (e.g. resume from sleep)
            // are never blocked by the ViGEm connect retry back-off delays.
            _ = Task.Run(() => SetControllerMode(HIDmode));
        }

        public static void Suspend(bool OS)
        {
            // Disconnect the controller first
            SetControllerMode(HIDmode.NoController);

            if (!controllerLock.Wait(3000))
                return;

            try
            {
                // Dispose of the ViGEm client and unload the module
                if (vClient is not null)
                {
                    vClient.Dispose();
                    vClient = null;

                    if (Module != IntPtr.Zero)
                    {
                        FreeLibrary(Module);
                        Module = IntPtr.Zero;
                    }
                }

                if (OS)
                {
                    // Halt DSU
                    SetDSUStatus(false);
                }
            }
            catch { }
            finally
            {
                controllerLock.Release();
            }
        }

        private static void SettingsManager_SettingValueChanged(string name, object? value, bool temporary, bool initializing)
        {
            switch (name)
            {
                case "HIDmode":
                    {
                        // update variable
                        defaultHIDmode = (HIDmode)Convert.ToInt32(value);
                        SetControllerMode(defaultHIDmode);
                    }
                    break;
                case "HIDstatus":
                    {
                        // skip on cold boot, retrieved by Start() function and called by SetControllerMode()
                        if (ManagerFactory.settingsManager.IsReady)
                            SetControllerStatus((HIDstatus)Convert.ToInt32(value));
                    }
                    break;
                case "DSUEnabled":
                    SetDSUStatus(Convert.ToBoolean(value));
                    break;
                case "DSUport":
                    if (DSUServer.IsInitialized)
                        DSUServer.Restart(Convert.ToInt32(value));
                    else
                        DSUServer.serverPort = Convert.ToInt32(value);
                    break;
            }
        }

        private static async void ProfileManager_Applied(Profile profile, UpdateSource source)
        {
            // SetControllerMode takes care of ignoring identical mode switching
            if (HIDmode == profile.HID || (profile.HID == HIDmode.NotSelected && HIDmode == defaultHIDmode))
                return;

            while (ControllerManager.managerStatus == ControllerManagerStatus.Busy)
                await Task.Delay(1000).ConfigureAwait(false); // Avoid blocking the synchronization context

            switch (profile.HID)
            {
                case HIDmode.NoController:
                case HIDmode.Xbox360Controller:
                case HIDmode.DualShock4Controller:
                case HIDmode.DualSenseController:
                case HIDmode.SteamDeckController:
                case HIDmode.SwitchProController:
                    SetControllerMode(profile.HID);
                    break;

                case HIDmode.NotSelected:
                    SetControllerMode(defaultHIDmode);
                    break;
            }
        }

        private static async void ProfileManager_Discarded(Profile profile, bool swapped, Profile nextProfile)
        {
            // don't bother discarding settings, new one will be enforce shortly
            if (swapped)
                return;

            while (ControllerManager.managerStatus == ControllerManagerStatus.Busy)
                await Task.Delay(1000).ConfigureAwait(false); // Avoid blocking the synchronization context

            // restore default HID mode
            if (profile.HID != HIDmode.NotSelected)
                SetControllerMode(defaultHIDmode);
        }

        public static int CreateTemporaryControllers(int maxCount = int.MaxValue)
        {
            if (vClient is null)
                return 0;

            DisposeTemporaryControllers();
            // count available XInput slots
            int availableSlots = 0;
            for (int i = 0; i < XInputController.MaxControllers; i++)
            {
                Controller controller = new Controller((UserIndex)i);
                if (!controller.IsConnected) availableSlots++;
            }

            // cap to the requested maximum
            int toCreate = Math.Min(availableSlots, maxCount);
            int created = 0;

            for (int i = 0; i < toCreate; i++)
            {
                try
                {
                    CreateTemporaryControllerTarget();
                    created++;
                    //var controller = vClient.CreateXbox360Controller(VendorId, ProductId);
                    //controller.Connect();

                    //lock (temporaryControllerLock)
                    //{
                    //    temporaryControllers.Add(controller);
                    //    created++;
                    //}

                    Thread.Sleep(500);
                }
                catch { /* swallow */ }
            }

            lock (temporaryControllerLock) { return temporaryControllers.Count; }
        }

        public static void DisposeTemporaryControllers()
        {
            if (vClient is null)
                return;

            IVirtualGamepad[] snapshot;

            lock (temporaryControllerLock)
            {
                if (temporaryControllers.Count == 0)
                    return;

                snapshot = [.. temporaryControllers];
                temporaryControllers.Clear();
            }

            foreach (var controller in snapshot)
            {
                try
                {
                    controller.Disconnect();
                    Thread.Sleep(500);
                }
                catch { /* swallow */ }
            }
        }

        private static IXbox360Controller CreateTemporaryControllerTarget()
        {
            var controller = vClient.CreateXbox360Controller(VendorId, ProductId);
            controller.Connect();

            lock (temporaryControllerLock)
                temporaryControllers.Add(controller);
            return controller;
        }

        private static void SetDSUStatus(bool started)
        {
            if (started)
                DSUServer.Start();
            else
                DSUServer.Stop();
        }


        private static bool IsViiperBackedMode(HIDmode mode)
        {
            return mode == HIDmode.Xbox360Controller
                || mode == HIDmode.DualShock4Controller
                || mode == HIDmode.DualSenseController
                || mode == HIDmode.SteamDeckController
                || mode == HIDmode.SteamController
                || mode == HIDmode.SwitchProController
                || mode == HIDmode.Free;
        }

        private static bool CanUseControllerMode(HIDmode mode)
        {
            if (!IsViiperBackedMode(mode))
                return true;

            if (!ManagerFactory.settingsManager.GetBoolean("VIIPEREnabled"))
            {
                LogManager.LogInformation("Skipping {0}: VIIPER server is disabled", mode);
                return false;
            }

            //if (!ViiperServerManager.IsRunning)
            //{
            //    StatusChanged?.Invoke(VirtualManagerStatus.Failed, 1, 1);
            //    LogManager.LogWarning("Skipping {0}: VIIPER server is not running", mode);
            //    return false;
            //}

            return true;
        }

        public static void SetControllerMode(HIDmode mode)
        {
            if (!controllerLock.Wait(3000))
                return;

            try
            {
                SetControllerModeCore(mode);
            }
            catch { }
            finally
            {
                controllerLock.Release();
            }

            // Update controller status synchronously
            SetControllerStatus(HIDstatus);
        }


        public static void SetControllerStatus(HIDstatus status)
        {
            if (!controllerLock.Wait(3000))
                return;

            try
            {
                SetControllerStatusCore(status);
            }
            catch { }
            finally
            {
                controllerLock.Release();
            }
        }

        private static void SetControllerModeCore(HIDmode mode)
        {
            // If the requested mode is already active, do nothing
            if (HIDmode == mode)
            {
                if (HIDstatus == HIDstatus.Connected && (vTarget is not null && vTarget.IsConnected))
                    return;
                else if (HIDstatus == HIDstatus.Disconnected && (vTarget is null || !vTarget.IsConnected))
                    return;
            }

            // Disconnect and dispose the current virtual controller if it exists
            if (vTarget is not null)
            {
                vTarget.Connected -= OnTargetConnected;
                vTarget.Disconnected -= OnTargetDisconnected;
                vTarget.Vibrated -= OnTargetVibrated;
                vTarget.StatusChanged -= OnTargetConnectStatusChanged;
                vTarget.Disconnect();
                vTarget.Dispose();
                vTarget = null;
                NotifyMasterIntervalOverrideChanged();
            }

            // Sanity-check: if the ViGEm client isn't available, abort
            if (vClient is null)
                return;

            // Create a new target based on the requested mode
            switch (mode)
            {
                case HIDmode.NoController:
                    {
                        HIDmode = mode;
                        ControllerSelected?.Invoke(mode);
                        NotifyMasterIntervalOverrideChanged();
                        SetControllerStatusCore(HIDstatus);
                    }
                    return;
                case HIDmode.DualShock4Controller:
                    vTarget = new DualShock4Target();
                    break;

                case HIDmode.Xbox360Controller:
                    vTarget = new Xbox360Target(VendorId, ProductId);
                    break;

                case HIDmode.DualSenseController:
                    uint deviceId = VJoyTarget.FindAvailableDeviceId();
                    vTarget = new VJoyTarget(deviceId);
                    break;
            }

            // If target creation failed, log an error (unless it's the NoController case)
            if (vTarget is null)
            {
                if (mode != HIDmode.NoController)
                    LogManager.LogError("Failed to initialise virtual controller with HIDmode: {0}", mode);
                NotifyMasterIntervalOverrideChanged();
                return;
            }

            // Subscribe to target events
            vTarget.Connected += OnTargetConnected;
            vTarget.Disconnected += OnTargetDisconnected;
            vTarget.Vibrated += OnTargetVibrated;
            vTarget.StatusChanged += OnTargetConnectStatusChanged;

            // Update the current mode
            HIDmode = mode;

            // Notify subscribers about the controller change
            ControllerSelected?.Invoke(mode);
            NotifyMasterIntervalOverrideChanged();

            SetControllerStatusCore(HIDstatus);
        }

        private static void SetControllerStatusCore(HIDstatus status)
        {
            if (vTarget is null)
            {
                if (status == HIDstatus.Disconnected)
                    HIDstatus = status;

                return;
            }

            bool success = false;
            switch (status)
            {
                case HIDstatus.Connected:
                    if (!vTarget.IsConnected)
                        success = vTarget.Connect();
                    break;
                case HIDstatus.Disconnected:
                    if (vTarget.IsConnected)
                        success = vTarget.Disconnect();
                    break;
            }

            // Only update the internal status if the operation was successful
            if (success)
                HIDstatus = status;
        }


        private static void OnTargetConnectStatusChanged(ViGEmTarget target, VirtualManagerStatus status, int attempt, int maxAttempts)
        {
            StatusChanged?.Invoke(status, attempt, maxAttempts);
        }

        private static void OnTargetConnected(ViGEmTarget target)
        {
            ToastManager.SendToast($"{target}", "is now connected"); //, $"controller_{(uint)target.HID}_1", true);
        }

        private static void OnTargetDisconnected(ViGEmTarget target)
        {
            ToastManager.SendToast($"{target}", "is now disconnected"); //, $"controller_{(uint)target.HID}_0", true);
        }

        private static void OnTargetVibrated(byte LargeMotor, byte SmallMotor)
        {
            Vibrated?.Invoke(LargeMotor, SmallMotor);
        }

        /// <summary>
        /// Sets the system sleep state. When sleeping, UpdateInputs will only update the virtual
        /// controller if button state has changed, preventing gyro from waking the device.
        /// </summary>
        public static void SetSystemSleepState(bool sleeping)
        {
            isSystemSleeping = sleeping;
        }

        /// <summary>
        /// Compares two axis states with a noise filter threshold for Xbox mode.
        /// Returns true if axis values differ by more than the noise threshold.
        /// </summary>
        private static bool AxisStateHasSignificantChange(AxisState? previous, AxisState current)
        {
            if (previous is null)
                return !current.IsEmpty();

            foreach (AxisFlags axis in AxisState.TrueAxis)
            {
                short prevValue = previous[axis];
                short currValue = current[axis];

                if (Math.Abs(currValue - prevValue) > AxisNoiseThreshold)
                    return true;
            }

            return false;
        }

        public static void UpdateInputs(ControllerState controllerState, GamepadMotion gamepadMotion)
        {
            // Skip sending inputs to virtual controller when listening for hotkey inputs
            if (InputsManager.IsListening)
                return;

            if (isSystemSleeping)
                return;

            vTarget?.UpdateInputs(controllerState, gamepadMotion);
        }
    }
}