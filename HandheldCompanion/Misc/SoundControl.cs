using HandheldCompanion.Managers;
using HandheldCompanion.Shared;
using HandheldCompanion.Views.Windows;
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
    private static MMDevice mmDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
    private static MMDevice commDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);

    private static Action<SoundDirections, float, bool> mmAudioEventHandler;
    private static AudioNotificationClient mmAudioNotificationClient = new();
    public static int AudioGet()
    {
        if (mmDevice is null || mmDevice.AudioEndpointVolume is null)
            return -1;
        return (int)Math.Round(mmDevice.AudioEndpointVolume.MasterVolumeLevelScalar * 100f);
    }

    public static void AudioSet(int volume)
    {
        if (mmDevice is null || mmDevice.AudioEndpointVolume is null)
            return;
        mmDevice.AudioEndpointVolume.MasterVolumeLevelScalar = (float)(volume / 100f);

    }

    public static bool? AudioMuted()
    {
        if (mmDevice is null || mmDevice.AudioEndpointVolume is null)
            return null;
        return mmDevice.AudioEndpointVolume.Mute;
    }

    public static bool? ToggleAudio()
    {
        var isMute = AudioMuted();
        if (isMute is null)
            return null;

        mmDevice.AudioEndpointVolume.Mute = !isMute.Value;
        return mmDevice.AudioEndpointVolume.Mute;
    }

    public static int AudioAdjust(int delta)
    {
        var volume = AudioGet();
        volume = Math.Min(100, Math.Max(0, volume + delta));
        AudioSet(volume);
        return volume;
    }

    public static int MicrophoneGet()
    {
        if (commDevice is null || commDevice.AudioEndpointVolume is null)
            return -1;
        return (int)(commDevice.AudioEndpointVolume.MasterVolumeLevelScalar * 100f);
    }

    public static void MicrophoneSet(int volume)
    {
        if (commDevice is null || commDevice.AudioEndpointVolume is null)
            return;
        commDevice.AudioEndpointVolume.MasterVolumeLevelScalar = (float)(volume / 100f);

    }

    public static bool? MicrophoneMuted()
    {
        if (commDevice is null || commDevice.AudioEndpointVolume is null)
            return null;
        return commDevice.AudioEndpointVolume.Mute;
    }

    public static bool? ToggleMicrophone()
    {
        var isMute = MicrophoneMuted();
        if (isMute is null)
            return null;

        commDevice.AudioEndpointVolume.Mute = !isMute.Value;
        return commDevice.AudioEndpointVolume.Mute;
    }

    public static void SubscribeToEvents(Action<SoundDirections, float, bool> EventHandler)
    {
        try
        {
            mmAudioEventHandler = EventHandler;
            enumerator.RegisterEndpointNotificationCallback(mmAudioNotificationClient);
            if (mmDevice is not null && mmDevice.AudioEndpointVolume is not null)
            {
                mmDevice.AudioEndpointVolume.OnVolumeNotification += (data) =>
                {
                    EventHandler?.Invoke(
                        SoundDirections.Output,
                        (float)Math.Round(data.MasterVolume * 100f),
                        data.Muted);
                };
            }

            if (commDevice is not null && commDevice.AudioEndpointVolume is not null)
            {
                commDevice.AudioEndpointVolume.OnVolumeNotification += (data) =>
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
        enumerator.UnregisterEndpointNotificationCallback(mmAudioNotificationClient);
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
                        var wasMuted = mmDevice.AudioEndpointVolume.Mute;
                        mmDevice.AudioEndpointVolume.OnVolumeNotification -= (data) => { };
                        mmDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                        if (mmDevice is not null && mmDevice.AudioEndpointVolume is not null)
                        {
                            mmDevice.AudioEndpointVolume.OnVolumeNotification += (data) =>
                            {
                                mmAudioEventHandler(
                                    SoundDirections.Output,
                                    (float)Math.Round(data.MasterVolume * 100f),
                                    data.Muted);
                            };
                            mmAudioEventHandler(
                                SoundDirections.Output,
                                (float)Math.Round(mmDevice.AudioEndpointVolume.MasterVolumeLevelScalar * 100f),
                                mmDevice.AudioEndpointVolume.Mute);
                            if (wasMuted != mmDevice.AudioEndpointVolume.Mute)
                                ToastManager.RunToast(
                                    mmDevice.AudioEndpointVolume.Mute ? Properties.Resources.Muted : Properties.Resources.Unmuted,
                                    mmDevice.AudioEndpointVolume.Mute ? ToastIcons.VolumeMute : ToastIcons.Volume);
                            Thread.Sleep(1000);
                        }
                    }
                    break;
                case DataFlow.Capture:
                    {
                        var wasMuted = mmDevice.AudioEndpointVolume.Mute;
                        commDevice.AudioEndpointVolume.OnVolumeNotification -= (data) => { };
                        commDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
                        if (commDevice is not null && commDevice.AudioEndpointVolume is not null)
                        {
                            commDevice.AudioEndpointVolume.OnVolumeNotification += (data) =>
                            {
                                mmAudioEventHandler(
                                    SoundDirections.Input,
                                    (float)Math.Round(data.MasterVolume * 100f),
                                    data.Muted);
                            };
                            mmAudioEventHandler(
                                SoundDirections.Input,
                                (float)Math.Round(commDevice.AudioEndpointVolume.MasterVolumeLevelScalar * 100f),
                                commDevice.AudioEndpointVolume.Mute);
                            if (wasMuted != commDevice.AudioEndpointVolume.Mute)
                                ToastManager.RunToast(
                                    commDevice.AudioEndpointVolume.Mute ? Properties.Resources.Muted : Properties.Resources.Unmuted,
                                    commDevice.AudioEndpointVolume.Mute ? ToastIcons.MicrophoneMute : ToastIcons.Microphone);
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
