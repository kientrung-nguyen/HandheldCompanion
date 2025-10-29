using HandheldCompanion.Managers;
using HandheldCompanion.Misc;
using HandheldCompanion.Shared;
using HandheldCompanion.Utils;
using RTSSSharedMemoryNET;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using static HandheldCompanion.Misc.ProcessEx;
using Path = System.IO.Path;
using Timer = System.Timers.Timer;

namespace HandheldCompanion.Platforms.Misc;

public class RTSS : IPlatform
{
    private const uint WM_APP = 0x8000;
    private const uint WM_RTSS_UPDATESETTINGS = WM_APP + 100;
    private const uint WM_RTSS_SHOW_PROPERTIES = WM_APP + 102;

    private const uint RTSSHOOKSFLAG_OSD_VISIBLE = 1;
    private const uint RTSSHOOKSFLAG_LIMITER_DISABLED = 4;
    private const string GLOBAL_PROFILE = "";

    private const int HOOK_RETRY_COUNT = 60;
    private const int HOOK_RETRY_DELAY_MS = 1000;
    private const int HOOK_TASK_TIMEOUT_MS = 1000;

    private int hookedProcessId = 0;
    private int targetProcessId = 0;
    private uint lastOsdFrameId = 0;

    private bool profileLoaded;
    private AppEntry? appEntry;

    private int RequestedFramerate;

    public RTSS()
    {
        Name = "RTSS";
        ExecutableName = "RTSS.exe";

        ExpectedVersion = new Version(7, 3, 4);
        Url = "https://www.guru3d.com/files-details/rtss-rivatuner-statistics-server-download.html";

        // store specific modules
        Modules =
        [
            "RTSSHooks64.dll"
        ];

        // check if platform is installed
        InstallPath = RegistryUtils.GetString(@"SOFTWARE\WOW6432Node\Unwinder\RTSS", "InstallDir");
        if (Path.Exists(InstallPath))
        {
            // update paths
            SettingsPath = Path.Combine(InstallPath, @"Profiles\Global");
            ExecutablePath = Path.Combine(InstallPath, ExecutableName);

            // check executable
            if (File.Exists(ExecutablePath))
            {
                if (!HasModules)
                {
                    LogManager.LogWarning(
                        "Rivatuner Statistics Server RTSSHooks64.dll is missing. Please get it from: {0}", Url);
                    return;
                }

                // check executable version
                var versionInfo = FileVersionInfo.GetVersionInfo(ExecutablePath);
                var CurrentVersion = new Version(versionInfo.ProductMajorPart, versionInfo.ProductMinorPart,
                    versionInfo.ProductBuildPart);

                if (CurrentVersion < ExpectedVersion)
                {
                    LogManager.LogWarning("Rivatuner Statistics Server is outdated. Please get it from: {0}", Url);
                    return;
                }

                IsInstalled = true;
            }
        }

        if (!IsInstalled)
        {
            LogManager.LogWarning("Rivatuner Statistics Server is missing. Please get it from: {0}",
                "https://www.guru3d.com/files-details/rtss-rivatuner-statistics-server-download.html");
            return;
        }

        // our main watchdog to (re)apply requested settings
        PlatformWatchdog = new Timer(2000) { Enabled = false, AutoReset = false };
        PlatformWatchdog.Elapsed += (sender, e) => PlatformWatchdogElapsed();
    }

    public override bool Start()
    {
        // start RTSS if not running
        if (!IsRunning)
            StartProcess();
        else
            // hook into current process
            Process.Exited += Process_Exited;

        // manage events
        ManagerFactory.processManager.ForegroundChanged += ProcessManager_ForegroundChanged;
        ManagerFactory.processManager.ProcessStopped += ProcessManager_ProcessStopped;
        ManagerFactory.profileManager.Applied += ProfileManager_Applied;

        // raise events
        switch (ManagerFactory.processManager.Status)
        {
            default:
            case ManagerStatus.Initializing:
                ManagerFactory.processManager.Initialized += ProcessManager_Initialized;
                break;
            case ManagerStatus.Initialized:
                QueryForeground();
                break;
        }

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

        return base.Start();
    }

    private void QueryForeground()
    {
        ProcessEx processEx = ProcessManager.GetCurrent();
        if (processEx is null)
            return;

        ProcessFilter filter = ProcessManager.GetFilter(processEx.Executable, processEx.Path);
        ProcessManager_ForegroundChanged(processEx, null, filter);
    }

    private void ProcessManager_Initialized()
    {
        QueryForeground();
    }

    private void QueryProfile()
    {
        ProfileManager_Applied(ManagerFactory.profileManager.GetCurrent(), UpdateSource.Background);
    }

    private void ProfileManager_Initialized()
    {
        QueryProfile();
    }

    public override bool Stop(bool kill = false)
    {
        // manage events
        ManagerFactory.processManager.ForegroundChanged -= ProcessManager_ForegroundChanged;
        ManagerFactory.processManager.ProcessStopped -= ProcessManager_ProcessStopped;
        ManagerFactory.processManager.Initialized -= ProcessManager_Initialized;
        ManagerFactory.profileManager.Applied -= ProfileManager_Applied;
        ManagerFactory.profileManager.Initialized -= ProfileManager_Initialized;

        return base.Stop(kill);
    }

    public AppEntry? GetAppEntry()
    {
        return appEntry;
    }

    private void ProfileManager_Applied(Profile profile, UpdateSource source)
    {
        var frameLimit = 0;

        if (ScreenControl.PrimaryDisplay is not null)
        {
            var frameLimits = ScreenControl.GetFramelimits(ScreenControl.PrimaryDisplay);
            var fpsInLimits = frameLimits.FirstOrDefault(l => l == profile.FramerateValue);
            if (fpsInLimits is null)
            {
                var diffs = frameLimits
                    .Select(limit => (Math.Abs(profile.FramerateValue - limit ?? 0), limit))
                    .OrderBy(g => g.Item1).ThenBy(g => g.limit).ToList();

                var lowestDiff = diffs.First().Item1;
                var lowestDiffs = diffs.Where(d => d.Item1 == lowestDiff);

                fpsInLimits = lowestDiffs.Last().limit;
            }
            // Determine most appropriate frame rate limit based on screen frequency
            frameLimit = fpsInLimits ?? 0;
        }

        if (frameLimit > 0)
        {
            // Apply profile-defined framerate
            RequestFPS(frameLimit);
        }
        else if (frameLimit == 0 && RequestedFramerate > 0)
        {
            // Reset to 0 only when a cap was set previously and the current profile has no limit 
            // These conditions prevent 0 from being set on every profile change 
            RequestFPS(frameLimit, true);
        }
    }

    private void ProcessManager_ForegroundChanged(ProcessEx? processEx, ProcessEx? backgroundEx, ProcessFilter filter)
    {
        if (processEx is null || processEx.ProcessId == 0)
            return;

        switch (filter)
        {
            case ProcessFilter.Allowed:
                break;
            default:
                return;
        }

        // unhook previous process
        UnhookProcess(targetProcessId);

        // update foreground process id
        targetProcessId = processEx.ProcessId;

        // try to hook new process
        new Thread(() => TryHookProcess(targetProcessId)).Start();
    }

    private void TryHookProcess(int processId)
    {
        if (!IsRunning)
            return;

        var count = HOOK_RETRY_COUNT;
        do
        {
            try
            {
                var entries = OSD.GetAppEntries()
                    .Where(x => (x.Flags & AppFlags.MASK) != AppFlags.None && x.ProcessId == processId)
                    .ToList();

                appEntry = entries.FirstOrDefault();

            }
            catch (FileNotFoundException ex)
            {
                LogManager.LogDebug("RTSS shared memory file not found: {0}", ex.Message);
                return;
            }
            catch (Exception ex)
            {
                LogManager.LogError("Error accessing RTSS app entries: {0}", ex.Message);
            }

            // wait a bit
            Thread.Sleep(1000);
            count--;
        } while (appEntry is null && targetProcessId == processId && KeepAlive && count > 0);

        if (appEntry is null)
            return;

        // set HookedProcessId
        hookedProcessId = appEntry.ProcessId;
        lastOsdFrameId = appEntry.OSDFrameId;

        // raise event
        Hooked?.Invoke(appEntry);
    }

    private void UnhookProcess(int processId)
    {
        if (processId != hookedProcessId)
            return;

        // clear RTSS target app
        appEntry = null;

        // clear HookedProcessId
        hookedProcessId = 0;

        // raise event
        Unhooked?.Invoke(processId);
    }

    private void ProcessManager_ProcessStopped(ProcessEx processEx)
    {
        if (processEx is null || processEx.ProcessId == 0)
            return;

        UnhookProcess(processEx.ProcessId);
    }

    private void PlatformWatchdogElapsed()
    {
        lock (updateLock)
        {
            int requestedFramerate = RequestedFramerate;
            if (GetTargetFPS() != requestedFramerate)
                SetTargetFPS(requestedFramerate);

            try
            {
                // force "Show On-Screen Display" to On
                _ = SetFlags(~RTSSHOOKSFLAG_OSD_VISIBLE, RTSSHOOKSFLAG_OSD_VISIBLE);
            }
            catch (DllNotFoundException ex)
            {
                LogManager.LogError("RTSSHooks64.dll not found: {0}", ex.Message);
            }
            catch (Exception ex)
            {
                LogManager.LogError("Error setting RTSS flags: {0}", ex.Message);
            }

            // force "On-Screen Display Support" to On
            if (GetEnableOSD() != true)
                SetEnableOSD(true);
        }
    }

    private void Process_Exited(object? sender, EventArgs e)
    {
        if (KeepAlive)
            StartProcess();
    }

    public bool HasHook()
    {
        return hookedProcessId != 0;
    }

    public void RefreshAppEntry()
    {
        try
        {
            var entries = OSD.GetAppEntries()
                .Where(x => (x.Flags & AppFlags.MASK) != AppFlags.None)
                .ToList();

            var entry = entries.FirstOrDefault(a => a.ProcessId == (appEntry?.ProcessId ?? 0));

            if (entry != null)
            {
                var newFrameId = entry.OSDFrameId;
                lastOsdFrameId = newFrameId != lastOsdFrameId ? newFrameId : 0;
                appEntry = entry;
            }
            else
            {
                lastOsdFrameId = 0;
            }
        }
        catch (FileNotFoundException ex)
        {
            LogManager.LogDebug("RTSS shared memory not available: {0}", ex.Message);
        }
        catch (Exception ex)
        {
            LogManager.LogError("Error refreshing RTSS app entry: {0}", ex.Message);
        }
    }

    public double GetFramerate(bool refresh = false)
    {
        try
        {
            if (refresh)
                RefreshAppEntry();

            if (appEntry is null || lastOsdFrameId == 0)
                return 0.0d;

            return appEntry.StatFrameTimeBufFramerate / 10.0d;

        }
        catch (InvalidDataException ex)
        {
            LogManager.LogDebug("Invalid RTSS framerate data: {0}", ex.Message);
        }
        catch (FileNotFoundException ex)
        {
            LogManager.LogDebug("RTSS shared memory not available: {0}", ex.Message);
        }
        catch (Exception ex)
        {
            LogManager.LogError("Error reading RTSS framerate: {0}", ex.Message);
        }

        return 0.0d;
    }

    public double GetFrametime(bool refresh = false)
    {
        try
        {
            if (refresh)
                RefreshAppEntry();

            if (appEntry is null)
                return 0.0d;

            return (double)appEntry.InstantaneousFrameTime / 1000;
        }
        catch (InvalidDataException ex)
        {
            LogManager.LogDebug("Invalid RTSS frametime data: {0}", ex.Message);
        }
        catch (FileNotFoundException ex)
        {
            LogManager.LogDebug("RTSS shared memory not available: {0}", ex.Message);
        }
        catch (Exception ex)
        {
            LogManager.LogError("Error reading RTSS frametime: {0}", ex.Message);
        }

        return 0.0d;
    }

    public bool GetProfileProperty<T>(string propertyName, out T value)
    {
        var bytes = new byte[Marshal.SizeOf<T>()];
        var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        value = default;
        try
        {
            if (!GetProfileProperty(propertyName, handle.AddrOfPinnedObject(), (uint)bytes.Length))
            {
                LogManager.LogDebug("Failed to get RTSS property: {0}", propertyName);
                return false;
            }

            value = Marshal.PtrToStructure<T>(handle.AddrOfPinnedObject());
            return true;
        }
        catch (Exception ex)
        {
            LogManager.LogError("Error reading RTSS property {0}: {1}", propertyName, ex.Message);
            return false;
        }
        finally
        {
            handle.Free();
        }
    }

    public bool SetProfileProperty<T>(string propertyName, T value)
    {
        var bytes = new byte[Marshal.SizeOf<T>()];
        var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            Marshal.StructureToPtr(value, handle.AddrOfPinnedObject(), false);
            return SetProfileProperty(propertyName, handle.AddrOfPinnedObject(), (uint)bytes.Length);
        }
        catch (Exception ex)
        {
            LogManager.LogError("Error setting RTSS property {0}: {1}", propertyName, ex.Message);
            return false;
        }
        finally
        {
            handle.Free();
        }
    }

    public void UpdateSettings()
    {
        PostMessage(WM_RTSS_UPDATESETTINGS, nint.Zero, nint.Zero);
    }

    private void PostMessage(uint Msg, nint wParam, nint lParam)
    {
        var hWnd = FindWindow(null, "RTSS");
        if (hWnd == nint.Zero)
            hWnd = FindWindow(null, "RivaTuner Statistics Server");

        if (hWnd != nint.Zero)
            PostMessage(hWnd, Msg, wParam, lParam);
    }

    public uint EnableFlag(uint flag, bool status)
    {
        var current = SetFlags(~flag, status ? flag : 0);
        UpdateSettings();
        return current;
    }

    public bool GetEnableOSD()
    {
        if (!IsRunning)
            return false;

        try
        {
            // load default profile
            if (!profileLoaded)
            {
                LoadProfile();
                profileLoaded = true;
            }

            if (GetProfileProperty("EnableOSD", out int enabled))
                return Convert.ToBoolean(enabled);
        }
        catch (Exception ex)
        {
            LogManager.LogError("Error getting RTSS OSD state: {0}", ex.Message);
        }

        return false;
    }

    private bool SetProfilePropertyAndUpdate<T>(string propertyName, T value, string errorMessage)
    {
        if (!IsRunning)
            return false;

        try
        {
            LoadProfile();

            if (SetProfileProperty(propertyName, value))
            {
                SaveProfile();
                UpdateProfiles();
                return true;
            }
        }
        catch (Exception ex)
        {
            LogManager.LogWarning("{0}: {1}", errorMessage, ex.Message);
        }

        return false;
    }

    public bool SetEnableOSD(bool enable)
    {
        return SetProfilePropertyAndUpdate("EnableOSD", enable ? 1 : 0,
            "Failed to set OSD visibility settings in RTSS");
    }

    private bool SetTargetFPS(int limit)
    {
        return SetProfilePropertyAndUpdate("FramerateLimit", limit,
            "Failed to set Framerate Limit in RTSS");
    }

    private int GetTargetFPS()
    {
        if (!IsRunning)
            return 0;

        try
        {
            // load default profile
            if (!profileLoaded)
            {
                LoadProfile();
                profileLoaded = true;
            }

            if (GetProfileProperty("FramerateLimit", out int fpsLimit))
                return fpsLimit;
        }
        catch (Exception ex)
        {
            LogManager.LogError("Error getting RTSS target FPS: {0}", ex.Message);
        }

        return 0;
    }

    public void RequestFPS(int framerate, bool immediate = false)
    {
        RequestedFramerate = framerate;
        if (!immediate)
            return;

        SetTargetFPS(framerate);
    }

    public override bool StartProcess()
    {
        if (!IsInstalled)
            return false;

        if (IsRunning)
            KillProcess();

        return base.StartProcess();
    }

    public override bool StopProcess()
    {
        if (IsStarting)
            return false;
        if (!IsInstalled)
            return false;
        if (!IsRunning)
            return false;

        KillProcess();

        return true;
    }

    public override void Dispose()
    {
        // Dispose timer
        if (PlatformWatchdog != null)
        {
            PlatformWatchdog.Stop();
            PlatformWatchdog.Dispose();
        }

        Stop();
        base.Dispose();
    }

    #region struct

    [return: MarshalAs(UnmanagedType.Bool)]
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool PostMessage(nint hWnd, uint Msg, nint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint FindWindow(string lpClassName, string lpWindowName);

    [DllImport("RTSSHooks64.dll")]
    public static extern uint SetFlags(uint dwAND, uint dwXOR);

    [DllImport("RTSSHooks64.dll", CharSet = CharSet.Ansi)]
    public static extern void LoadProfile(string profile = GLOBAL_PROFILE);

    [DllImport("RTSSHooks64.dll", CharSet = CharSet.Ansi)]
    public static extern void SaveProfile(string profile = GLOBAL_PROFILE);

    [DllImport("RTSSHooks64.dll", CharSet = CharSet.Ansi)]
    public static extern void DeleteProfile(string profile = GLOBAL_PROFILE);

    [DllImport("RTSSHooks64.dll", CharSet = CharSet.Ansi)]
    public static extern bool GetProfileProperty(string propertyName, nint value, uint size);

    [DllImport("RTSSHooks64.dll", CharSet = CharSet.Ansi)]
    public static extern bool SetProfileProperty(string propertyName, nint value, uint size);

    [DllImport("RTSSHooks64.dll", CharSet = CharSet.Ansi)]
    public static extern void ResetProfile(string profile = GLOBAL_PROFILE);

    [DllImport("RTSSHooks64.dll", CharSet = CharSet.Ansi)]
    public static extern void UpdateProfiles();

    #endregion

    #region events

    public event HookedEventHandler Hooked;

    public delegate void HookedEventHandler(AppEntry appEntry);

    public event UnhookedEventHandler Unhooked;

    public delegate void UnhookedEventHandler(int processId);

    #endregion
}