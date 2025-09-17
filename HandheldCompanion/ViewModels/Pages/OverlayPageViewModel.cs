using HandheldCompanion.Devices;
using HandheldCompanion.GraphicsProcessingUnit;
using HandheldCompanion.Managers;
using HandheldCompanion.Misc;
using HandheldCompanion.Platforms;
using LiveCharts;
using System;
using System.Timers;
using System.Windows;
using System.Windows.Media;
using System.Xml.Linq;

namespace HandheldCompanion.ViewModels
{
    public class OverlayPageViewModel : BaseViewModel
    {
        public bool IsRunningRTSS => PlatformManager.RTSS.IsInstalled;

        public bool IsRunningLHM => PlatformManager.LibreHardwareMonitor.IsInstalled;

        private int _onScreenDisplayLevel;
        public int OnScreenDisplayLevel
        {
            get => _onScreenDisplayLevel;
            set
            {
                if (value != OnScreenDisplayLevel)
                {
                    _onScreenDisplayLevel = value;
                    ManagerFactory.settingsManager.Set(Settings.OnScreenDisplayLevel, value);
                    OnPropertyChanged(nameof(OnScreenDisplayLevel));
                }
            }
        }

        private int _onScreenDisplayTimeLevel;
        public int OnScreenDisplayTimeLevel
        {
            get => _onScreenDisplayTimeLevel;
            set
            {
                if (value != OnScreenDisplayTimeLevel)
                {
                    _onScreenDisplayTimeLevel = value;
                    ManagerFactory.settingsManager.Set(Settings.OnScreenDisplayTimeLevel, value);
                    OnPropertyChanged(nameof(OnScreenDisplayTimeLevel));
                }
            }
        }

        private int _onScreenDisplayFPSLevel;
        public int OnScreenDisplayFPSLevel
        {
            get => _onScreenDisplayFPSLevel;
            set
            {
                if (value != OnScreenDisplayFPSLevel)
                {
                    _onScreenDisplayFPSLevel = value;
                    ManagerFactory.settingsManager.Set(Settings.OnScreenDisplayFPSLevel, value);
                    OnPropertyChanged(nameof(OnScreenDisplayFPSLevel));
                }
            }
        }

        private int _onScreenDisplayCPULevel;
        public int OnScreenDisplayCPULevel
        {
            get => _onScreenDisplayCPULevel;
            set
            {
                if (value != OnScreenDisplayCPULevel)
                {
                    _onScreenDisplayCPULevel = value;
                    ManagerFactory.settingsManager.Set(Settings.OnScreenDisplayCPULevel, value);
                    OnPropertyChanged(nameof(OnScreenDisplayCPULevel));
                }
            }
        }

        private int _onScreenDisplayGPULevel;
        public int OnScreenDisplayGPULevel
        {
            get => _onScreenDisplayGPULevel;
            set
            {
                if (value != OnScreenDisplayGPULevel)
                {
                    _onScreenDisplayGPULevel = value;
                    ManagerFactory.settingsManager.Set(Settings.OnScreenDisplayGPULevel, value);
                    OnPropertyChanged(nameof(OnScreenDisplayGPULevel));
                }
            }
        }

        private int _onScreenDisplayRAMLevel;
        public int OnScreenDisplayRAMLevel
        {
            get => _onScreenDisplayRAMLevel;
            set
            {
                if (value != OnScreenDisplayRAMLevel)
                {
                    _onScreenDisplayRAMLevel = value;
                    ManagerFactory.settingsManager.Set(Settings.OnScreenDisplayRAMLevel, value);
                    OnPropertyChanged(nameof(OnScreenDisplayRAMLevel));
                }
            }
        }

        private int _onScreenDisplayVRAMLevel;
        public int OnScreenDisplayVRAMLevel
        {
            get => _onScreenDisplayVRAMLevel;
            set
            {
                if (value != OnScreenDisplayVRAMLevel)
                {
                    _onScreenDisplayVRAMLevel = value;
                    ManagerFactory.settingsManager.Set(Settings.OnScreenDisplayVRAMLevel, value);
                    OnPropertyChanged(nameof(OnScreenDisplayVRAMLevel));
                }
            }
        }

        private int _onScreenDisplayBATTLevel;
        public int OnScreenDisplayBATTLevel
        {
            get => _onScreenDisplayBATTLevel;
            set
            {
                if (value != OnScreenDisplayBATTLevel)
                {
                    _onScreenDisplayBATTLevel = value;
                    ManagerFactory.settingsManager.Set(Settings.OnScreenDisplayBATTLevel, value);
                    OnPropertyChanged(nameof(OnScreenDisplayBATTLevel));
                }
            }
        }

        private float _CPUPower;
        public float CPUPower
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

        private float _CPUTemperature;
        public float CPUTemperature
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

        private float _CPULoad;
        public float CPULoad
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

        private float _GPUPower;
        public float GPUPower
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

        private float _GPUTemperature;
        public float GPUTemperature
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

        private float _GPULoad;
        public float GPULoad
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

        private double _Framerate;
        public double Framerate
        {
            get => _Framerate;
            set
            {
                if (value != Framerate)
                {
                    _Framerate = value;
                    OnPropertyChanged(nameof(Framerate));
                }
            }
        }

        private double _Frametime;
        public double Frametime
        {
            get => _Frametime;
            set
            {
                if (value != Frametime)
                {
                    _Frametime = value;
                    OnPropertyChanged(nameof(Frametime));
                }
            }
        }

        private ChartValues<double> _framerateValues = [];
        public ChartValues<double> FramerateValues
        {
            get { return _framerateValues; }
            set
            {
                _framerateValues = value;
                OnPropertyChanged(nameof(FramerateValues));
            }
        }

        private ImageSource _ProcessIcon;
        public ImageSource ProcessIcon
        {
            get => _ProcessIcon;
            set
            {
                if (value != ProcessIcon)
                {
                    _ProcessIcon = value;
                    OnPropertyChanged(nameof(ProcessIcon));
                }
            }
        }

        private string _ProcessName = Properties.Resources.QuickProfilesPage_Waiting;
        public string ProcessName
        {
            get => _ProcessName;
            set
            {
                if (value != ProcessName)
                {
                    _ProcessName = value;
                    OnPropertyChanged(nameof(ProcessName));
                }
            }
        }

        private string _ProcessPath;
        public string ProcessPath
        {
            get => _ProcessPath;
            set
            {
                if (value != ProcessPath)
                {
                    _ProcessPath = value;
                    OnPropertyChanged(nameof(ProcessPath));
                }
            }
        }

        private Timer updateTimer;
        private int updateInterval = 1000;

        private Timer framerateTimer;
        private int framerateInterval = 1000;

        public OverlayPageViewModel()
        {
            updateTimer = new Timer(updateInterval) { Enabled = true };
            updateTimer.Elapsed += UpdateTimer_Elapsed;

            framerateTimer = new Timer(framerateInterval) { Enabled = true };
            framerateTimer.Elapsed += FramerateTimer_Elapsed;

            _onScreenDisplayLevel = ManagerFactory.settingsManager.Get<int>(Settings.OnScreenDisplayLevel);
            _onScreenDisplayTimeLevel = ManagerFactory.settingsManager.Get<int>(Settings.OnScreenDisplayTimeLevel);
            _onScreenDisplayFPSLevel = ManagerFactory.settingsManager.Get<int>(Settings.OnScreenDisplayFPSLevel);
            _onScreenDisplayCPULevel = ManagerFactory.settingsManager.Get<int>(Settings.OnScreenDisplayCPULevel);
            _onScreenDisplayGPULevel = ManagerFactory.settingsManager.Get<int>(Settings.OnScreenDisplayGPULevel);
            _onScreenDisplayRAMLevel = ManagerFactory.settingsManager.Get<int>(Settings.OnScreenDisplayRAMLevel);
            _onScreenDisplayVRAMLevel = ManagerFactory.settingsManager.Get<int>(Settings.OnScreenDisplayVRAMLevel);
            _onScreenDisplayBATTLevel = ManagerFactory.settingsManager.Get<int>(Settings.OnScreenDisplayBATTLevel);

            CPUName = IDevice.GetCurrent().Processor;

            // manage events
            ManagerFactory.settingsManager.SettingValueChanged += SettingsManager_SettingValueChanged;
            PlatformManager.RTSS.Updated += PlatformManager_RTSS_Updated;
            PlatformManager.HardwareMonitor.CPUPowerChanged += LibreHardwareMonitor_CPUPowerChanged;
            PlatformManager.HardwareMonitor.CPUTemperatureChanged += LibreHardwareMonitor_CPUTemperatureChanged;
            PlatformManager.HardwareMonitor.CPULoadChanged += LibreHardwareMonitor_CPULoadChanged;
            GPUManager.Hooked += GPUManager_Hooked;
            ManagerFactory.processManager.ForegroundChanged += ProcessManager_ForegroundChanged;

            // raise events
            if (GPUManager.IsInitialized)
            {
                GPU gpu = GPUManager.GetCurrent();
                if (gpu is not null)
                    GPUManager_Hooked(gpu);
            }

            if (ProcessManager.IsInitialized)
            {
                ProcessManager_ForegroundChanged(ProcessManager.GetForegroundProcess(), null);
            }
        }

        private void ProcessManager_ForegroundChanged(ProcessEx? processEx, ProcessEx? backgroundEx)
        {
            // get path
            string path = processEx != null ? processEx.Path : string.Empty;

            Application.Current.Dispatcher.Invoke(() =>
            {
                ProcessIcon = processEx?.ProcessIcon;

                if (processEx is null)
                {
                    ProcessName = Properties.Resources.QuickProfilesPage_Waiting;
                    ProcessPath = string.Empty;
                }
                else
                {
                    ProcessName = processEx.Executable;
                    ProcessPath = processEx.Path;
                }
            });
        }

        private void UpdateTimer_Elapsed(object? sender, ElapsedEventArgs e)
        {
            GPU gpu = GPUManager.GetCurrent();
            if (gpu is not null)
            {
                if (gpu.HasPower())
                {
                    GPUPower = (float)Math.Round((float)gpu.GetPower());
                }

                if (gpu.HasLoad())
                {
                    GPULoad = (float)Math.Round((float)gpu.GetLoad());
                }

                if (gpu.HasTemperature())
                {
                    GPUTemperature = (float)Math.Round((float)gpu.GetTemperature());
                }
            }
        }

        private void FramerateTimer_Elapsed(object? sender, ElapsedEventArgs e)
        {
            if (PlatformManager.RTSS.HasHook())
            {
                PlatformManager.RTSS.RefreshAppEntry();

                // refresh values
                Framerate = Math.Round(PlatformManager.RTSS.GetFramerate());
                Frametime = Math.Round(PlatformManager.RTSS.GetFrametime(), 1);

                /*
                if (FramerateValues.Count == 100)
                    FramerateValues.RemoveAt(0);

                FramerateValues.Add(framerate);
                */
            }
            else
            {
                Framerate = 0;
                Frametime = 0;

                /*
                if (FramerateValues.Count != 0)
                    FramerateValues = new();
                */
            }
        }

        private void GPUManager_Hooked(GPU GPU)
        {
            // localize me
            GPUName = GPU is not null ? GPU.adapterInformation.Details.Description : "No GPU detected";

            HasGPUPower = GPU is not null && GPU.HasPower();
            HasGPUTemperature = GPU is not null && GPU.HasTemperature();
            HasGPULoad = GPU is not null && GPU.HasLoad();
        }

        private void LibreHardwareMonitor_CPULoadChanged(object? value)
        {
            if (value is null)
                return;

            CPULoad = (float)Math.Round((float)value);
        }

        private void LibreHardwareMonitor_CPUTemperatureChanged(object? value)
        {
            if (value is null)
                return;

            CPUTemperature = (float)Math.Round((float)value);
        }

        private void LibreHardwareMonitor_CPUPowerChanged(object? value)
        {
            if (value is null)
                return;

            CPUPower = (float)Math.Round((float)value);
        }

        public override void Dispose()
        {
            ManagerFactory.settingsManager.SettingValueChanged -= SettingsManager_SettingValueChanged;
            PlatformManager.RTSS.Updated -= PlatformManager_RTSS_Updated;
            base.Dispose();
        }

        private void SettingsManager_SettingValueChanged(string name, object value, bool temporary)
        {
            switch (name)
            {
                case "OnScreenDisplayRefreshRate":
                    updateInterval = Convert.ToInt32(value);
                    updateTimer.Interval = updateInterval;

                    framerateInterval = Convert.ToInt32(value);
                    framerateTimer.Interval = framerateInterval;
                    return;
            }

            UpdateSettings(name, value);
            OnPropertyChanged(name); // setting names matches property name
        }

        private void PlatformManager_RTSS_Updated(PlatformStatus status)
        {
            if (status == Platforms.PlatformStatus.Stalled)
                OnScreenDisplayLevel = 0;
        }

        private void UpdateSettings(string name, object value)
        {
            if (name == Settings.OnScreenDisplayLevel)
                _onScreenDisplayLevel = Convert.ToInt32(value);

            else if (name == Settings.OnScreenDisplayTimeLevel)
                _onScreenDisplayTimeLevel = Convert.ToInt32(value);

            else if (name == Settings.OnScreenDisplayFPSLevel)
                _onScreenDisplayFPSLevel = Convert.ToInt32(value);

            else if (name == Settings.OnScreenDisplayCPULevel)
                _onScreenDisplayCPULevel = Convert.ToInt32(value);

            else if (name == Settings.OnScreenDisplayGPULevel)
                _onScreenDisplayGPULevel = Convert.ToInt32(value);

            else if (name == Settings.OnScreenDisplayRAMLevel)
                _onScreenDisplayRAMLevel = Convert.ToInt32(value);

            else if (name == Settings.OnScreenDisplayVRAMLevel)
                _onScreenDisplayVRAMLevel = Convert.ToInt32(value);

            else if (name == Settings.OnScreenDisplayBATTLevel)
                _onScreenDisplayBATTLevel = Convert.ToInt32(value);
        }
    }
}
