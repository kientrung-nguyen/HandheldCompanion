using HandheldCompanion.Managers;
using HandheldCompanion.Shared;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using System;

namespace HandheldCompanion.Misc;


public static class AudioControl
{
    private static MMDeviceEnumerator enumerator = new();
    private static MMDevice? mmDevice;
    private static AudioControlNotificationClient? mmAudioNotificationClient;

    public static double Get()
    {
        if (mmDevice is null || mmDevice.AudioEndpointVolume is null)
            return -1;
        return Math.Round(mmDevice.AudioEndpointVolume.MasterVolumeLevelScalar * 100d);
    }

    public static void Set(double volume)
    {
        if (mmDevice is null || mmDevice.AudioEndpointVolume is null)
            return;
        volume = Math.Clamp(volume, 0.0d, 100.0d);
        mmDevice.AudioEndpointVolume.MasterVolumeLevelScalar = (float)(volume / 100d);

    }

    public static bool? IsMuted()
    {
        if (mmDevice is null || mmDevice.AudioEndpointVolume is null)
            return null;
        return mmDevice.AudioEndpointVolume.Mute;
    }

    public static void Mute(bool mute)
    {
        if (mmDevice is null || mmDevice.AudioEndpointVolume is null)
            return;
        if (mmDevice.AudioEndpointVolume.Mute == mute)
            return;
        mmDevice.AudioEndpointVolume.Mute = mute;
    }

    public static bool? Toggle()
    {
        if (mmDevice is null || mmDevice.AudioEndpointVolume is null)
            return null;

        var isMute = IsMuted() ?? true;
        mmDevice.AudioEndpointVolume.Mute = !isMute;
        return mmDevice.AudioEndpointVolume.Mute;
    }

    public static double Adjust(int delta)
    {
        var volume = Get();
        volume = Math.Min(100, Math.Max(0, volume + delta));
        Set(volume);
        return volume;
    }

    public static void SubscribeToEvents(Action<AudioVolumeNotificationData> eventHandler)
    {
        try
        {
            if (mmDevice is not null && mmDevice.AudioEndpointVolume is not null)
                mmDevice.AudioEndpointVolume.OnVolumeNotification -= new AudioEndpointVolumeNotificationDelegate(eventHandler);

            if (mmAudioNotificationClient == null)
            {
                mmAudioNotificationClient = new AudioControlNotificationClient(eventHandler);
                enumerator.RegisterEndpointNotificationCallback(mmAudioNotificationClient);
            }

            mmDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            if (mmDevice?.AudioEndpointVolume is not null)
            {
                mmDevice.AudioEndpointVolume.OnVolumeNotification += new AudioEndpointVolumeNotificationDelegate(eventHandler);
                eventHandler?.Invoke(new AudioVolumeNotificationData(
                    Guid.NewGuid(),
                    mmDevice.AudioEndpointVolume.Mute,
                    mmDevice.AudioEndpointVolume.MasterVolumeLevelScalar,
                    [],
                    Guid.NewGuid()
                    ));
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
        if (mmAudioNotificationClient != null)
            enumerator.UnregisterEndpointNotificationCallback(mmAudioNotificationClient);
    }

    private class AudioControlNotificationClient : IMMNotificationClient
    {
        private readonly Action<AudioVolumeNotificationData> _eventHandler;
        public AudioControlNotificationClient(Action<AudioVolumeNotificationData> eventHandler)
        {
            _eventHandler = eventHandler;
        }

        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
        {
            if ((flow != DataFlow.Render || role != Role.Multimedia) && (flow != DataFlow.Capture || role != Role.Communications))
                return;
            var mmDevice = enumerator.GetDevice(defaultDeviceId);
            ToastManager.SendToast($"Audio device changed", $"Audio changed to {mmDevice?.FriendlyName}");
            SubscribeToEvents(_eventHandler);
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
