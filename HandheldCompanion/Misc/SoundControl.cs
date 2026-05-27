using HandheldCompanion.Shared;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using System;
using System.Threading;

namespace HandheldCompanion.Misc;

public enum SoundDirections
{
    Input,
    Output
}

public static class SoundControl
{
    private static MMDeviceEnumerator enumerator = new();
    private static MMDevice MMDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
    private static MMDevice COMMDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);

    private static Action<SoundDirections, float, bool>? MMAudioEventHandler;
    private static readonly AudioNotificationClient MMAudioNotificationClient = new();

    public static double AudioGet()
    {
        if (MMDevice is null || MMDevice.AudioEndpointVolume is null)
            return -1;
        return Math.Round(MMDevice.AudioEndpointVolume.MasterVolumeLevelScalar * 100d);
    }

    public static void AudioSet(double volume)
    {
        if (MMDevice is null || MMDevice.AudioEndpointVolume is null)
            return;
        MMDevice.AudioEndpointVolume.MasterVolumeLevelScalar = (float)(volume / 100d);

    }

    public static bool? AudioMuted()
    {
        if (MMDevice is null || MMDevice.AudioEndpointVolume is null)
            return null;
        return MMDevice.AudioEndpointVolume.Mute;
    }

    public static void AudioMute(bool mute)
    {
        if (MMDevice is null || MMDevice.AudioEndpointVolume is null)
            return;
        MMDevice.AudioEndpointVolume.Mute = mute;
    }

    public static bool? ToggleAudio()
    {
        var isMute = AudioMuted();
        if (isMute is null)
            return null;

        MMDevice.AudioEndpointVolume.Mute = !isMute.Value;
        return MMDevice.AudioEndpointVolume.Mute;
    }

    public static double AudioAdjust(int delta)
    {
        var volume = AudioGet();
        volume = Math.Min(100, Math.Max(0, volume + delta));
        AudioSet(volume);
        return volume;
    }

    public static int MicrophoneGet()
    {
        if (COMMDevice is null || COMMDevice.AudioEndpointVolume is null)
            return -1;
        return (int)(COMMDevice.AudioEndpointVolume.MasterVolumeLevelScalar * 100f);
    }

    public static void MicrophoneSet(int volume)
    {
        if (COMMDevice is null || COMMDevice.AudioEndpointVolume is null)
            return;
        COMMDevice.AudioEndpointVolume.MasterVolumeLevelScalar = (float)(volume / 100f);

    }

    public static bool? MicrophoneMuted()
    {
        if (COMMDevice is null || COMMDevice.AudioEndpointVolume is null)
            return null;
        return COMMDevice.AudioEndpointVolume.Mute;
    }

    public static bool? ToggleMicrophone()
    {
        var isMute = MicrophoneMuted();
        if (isMute is null)
            return null;

        COMMDevice.AudioEndpointVolume.Mute = !isMute.Value;
        return COMMDevice.AudioEndpointVolume.Mute;
    }

    public static void SubscribeToEvents(Action<SoundDirections, float, bool> EventHandler)
    {
        try
        {
            MMAudioEventHandler = EventHandler;
            enumerator.RegisterEndpointNotificationCallback(MMAudioNotificationClient);
            if (MMDevice is not null && MMDevice.AudioEndpointVolume is not null)
            {
                MMDevice.AudioEndpointVolume.OnVolumeNotification += (data) =>
                {
                    EventHandler?.Invoke(
                        SoundDirections.Output,
                        (float)Math.Round(data.MasterVolume * 100f),
                        data.Muted);
                };
            }

            if (COMMDevice is not null && COMMDevice.AudioEndpointVolume is not null)
            {
                COMMDevice.AudioEndpointVolume.OnVolumeNotification += (data) =>
                {
                    EventHandler?.Invoke(
                        SoundDirections.Input,
                        (float)Math.Round(data.MasterVolume * 100f),
                        data.Muted);
                };
            }
        }
        catch
        {
            LogManager.LogError("Can't connect to Audio Endpoint Volume events");
            throw;
        }
    }

    public static void Unsubscribe()
    {
        enumerator.UnregisterEndpointNotificationCallback(MMAudioNotificationClient);
    }

    class AudioNotificationClient : IMMNotificationClient
    {
        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
        {
            if ((flow != DataFlow.Render || role != Role.Multimedia) && (flow != DataFlow.Capture || role != Role.Communications))
                return;

            switch (flow)
            {
                case DataFlow.Render:
                    {
                        var wasMuted = MMDevice.AudioEndpointVolume.Mute;
                        MMDevice.AudioEndpointVolume.OnVolumeNotification -= (data) => { };
                        MMDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                        if (MMDevice is not null && MMDevice.AudioEndpointVolume is not null)
                        {
                            MMDevice.AudioEndpointVolume.OnVolumeNotification += (data) =>
                            {
                                MMAudioEventHandler(
                                    SoundDirections.Output,
                                    (float)Math.Round(data.MasterVolume * 100f),
                                    data.Muted);
                            };
                            MMAudioEventHandler(
                                SoundDirections.Output,
                                (float)Math.Round(MMDevice.AudioEndpointVolume.MasterVolumeLevelScalar * 100f),
                                MMDevice.AudioEndpointVolume.Mute);
                            if (wasMuted != MMDevice.AudioEndpointVolume.Mute)
                                ;
                            Thread.Sleep(1000);
                        }
                    }
                    break;
                case DataFlow.Capture:
                    {
                        var wasMuted = MMDevice.AudioEndpointVolume.Mute;
                        COMMDevice.AudioEndpointVolume.OnVolumeNotification -= (data) => { };
                        COMMDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
                        if (COMMDevice is not null && COMMDevice.AudioEndpointVolume is not null)
                        {
                            COMMDevice.AudioEndpointVolume.OnVolumeNotification += (data) =>
                            {
                                MMAudioEventHandler(
                                    SoundDirections.Input,
                                    (float)Math.Round(data.MasterVolume * 100f),
                                    data.Muted);
                            };
                            MMAudioEventHandler(
                                SoundDirections.Input,
                                (float)Math.Round(COMMDevice.AudioEndpointVolume.MasterVolumeLevelScalar * 100f),
                                COMMDevice.AudioEndpointVolume.Mute);
                            if (wasMuted != COMMDevice.AudioEndpointVolume.Mute)
                                ;
                            //ToastManager.RunToast(
                            //    commDevice.AudioEndpointVolume.Mute ? Properties.Resources.Muted : Properties.Resources.Unmuted,
                            //    commDevice.AudioEndpointVolume.Mute ? ToastIcons.MicrophoneMute : ToastIcons.Microphone);
                            Thread.Sleep(1000);
                        }
                    }
                    break;

            }
        }

        public void OnDeviceAdded(string deviceId)
        {
        }

        public void OnDeviceRemoved(string deviceId)
        {
        }

        public void OnDeviceStateChanged(string deviceId, DeviceState newState)
        {
        }

        public void OnPropertyValueChanged(string deviceId, PropertyKey key)
        {
        }
    }

}
