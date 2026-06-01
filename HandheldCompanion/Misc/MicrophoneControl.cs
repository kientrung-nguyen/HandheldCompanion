using HandheldCompanion.Managers;
using HandheldCompanion.Shared;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using System;
using System.Threading;

namespace HandheldCompanion.Misc;

public static class MicrophoneControl
{
    private static MMDeviceEnumerator enumerator = new();
    private static MMDevice? commDevice;
    private static MicrophoneControlNotificationClient? commAudioNotificationClient;

    public static double Get()
    {
        if (commDevice is null || commDevice.AudioEndpointVolume is null)
            return -1;
        return Math.Round(commDevice.AudioEndpointVolume.MasterVolumeLevelScalar * 100d);
    }

    public static void Set(double volume)
    {
        if (commDevice is null || commDevice.AudioEndpointVolume is null)
            return;
        volume = Math.Clamp(volume, 0.0d, 100.0d);
        commDevice.AudioEndpointVolume.MasterVolumeLevelScalar = (float)(volume / 100d);

    }

    public static bool? IsMuted()
    {
        if (commDevice is null || commDevice.AudioEndpointVolume is null)
            return null;

        return commDevice.AudioEndpointVolume.Mute;
    }

    public static void Mute(bool mute)
    {
        if (commDevice is null || commDevice.AudioEndpointVolume is null)
            return;

        if (commDevice.AudioEndpointVolume.Mute == mute)
            return;

        commDevice.AudioEndpointVolume.Mute = mute;
    }

    public static bool? Toggle()
    {
        if (commDevice is null || commDevice.AudioEndpointVolume is null)
            return null;

        var isMute = IsMuted() ?? true;
        commDevice.AudioEndpointVolume.Mute = !isMute;
        return commDevice.AudioEndpointVolume.Mute;
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
            if (commDevice is not null && commDevice.AudioEndpointVolume is not null)
                commDevice.AudioEndpointVolume.OnVolumeNotification -= new AudioEndpointVolumeNotificationDelegate(eventHandler);

            if (commAudioNotificationClient == null)
            {
                commAudioNotificationClient = new MicrophoneControlNotificationClient(eventHandler);
                enumerator.RegisterEndpointNotificationCallback(commAudioNotificationClient);
            }

            commDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia);
            if (commDevice?.AudioEndpointVolume is not null)
                commDevice.AudioEndpointVolume.OnVolumeNotification += new AudioEndpointVolumeNotificationDelegate(eventHandler);
        }
        catch
        {
            LogManager.LogError("Can't connect to Audio Endpoint Volume events");
            throw;
        }
    }

    public static void Unsubscribe()
    {
        if (commAudioNotificationClient != null)
            enumerator.UnregisterEndpointNotificationCallback(commAudioNotificationClient);
    }

    private class MicrophoneControlNotificationClient : IMMNotificationClient
    {
        private readonly Action<AudioVolumeNotificationData> _eventHandler;
        public MicrophoneControlNotificationClient(Action<AudioVolumeNotificationData> eventHandler)
        {
            _eventHandler = eventHandler;
        }

        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
        {
            if ((flow != DataFlow.Render || role != Role.Multimedia) && (flow != DataFlow.Capture || role != Role.Communications))
                return;

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
