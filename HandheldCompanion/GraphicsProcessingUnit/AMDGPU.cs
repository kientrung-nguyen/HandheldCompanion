﻿using HandheldCompanion.ADLX;
using HandheldCompanion.Managers;
using HandheldCompanion.Utils;
using SharpDX.Direct3D9;
using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Timers;
using WindowsDisplayAPI;
using static HandheldCompanion.ADLX.ADLXBackend;
using Timer = System.Timers.Timer;

namespace HandheldCompanion.GraphicsProcessingUnit
{
    public class AmdGpu : GPU
    {
        #region events
        public event RSRStateChangedEventHandler RSRStateChanged;
        public delegate void RSRStateChangedEventHandler(bool Supported, bool Enabled, int Sharpness);

        public event AFMFStateChangedEventHandler AFMFStateChanged;
        public delegate void AFMFStateChangedEventHandler(bool Supported, bool Enabled);
        #endregion

        private bool prevRSRSupport = false;
        private bool prevRSR = false;
        private int prevRSRSharpness = -1;

        private bool prevAFMFSupport = false;
        private bool prevAFMF = false;

        protected new AdlxTelemetryData TelemetryData = new();
        //protected AmdGpuControl AmdGpuControl;
        //protected ADLSingleSensorData[] AmdGpuSensorsData = [];

        public bool HasRSRSupport()
        {
            if (!IsInitialized)
                return false;

            return Execute(ADLXBackend.HasRSRSupport, false);
        }

        public override bool HasIntegerScalingSupport()
        {
            if (!IsInitialized)
                return false;

            return Execute(() => ADLXBackend.HasIntegerScalingSupport(displayIdx), false);
        }

        public override bool HasGPUScalingSupport()
        {
            if (!IsInitialized)
                return false;

            return Execute(() => ADLXBackend.HasGPUScalingSupport(displayIdx), false);
        }

        public override bool HasScalingModeSupport()
        {
            if (!IsInitialized)
                return false;

            return Execute(() => ADLXBackend.HasScalingModeSupport(displayIdx), false);
        }

        public bool GetRSR()
        {
            if (!IsInitialized)
                return false;

            return Execute(ADLXBackend.GetRSR, false);
        }

        public bool HasAFMFSupport()
        {
            if (!IsInitialized)
                return false;

            return Execute(ADLXBackend.HasAFMFSupport, false);
        }

        public bool GetAFMF()
        {
            if (!IsInitialized)
                return false;

            return Execute(ADLXBackend.GetAFMF, false);
        }

        public int GetRSRSharpness()
        {
            if (!IsInitialized)
                return -1;

            return Execute(ADLXBackend.GetRSRSharpness, -1);
        }

        public override bool GetImageSharpening()
        {
            if (!IsInitialized)
                return false;

            return Execute(() => ADLXBackend.GetImageSharpening(deviceIdx), false);
        }

        public override int GetImageSharpeningSharpness()
        {
            if (!IsInitialized)
                return -1;

            return Execute(() => ADLXBackend.GetImageSharpeningSharpness(deviceIdx), -1);
        }

        public override bool GetIntegerScaling()
        {
            if (!IsInitialized)
                return false;

            return Execute(() => ADLXBackend.GetIntegerScaling(displayIdx), false);
        }

        public override bool GetGPUScaling()
        {
            if (!IsInitialized)
                return false;

            return Execute(() => ADLXBackend.GetGPUScaling(displayIdx), false);
        }

        public bool SetAntiLag(bool enable)
        {
            if (!IsInitialized)
                return false;

            return Execute(() => ADLXBackend.SetAntiLag(displayIdx, enable), false);
        }

        public bool GetAntiLag()
        {
            if (!IsInitialized)
                return false;

            return Execute(() => ADLXBackend.GetAntiLag(displayIdx), false);
        }

        public override int GetScalingMode()
        {
            if (!IsInitialized)
                return -1;

            return Execute(() => ADLXBackend.GetScalingMode(displayIdx), -1);
        }

        public bool SetRSRSharpness(int sharpness)
        {
            if (!IsInitialized)
                return false;

            return Execute(() => ADLXBackend.SetRSRSharpness(sharpness), false);
        }

        public override bool SetImageSharpening(bool enable)
        {
            if (!IsInitialized)
                return false;

            return Execute(() => ADLXBackend.SetImageSharpening(deviceIdx, enable), false);
        }

        public bool SetRSR(bool enable)
        {
            if (!IsInitialized)
                return false;

            // mutually exclusive
            if (enable)
            {
                if (GetIntegerScaling())
                    SetIntegerScaling(false);

                if (GetImageSharpening())
                    SetImageSharpening(false);
            }

            return Execute(() => ADLXBackend.SetRSR(enable), false);
        }

        public bool SetAFMF(bool enable)
        {
            if (!IsInitialized)
                return false;

            return Execute(() => ADLXBackend.SetAFMF(enable), false);
        }

        public override bool SetImageSharpeningSharpness(int sharpness)
        {
            if (!IsInitialized)
                return false;

            return Execute(() => ADLXBackend.SetImageSharpeningSharpness(deviceIdx, sharpness), false);
        }

        public override bool SetIntegerScaling(bool enabled, byte type = 0)
        {
            if (!IsInitialized)
                return false;

            // mutually exclusive
            if (enabled)
            {
                if (GetRSR())
                    SetRSR(false);
            }

            return Execute(() => ADLXBackend.SetIntegerScaling(displayIdx, enabled), false);
        }

        public override bool SetGPUScaling(bool enabled)
        {
            if (!IsInitialized)
                return false;

            return Execute(() => ADLXBackend.SetGPUScaling(displayIdx, enabled), false);
        }

        public override bool SetScalingMode(int mode)
        {
            if (!IsInitialized)
                return false;

            return Execute(() => ADLXBackend.SetScalingMode(displayIdx, mode), false);
        }

        private AdlxTelemetryData GetTelemetry()
        {
            if (!IsInitialized)
                return TelemetryData;

            return Execute(() =>
            {
                ADLXBackend.GetAdlxTelemetry(deviceIdx, ref TelemetryData);
                return TelemetryData;
            }, TelemetryData);
        }

        public override bool HasClock()
        {
            return TelemetryData.gpuClockSpeedSupported;
        }

        public override float GetClock()
        {
            return (float)TelemetryData.gpuClockSpeedValue;
        }

        public override bool HasLoad()
        {
            return TelemetryData.gpuUsageSupported;
        }

        public override float GetLoad()
        {
            return (float)TelemetryData.gpuUsageValue;
        }

        public override bool HasPower()
        {
            return TelemetryData.gpuPowerSupported;
        }

        public override float GetPower()
        {
            return (float)TelemetryData.gpuPowerValue;
        }

        public override bool HasTemperature()
        {
            return TelemetryData.gpuTemperatureSupported;
        }

        public override float GetTemperature()
        {
            return (float)TelemetryData.gpuTemperatureValue;
        }

        public override bool HasVRAMUsage()
        {
            return TelemetryData.gpuVramSupported;
        }

        public override float GetVRAMUsage()
        {
            return (float)TelemetryData.gpuVramValue;
        }

        public AmdGpu(AdapterInformation adapterInformation) : base(adapterInformation)
        {
            ADLX_RESULT result = ADLX_RESULT.ADLX_FAIL;
            var adapterCount = 0;
            var uniqueId = 0;
            var friendlyName = Display.GetDisplays()
                .FirstOrDefault(v => v.DisplayScreen.ToPathDisplaySource().DisplayName.Equals(adapterInformation.Details.DeviceName))?
                .ToPathDisplayTarget().FriendlyName ?? string.Empty;

            result = GetNumberOfDisplays(ref adapterCount);
            if (result != ADLX_RESULT.ADLX_OK)
                return;

            if (adapterCount == 1)
                displayIdx = 0;
            else
            {
	            for (int idx = 0; idx < adapterCount; idx++)
	            {
	                var displayName = new StringBuilder(256); // Assume display name won't exceed 255 characters

	                // skip if failed to retrieve display
	                result = GetDisplayName(idx, displayName, displayName.Capacity);
	                if (result != ADLX_RESULT.ADLX_OK)
	                    continue;

	                // skip if display is not the one we're looking for
	                if (!displayName.ToString().Equals(friendlyName))
	                    continue;

	                // update displayIdx
	                displayIdx = idx;
	                break;
                }
            }

            if (displayIdx != -1)
            {
                // get the associated GPU UniqueId
                result = GetDisplayGPU(displayIdx, ref uniqueId);
                if (result == ADLX_RESULT.ADLX_OK)
                {
                    result = GetGPUIndex(uniqueId, ref deviceIdx);
                    if (result == ADLX_RESULT.ADLX_OK)
                        IsInitialized = true;
                }
            }

            if (!IsInitialized)
                return;

            //AmdGpuControl = new AmdGpuControl();
            // pull telemetry once
            TelemetryData = GetTelemetry();

            UpdateTimer = new Timer(UpdateInterval)
            {
                AutoReset = true
            };
            UpdateTimer.Elapsed += UpdateTimer_Elapsed;

            TelemetryTimer = new Timer(TelemetryInterval)
            {
                AutoReset = true
            };
            TelemetryTimer.Elapsed += TelemetryTimer_Elapsed;
        }

        private void TelemetryTimer_Elapsed(object? sender, ElapsedEventArgs e)
        {
            if (halting)
                return;

            if (Monitor.TryEnter(telemetryLock))
            {
                try
                {
                    TelemetryData = GetTelemetry();
                    /*
                    for (int i = 0; i < AmdGpuSensorsData.Length; i++)
                    {
                        if (EnumUtils<ADLSensorType>.TryParse(i, out var type))
                        {
                            if (AmdGpuSensorsData[(int)type].Supported == 1)
                                LogManager.LogDebug($"{type}: {AmdGpuSensorsData[(int)type].Value}");

                        }
                        else if (AmdGpuSensorsData[i].Supported == 1)
                            LogManager.LogDebug($"[{i}]: {AmdGpuSensorsData[i].Value}");
                    }
                    */
                }
                finally
                {
                    Monitor.Exit(telemetryLock);
                }
            }
        }

        public override void Start()
        {
            if (!IsInitialized)
                return;

            base.Start();
        }

        public override void Stop()
        {
            base.Stop();
        }

        private void UpdateTimer_Elapsed(object? sender, ElapsedEventArgs e)
        {
            if (halting)
                return;

            if (Monitor.TryEnter(updateLock))
            {
                try
                {
                    bool GPUScaling = false;

                    try
                    {
                        // check for GPU Scaling support
                        // if yes, get GPU Scaling (bool)
                        bool GPUScalingSupport = HasGPUScalingSupport();
                        if (GPUScalingSupport)
                            GPUScaling = GetGPUScaling();

                        // check for Scaling Mode support
                        // if yes, get Scaling Mode (int)
                        bool ScalingSupport = HasScalingModeSupport();
                        int ScalingMode = 0;
                        if (ScalingSupport)
                            ScalingMode = GetScalingMode();

                        if (GPUScalingSupport != prevGPUScalingSupport || GPUScaling != prevGPUScaling || ScalingMode != prevScalingMode)
                        {
                            // raise event
                            base.OnGPUScalingChanged(GPUScalingSupport, GPUScaling, ScalingMode);

                            prevGPUScaling = GPUScaling;
                            prevScalingMode = ScalingMode;
                            prevGPUScalingSupport = GPUScalingSupport;
                        }
                    }
                    catch { }

                    try
                    {
                        // get RSR
                        bool RSRSupport = false;
                        bool RSR = false;
                        int RSRSharpness = GetRSRSharpness();

                        DateTime timeout = DateTime.Now.Add(TimeSpan.FromSeconds(2));
                        while (DateTime.Now < timeout && !RSRSupport)
                        {
                            RSRSupport = HasRSRSupport();
                            Thread.Sleep(250);
                        }
                        RSR = GetRSR();

                        if (RSRSupport != prevRSRSupport || RSR != prevRSR || RSRSharpness != prevRSRSharpness)
                        {
                            // raise event
                            RSRStateChanged?.Invoke(RSRSupport, RSR, RSRSharpness);

                            prevRSRSupport = RSRSupport;
                            prevRSR = RSR;
                            prevRSRSharpness = RSRSharpness;
                        }
                    }
                    catch { }

                    try
                    {
                        // get AFMF
                        bool AFMFSupport = false;
                        bool AFMF = false;

                        DateTime timeout = DateTime.Now.Add(TimeSpan.FromSeconds(2));
                        while (DateTime.Now < timeout && !AFMFSupport)
                        {
                            AFMFSupport = HasAFMFSupport();
                            Thread.Sleep(250);
                        }
                        AFMF = GetAFMF();

                        if (AFMFSupport != prevAFMFSupport || AFMF != prevAFMF)
                        {
                            // raise event
                            AFMFStateChanged?.Invoke(AFMFSupport, AFMF);

                            prevAFMFSupport = AFMFSupport;
                            prevAFMF = AFMF;
                        }
                    }
                    catch { }

                    try
                    {
                        // get gpu scaling and scaling mode
                        bool IntegerScalingSupport = false;
                        bool IntegerScaling = false;

                        DateTime timeout = DateTime.Now.Add(TimeSpan.FromSeconds(2));
                        while (DateTime.Now < timeout && !IntegerScalingSupport)
                        {
                            IntegerScalingSupport = HasIntegerScalingSupport();
                            Thread.Sleep(250);
                        }
                        IntegerScaling = GetIntegerScaling();

                        if (IntegerScalingSupport != prevIntegerScalingSupport || IntegerScaling != prevIntegerScaling)
                        {
                            // raise event
                            base.OnIntegerScalingChanged(IntegerScalingSupport, IntegerScaling);

                            prevIntegerScalingSupport = IntegerScalingSupport;
                            prevIntegerScaling = IntegerScaling;
                        }
                    }
                    catch { }

                    try
                    {
                        bool ImageSharpening = GetImageSharpening();
                        int ImageSharpeningSharpness = GetImageSharpeningSharpness();

                        if (ImageSharpening != prevImageSharpening || ImageSharpeningSharpness != prevImageSharpeningSharpness)
                        {
                            // raise event
                            base.OnImageSharpeningChanged(ImageSharpening, ImageSharpeningSharpness);

                            prevImageSharpening = ImageSharpening;
                            prevImageSharpeningSharpness = ImageSharpeningSharpness;
                        }
                    }
                    catch { }
                }
                finally
                {
                    Monitor.Exit(updateLock);
                }
            }
        }
    }
}
