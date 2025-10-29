using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace HandheldCompanion.Utils;

public class DeviceUtils
{
    public enum SensorFamily
    {
        None = 0,
        Windows = 1,
        SerialUSBIMU = 2,
        Controller = 3
    }

    [Flags]
    public enum LEDLevel
    {
        None = 0,
        SolidColor = 1,
        Breathing = 2,
        Rainbow = 4,
        Wave = 8,
        Wheel = 16,
        Gradient = 32,
        Ambilight = 64,
        LEDPreset = 128,
    }

    public enum LEDDirection
    {
        Up,
        Down,
        Left,
        Right
    }

    public static USBDeviceInfo GetUSBDevice(string DeviceId)
    {
        try
        {
            using (var searcher =
                   new ManagementObjectSearcher(
                       $"SELECT * From Win32_PnPEntity WHERE DeviceId = '{DeviceId.Replace("\\", "\\\\")}'"))
            {
                var devices = searcher.Get().Cast<ManagementBaseObject>().ToList();
                return new USBDeviceInfo(devices.FirstOrDefault());
            }
        }
        catch
        {
        }

        return null;
    }

    public static List<USBDeviceInfo> GetSerialDevices()
    {
        var serials = new List<USBDeviceInfo>();
        try
        {
            using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_PnPEntity WHERE Name LIKE '%COM%' AND PNPClass = 'Ports'"))
            {
                var devices = searcher.Get().Cast<ManagementBaseObject>().ToList();
                foreach (var device in devices)
                    serials.Add(new USBDeviceInfo(device));
            }
        }
        catch { }

        return serials;
    }

    public static void RestartComputer()
    {
        Process.Start(new ProcessStartInfo("shutdown", "/r /f /t 0")
        {
            CreateNoWindow = true,
            UseShellExecute = false
        });
    }



    /// <summary>
    /// Retreives the local lan ip assigned to your pc in your LAN network
    /// usually in the form 192.168.XX.XX
    /// </summary>
    /// <returns></returns>
    public static IPAddress GetLANIP()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .ToList()
            .Select(iface => iface.GetIPProperties().UnicastAddresses
                .Where(addr => addr.Address.AddressFamily == AddressFamily.InterNetwork && addr.PrefixOrigin == PrefixOrigin.Dhcp)
            )
            .Where(list => list.Any())
            ?.FirstOrDefault()
            ?.FirstOrDefault()
            ?.Address ?? IPAddress.None;
    }

    /// <summary>
    /// Retrieves the primary network interface in your pc that you 
    /// use for internet, required for monitoring network bandwidths
    /// and speeds. The idea is that the interface that is used for internet
    /// has the local lan ip
    /// </summary>
    /// <returns></returns>
    public static NetworkInterface? GetPrimaryNetworkInterface()
    {
        IPAddress addr = GetLANIP();
        var interfaces = NetworkInterface.GetAllNetworkInterfaces().ToList();
        return interfaces.FirstOrDefault(iface => iface.GetIPProperties().UnicastAddresses.Select(ucast => ucast.Address).Contains(addr));
    }

}