﻿using HandheldCompanion.Actions;
using HandheldCompanion.Devices.Lenovo;
using HandheldCompanion.Inputs;
using HandheldCompanion.Managers;
using HandheldCompanion.Misc;
using HidLibrary;
using Nefarius.Utilities.DeviceManagement.PnP;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using WindowsInput.Events;
using static HandheldCompanion.Devices.Lenovo.SapientiaUsb;
using static HandheldCompanion.Utils.DeviceUtils;

namespace HandheldCompanion.Devices;

public class LegionGo : IDevice
{
    public enum LegionMode
    {
        Quiet = 0x01,
        Balanced = 0x02,
        Performance = 0x03,
        Custom = 0xFF,
    }

    private FanTable fanTable = new();

    public const byte INPUT_HID_ID = 0x04;

    public override bool IsOpen => hidDevices.ContainsKey(INPUT_HID_ID) && hidDevices[INPUT_HID_ID].IsOpen;

    public LegionGo()
    {
        // device specific settings
        ProductIllustration = "device_legion_go";

        // used to monitor OEM specific inputs
        _vid = 0x17EF;
        _pid = 0x6182;

        // https://www.amd.com/en/products/apu/amd-ryzen-z1
        // https://www.amd.com/en/products/apu/amd-ryzen-z1-extreme
        // https://www.amd.com/en/products/apu/amd-ryzen-7-7840u
        nTDP = new double[] { 15, 15, 20 };
        cTDP = new double[] { 5, 30 };
        GfxClock = new double[] { 100, 2700 };
        CpuClock = 5100;

        GyrometerAxis = new Vector3(-1.0f, -1.0f, 1.0f);
        GyrometerAxisSwap = new SortedDictionary<char, char>
        {
            { 'X', 'X' },
            { 'Y', 'Z' },
            { 'Z', 'Y' }
        };

        AccelerometerAxis = new Vector3(-1.0f, 1.0f, -1.0f);
        AccelerometerAxisSwap = new SortedDictionary<char, char>
        {
            { 'X', 'X' },
            { 'Y', 'Z' },
            { 'Z', 'Y' }
        };

        // device specific capacities
        Capabilities |= DeviceCapabilities.None;
        // Capabilities |= DeviceCapabilities.FanControl;
        Capabilities |= DeviceCapabilities.DynamicLighting;
        Capabilities |= DeviceCapabilities.DynamicLightingBrightness;

        // dynamic lighting capacities
        DynamicLightingCapabilities |= LEDLevel.SolidColor;
        DynamicLightingCapabilities |= LEDLevel.Breathing;
        DynamicLightingCapabilities |= LEDLevel.Rainbow;
        DynamicLightingCapabilities |= LEDLevel.Wheel;

        // Legion Go - Quiet
        DevicePowerProfiles.Add(new(Properties.Resources.PowerProfileLegionGoQuietName, Properties.Resources.PowerProfileLegionGoQuietDescription) 
        {
            Default = true,
            DeviceDefault = true,
            OSPowerMode = OSPowerMode.BetterBattery,
            OEMPowerMode = (int) LegionMode.Quiet,
            Guid = new("961cc777-2547-4f9d-8174-7d86181b8a7a"),
            TDPOverrideEnabled = true,
            TDPOverrideValues = new[] { 8.0d, 8.0d, 8.0d }
        });

        // Legion Go - Balanced
        DevicePowerProfiles.Add(new(Properties.Resources.PowerProfileLegionGoBalancedName, Properties.Resources.PowerProfileLegionGoBalancedDescription)
        {
            Default = true,
            DeviceDefault = true,
            OSPowerMode = OSPowerMode.Recommended,
            OEMPowerMode = (int)LegionMode.Balanced,
            Guid = new("3af9B8d9-7c97-431d-ad78-34a8bfea439f"),
            TDPOverrideEnabled = true,
            TDPOverrideValues = new[] { 15.0d, 15.0d, 15.0d }
        });

        // Legion Go - Performance
        DevicePowerProfiles.Add(new(Properties.Resources.PowerProfileLegionGoPerformanceName, Properties.Resources.PowerProfileLegionGoPerformanceDescription)
        {
            Default = true,
            DeviceDefault = true,
            OSPowerMode = OSPowerMode.BestPerformance,
            OEMPowerMode = (int)LegionMode.Performance,
            Guid = new("ded574b5-45a0-4f42-8737-46345c09c238"),
            TDPOverrideEnabled = true,
            TDPOverrideValues = new[] { 20.0d, 20.0d, 20.0d }
        });

        PowerProfileManager.Applied += PowerProfileManager_Applied;

        OEMChords.Add(new DeviceChord("LegionR",
            new List<KeyCode>(), new List<KeyCode>(),
            false, ButtonFlags.OEM1
        ));

        OEMChords.Add(new DeviceChord("LegionL",
            new List<KeyCode>(), new List<KeyCode>(),
            false, ButtonFlags.OEM2
        ));

        // device specific layout
        DefaultLayout.AxisLayout[AxisLayoutFlags.RightPad] = new MouseActions {MouseType = MouseActionsType.Move, Filtering = true, Sensivity = 15 };

        DefaultLayout.ButtonLayout[ButtonFlags.RightPadClick] = new List<IActions>() { new MouseActions { MouseType = MouseActionsType.LeftButton, HapticMode = HapticMode.Down, HapticStrength = HapticStrength.Low } };
        DefaultLayout.ButtonLayout[ButtonFlags.RightPadClickDown] = new List<IActions>() { new MouseActions { MouseType = MouseActionsType.RightButton, HapticMode = HapticMode.Down, HapticStrength = HapticStrength.High } };
        DefaultLayout.ButtonLayout[ButtonFlags.B5] = new List<IActions>() { new ButtonActions { Button = ButtonFlags.R1 } };
        DefaultLayout.ButtonLayout[ButtonFlags.B6] = new List<IActions>() { new MouseActions { MouseType = MouseActionsType.MiddleButton } };
        DefaultLayout.ButtonLayout[ButtonFlags.B7] = new List<IActions>() { new MouseActions { MouseType = MouseActionsType.ScrollUp } };
        DefaultLayout.ButtonLayout[ButtonFlags.B8] = new List<IActions>() { new MouseActions { MouseType = MouseActionsType.ScrollDown } };

        Init();
    }

    public override void SetFanControl(bool enable, int mode = 0)
    {
        // do something
    }

    public void SetFanFullSpeed(bool enabled)
    {
        // Fan control: Default, Full (0, 1)
        ECRAMWrite(0x8A, (byte)(enabled ? 1 : 0));
    }

    public override void SetFanDuty(double percent)
    {
        // do something
    }

    private void PowerProfileManager_Applied(PowerProfile profile, UpdateSource source)
    {
        if (profile.FanProfile.FanMode == FanMode.Hardware)
            fanTable = new(new ushort[] { 44, 48, 55, 60, 71, 79, 87, 87, 100, 100 });
        else
            fanTable = new(new ushort[] {
                (ushort)profile.FanProfile.FanSpeeds[1],
                (ushort)profile.FanProfile.FanSpeeds[2],
                (ushort)profile.FanProfile.FanSpeeds[3],
                (ushort)profile.FanProfile.FanSpeeds[4],
                (ushort)profile.FanProfile.FanSpeeds[5],
                (ushort)profile.FanProfile.FanSpeeds[6],
                (ushort)profile.FanProfile.FanSpeeds[7],
                (ushort)profile.FanProfile.FanSpeeds[8],
                (ushort)profile.FanProfile.FanSpeeds[9],
                (ushort)profile.FanProfile.FanSpeeds[10],
            });

        try
        {
            // Fan control
            ManagementScope managementScope = new ManagementScope("root\\WMI");
            managementScope.Connect();
            ObjectQuery objectQuery = new ObjectQuery("SELECT * FROM LENOVO_FAN_METHOD");
            using (ManagementObjectCollection searcher = new ManagementObjectSearcher(managementScope, objectQuery).Get())
            {
                var obj = searcher.Cast<object>().FirstOrDefault();
                if (obj is ManagementObject mo)
                {
                    using (mo)
                    {
                        // Invoke the Fan_Set_Table method
                        var inParams = mo.GetMethodParameters("Fan_Set_Table");
                        inParams["FanTable"] = fanTable.GetBytes();
                        mo.InvokeMethod("Fan_Set_Table", inParams, null);

                        // Invoke the Fan_Get_Table method
                        inParams = mo.GetMethodParameters("Fan_Get_Table");
                        inParams["FanID"] = 1;
                        inParams["SensorID"] = 0;

                        ManagementBaseObject outParams = mo.InvokeMethod("Fan_Get_Table", inParams, null);

                        /* Read output
                        uint fanTableSize = (uint)outParams["FanTableSize"];
                        uint[] fanTableArray = (uint[])outParams["FanTable"];
                        uint sensorTableSize = (uint)outParams["SensorTableSize"];
                        uint[] sensorTableArray = (uint[])outParams["SensorTable"];
                        Debug.WriteLine("fanTable:{0}", string.Join(',', fanTable));
                        */
                    }
                }
            }
        } catch { }

        Task.Run(async () =>
        {
            // Power mode
            int GetSmartFanMode = -1;

            DateTime timeout = DateTime.Now.Add(TimeSpan.FromSeconds(4));
            while (DateTime.Now < timeout && GetSmartFanMode != profile.OEMPowerMode)
            {
                try
                {

                    ManagementScope managementScope = new ManagementScope("root\\WMI");
                    managementScope.Connect();
                    ObjectQuery objectQuery = new ObjectQuery("SELECT * FROM LENOVO_GAMEZONE_DATA");
                    using (ManagementObjectCollection searcher = new ManagementObjectSearcher(managementScope, objectQuery).Get())
                    {
                        var obj = searcher.Cast<object>().FirstOrDefault();
                        if (obj is ManagementObject mo)
                        {
                            using (mo)
                            {
                                // Update value
                                ManagementBaseObject param = mo.GetMethodParameters("SetSmartFanMode");
                                param["Data"] = profile.OEMPowerMode;
                                mo.InvokeMethod("SetSmartFanMode", param, null);

                                // Read output
                                GetSmartFanMode = Convert.ToInt32(mo.InvokeMethod("GetSmartFanMode", null, null)?.Properties["Data"].Value);
                            }
                        }
                    }
                }
                catch { }

                await Task.Delay(1000);
            }
        });
    }

    public override bool Open()
    {
        var success = base.Open();
        if (!success)
            return false;

        SetQuickLightingEffect(0, 1);
        SetQuickLightingEffect(3, 1);
        SetQuickLightingEffect(4, 1);
        SetQuickLightingEffectEnable(0, false);
        SetQuickLightingEffectEnable(3, false);
        SetQuickLightingEffectEnable(4, false);

        lightProfileL = GetCurrentLightProfile(3);
        lightProfileR = GetCurrentLightProfile(4);

        // Legion XInput controller and other Legion devices shares the same USBHUB
        while (ControllerManager.PowerCyclers.Count > 0)
            Thread.Sleep(500);

        return true;
    }

    public override void Close()
    {
        // restore default touchpad behavior
        SetTouchPadStatus(1);

        // close devices
        foreach (KeyValuePair<byte, HidDevice> hidDevice in hidDevices)
        {
            byte key = hidDevice.Key;
            HidDevice device = hidDevice.Value;

            device.CloseDevice();
        }

        // Reset the fan speed to default before device shutdown/restart
        SetFanFullSpeed(false);

        base.Close();
    }

    public override bool IsReady()
    {
        IEnumerable<HidDevice> devices = GetHidDevices(_vid, _pid, 0);
        foreach (HidDevice device in devices)
        {
            if (!device.IsConnected)
                continue;

            if (device.Capabilities.InputReportByteLength == 64)
                hidDevices[INPUT_HID_ID] = device;  // HID-compliant vendor-defined device
        }

        hidDevices.TryGetValue(INPUT_HID_ID, out HidDevice hidDevice);
        if (hidDevice is null || !hidDevice.IsConnected)
            return false;

        PnPDevice pnpDevice = PnPDevice.GetDeviceByInterfaceId(hidDevice.DevicePath);
        string device_parent = pnpDevice.GetProperty<string>(DevicePropertyKey.Device_Parent);

        PnPDevice pnpParent = PnPDevice.GetDeviceByInstanceId(device_parent);
        Guid parent_guid = pnpParent.GetProperty<Guid>(DevicePropertyKey.Device_ClassGuid);
        string parent_instanceId = pnpParent.GetProperty<string>(DevicePropertyKey.Device_InstanceId);

        return DeviceHelper.IsDeviceAvailable(parent_guid, parent_instanceId);
    }

    private LightionProfile lightProfileL = new();
    private LightionProfile lightProfileR = new();
    public override bool SetLedBrightness(int brightness)
    {
        lightProfileL.brightness = brightness;
        lightProfileR.brightness = brightness;

        SetLightingEffectProfileID(3, lightProfileL);
        SetLightingEffectProfileID(4, lightProfileR);

        return true;
    }

    public override bool SetLedStatus(bool status)
    {
        SetLightingEnable(0, status);

        return true;
    }

    public override bool SetLedColor(Color MainColor, Color SecondaryColor, LEDLevel level, int speed = 100)
    {
        // Speed is inverted for Legion Go
        lightProfileL.speed = 100 - speed;
        lightProfileR.speed = 100 - speed;

        // 1 - solid color
        // 2 - breathing
        // 3 - rainbow
        // 4 - spiral rainbow
        switch (level)
        {
            case LEDLevel.Breathing:
                {
                    lightProfileL.effect = 2;
                    lightProfileR.effect = 2;
                    SetLightProfileColors(MainColor, MainColor);
                }
                break;
            case LEDLevel.Rainbow:
                {
                    lightProfileL.effect = 3;
                    lightProfileR.effect = 3;
                }
                break;
            case LEDLevel.Wheel:
                {
                    lightProfileL.effect = 4;
                    lightProfileR.effect = 4;
                }
                break;
            default:
                {
                    lightProfileL.effect = 1;
                    lightProfileR.effect = 1;
                    SetLightProfileColors(MainColor, MainColor);
                }
                break;
        }

        SetLightingEffectProfileID(3, lightProfileL);
        SetLightingEffectProfileID(4, lightProfileR);

        return true;
    }

    private void SetLightProfileColors(Color MainColor, Color SecondaryColor)
    {
        lightProfileL.r = MainColor.R;
        lightProfileL.g = MainColor.G;
        lightProfileL.b = MainColor.B;

        lightProfileR.r = SecondaryColor.R;
        lightProfileR.g = SecondaryColor.G;
        lightProfileR.b = SecondaryColor.B;
    }

    public override string GetGlyph(ButtonFlags button)
    {
        switch (button)
        {
            case ButtonFlags.OEM1:
                return "\u2205";
            case ButtonFlags.OEM2:
                return "\uE004";
            case ButtonFlags.OEM3:
                return "\u2212";
            case ButtonFlags.OEM4:
                return "\u2213";
            case ButtonFlags.OEM5:
                return "\u2214";
            case ButtonFlags.OEM6:
                return "\u2215";
            case ButtonFlags.OEM7:
                return "\u2216";
            case ButtonFlags.OEM8:
                return "\u2217";
        }

        return defaultGlyph;
    }
}