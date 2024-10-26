using HandheldCompanion.Controls;
using HandheldCompanion.Managers;
using HandheldCompanion.Managers.Desktop;
using HandheldCompanion.Misc;
using HandheldCompanion.Utils;
using RTSSSharedMemoryNET;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Path = System.IO.Path;
using Timer = System.Timers.Timer;

namespace HandheldCompanion.Platforms;

public class RTSS : IPlatform
{
    private const uint WM_APP = 0x8000;
    private const uint WM_RTSS_UPDATESETTINGS = WM_APP + 100;
    private const uint WM_RTSS_SHOW_PROPERTIES = WM_APP + 102;

    private const uint RTSSHOOKSFLAG_OSD_VISIBLE = 1;
    private const uint RTSSHOOKSFLAG_LIMITER_DISABLED = 4;
    private const string GLOBAL_PROFILE = "";

    private int hookedProcessId = 0;
    private uint lastOsdFrameId = 0;
    private bool profileLoaded;
    private AppEntry? appEntry;

    private int RequestedFramerate;

    public RTSS()
    {
        PlatformType = PlatformType.RTSS;
        ExpectedVersion = new Version(7, 3, 4);
        Url = "https://www.guru3d.com/files-details/rtss-rivatuner-statistics-server-download.html";

        Name = "RTSS";
        ExecutableName = RunningName = "RTSS.exe";

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
        PlatformWatchdog = new Timer(2000) { Enabled = false };
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

        ProcessManager.ForegroundChanged += ProcessManager_ForegroundChanged;
        ProcessManager.ProcessStopped += ProcessManager_ProcessStopped;
        ProfileManager.Applied += ProfileManager_Applied;

        // If RTSS was started while HC was fully initialized, we need to pass both current profile and foreground process
        if (SettingsManager.IsInitialized)
        {
            ProcessManager_ForegroundChanged(ProcessManager.GetForegroundProcess(), null);
            ProfileManager_Applied(ProfileManager.GetCurrent(), UpdateSource.Background);
        }

        return base.Start();
    }

    public override bool Stop(bool kill = false)
    {
        ProcessManager.ForegroundChanged -= ProcessManager_ForegroundChanged;
        ProcessManager.ProcessStopped -= ProcessManager_ProcessStopped;
        ProfileManager.Applied -= ProfileManager_Applied;

        return base.Stop(kill);
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
            // Determine most approriate frame rate limit based on screen frequency
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

    private async void ProcessManager_ForegroundChanged(ProcessEx? processEx, ProcessEx? backgroundEx)
    {
        if (processEx is null || processEx == ProcessManager.Empty)
            return;

        // hook new process
        appEntry = null;

        var processId = processEx.ProcessId;
        var foregroundId = processId;
        if (processId == 0) return;


        if (processEx.Filter != ProcessEx.ProcessFilter.Allowed) return;
        do
        {
            /*
             * loop until we either:
             * - got an RTSS entry
             * - process no longer exists
             * - RTSS was closed
             */

            var foreground = ProcessManager.GetForegroundProcess();
            foregroundId = foreground is not null ? foreground.ProcessId : 0;

            try
            {
                appEntry = OSD.GetAppEntries().FirstOrDefault(entry =>
                        (entry.Flags & AppFlags.MASK) != AppFlags.None &&
                        entry.ProcessId == processId
                    );
            }
            catch (FileNotFoundException) { return; }
            catch { }
            await Task.Delay(1000);
        } while (appEntry is null && foregroundId == processId && KeepAlive);

        OnHook();
    }

    private void OnHook()
    {
        if (appEntry is null)
            return;

        // set HookedProcessId
        hookedProcessId = appEntry.ProcessId;
        lastOsdFrameId = appEntry.OSDFrameId;

        // raise event
        Hooked?.Invoke(appEntry);
    }

    private void OnUnhook(ProcessEx processEx)
    {
        var processId = processEx.ProcessId;
        if (processId != hookedProcessId)
            return;

        // clear HookedProcessId
        hookedProcessId = 0;
        appEntry = null;

        // raise event
        Unhooked?.Invoke(processId);
    }


    private void ProcessManager_ProcessStopped(ProcessEx processEx)
    {
        OnUnhook(processEx);
    }

    private void PlatformWatchdogElapsed()
    {
        lock (updateLock)
        {
            // reset tentative counter
            Tentative = 0;

            if (GetTargetFPS() != RequestedFramerate)
                SetTargetFPS(RequestedFramerate);

            try
            {
                // force "Show On-Screen Display" to On
                _ = SetFlags(~RTSSHOOKSFLAG_OSD_VISIBLE, RTSSHOOKSFLAG_OSD_VISIBLE);
            }
            catch (DllNotFoundException)
            { }

            // force "On-Screen Display Support" to On
            if (GetEnableOSD() != true)
                SetEnableOSD(true);
        }
    }

    public bool HasHook()
    {
        return hookedProcessId != 0;
    }

    public void RefreshAppEntry()
    {
        // refresh appEntry
        appEntry = OSD.GetAppEntries().Where(x => (x.Flags & AppFlags.MASK) != AppFlags.None).FirstOrDefault(a => a.ProcessId == (appEntry is not null ? appEntry.ProcessId : 0));
        lastOsdFrameId = appEntry is null || appEntry.OSDFrameId == lastOsdFrameId ? 0 : appEntry.OSDFrameId;
    }

    public double GetFramerate(bool refresh = false)
    {
        try
        {
            if (refresh)
                RefreshAppEntry();
            if (appEntry is null || lastOsdFrameId == 0) return 0.0d;
            return appEntry.StatFrameTimeBufFramerate / 10.0d;
        }
        catch (InvalidDataException) { }
        catch (FileNotFoundException) { }
        return 0d;
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
        catch (InvalidDataException) { }
        catch (FileNotFoundException) { }
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
                return false;

            value = Marshal.PtrToStructure<T>(handle.AddrOfPinnedObject());
            return true;
        }
        catch
        {
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
        catch
        {
            return false;
        }
        finally
        {
            handle.Free();
        }
    }

    public void UpdateSettings()
    {
        PostMessage(WM_RTSS_UPDATESETTINGS, IntPtr.Zero, IntPtr.Zero);
    }

    private void PostMessage(uint Msg, IntPtr wParam, IntPtr lParam)
    {
        var hWnd = FindWindow(null, "RTSS");
        if (hWnd == IntPtr.Zero)
            hWnd = FindWindow(null, "RivaTuner Statistics Server");

        if (hWnd != IntPtr.Zero)
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
        catch
        {
        }

        return false;
    }

    public bool SetEnableOSD(bool enable)
    {
        if (!IsRunning)
            return false;

        try
        {
            // Ensure Global profile is loaded
            LoadProfile();

            // Set EnableOSD as requested
            if (SetProfileProperty("EnableOSD", enable ? 1 : 0))
            {
                // Save and reload profile
                SaveProfile();
                UpdateProfiles();

                return true;
            }
        }
        catch
        {
            LogManager.LogWarning("Failed to set OSD visibility settings in RTSS");
        }

        return false;
    }

    private bool SetTargetFPS(int limit)
    {
        if (!IsRunning)
            return false;

        try
        {
            // Ensure Global profile is loaded
            LoadProfile();

            // Set Framerate Limit as requested
            if (SetProfileProperty("FramerateLimit", limit))
            {
                // Save and reload profile
                SaveProfile();
                UpdateProfiles();

                return true;
            }
        }
        catch
        {
            LogManager.LogWarning("Failed to set Framerate Limit in RTSS");
        }

        /*
        if (File.Exists(SettingsPath))
        {
            IniFile iniFile = new(SettingsPath);
            if (iniFile.Write("Limit", Limit.ToString(), "Framerate"))
            {
                UpdateProfiles();
                return true;
            }
        }
        */

        return false;
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
        catch
        {
        }

        return 0;

        /*
        if (File.Exists(SettingsPath))
        {
            IniFile iniFile = new(SettingsPath);
            return Convert.ToInt32(iniFile.Read("Limit", "Framerate"));
        }
        */
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
        Stop();
        base.Dispose();
    }

    #region struct

    [return: MarshalAs(UnmanagedType.Bool)]
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

    [DllImport("RTSSHooks64.dll")]
    public static extern uint SetFlags(uint dwAND, uint dwXOR);

    [DllImport("RTSSHooks64.dll", CharSet = CharSet.Ansi)]
    public static extern void LoadProfile(string profile = GLOBAL_PROFILE);

    [DllImport("RTSSHooks64.dll", CharSet = CharSet.Ansi)]
    public static extern void SaveProfile(string profile = GLOBAL_PROFILE);

    [DllImport("RTSSHooks64.dll", CharSet = CharSet.Ansi)]
    public static extern void DeleteProfile(string profile = GLOBAL_PROFILE);

    [DllImport("RTSSHooks64.dll", CharSet = CharSet.Ansi)]
    public static extern bool GetProfileProperty(string propertyName, IntPtr value, uint size);

    [DllImport("RTSSHooks64.dll", CharSet = CharSet.Ansi)]
    public static extern bool SetProfileProperty(string propertyName, IntPtr value, uint size);

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