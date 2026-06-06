using HandheldCompanion.Commands.Functions.HC;
using HandheldCompanion.Inputs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using WindowsInput.Events;

namespace HandheldCompanion.Devices;

public class GPDWinMax2 : IDevice
{
    public GPDWinMax2()
    {
        // device specific settings
        ProductIllustration = "device_gpd_winmax2";
        UseOpenLib = true;

        // device specific capacities
        Capabilities = DeviceCapabilities.FanControl;

        ECDetails = new ECDetails
        {
            AddressStatusCommandPort = 0x4E,
            AddressDataPort = 0x4F,
            AddressFanControl = 0x275,
            AddressFanDuty = 0x1809,
            AddressFanRPM = 0x218,
            FanValueMin = 0,
            FanValueMax = 184, // FAN__RPMWRITE_MAX
            FanRPMMax = 4968, // FAN__RPMVALUE_MAX
            FanRPMLength = 2
            // 4968 FAN_RPMVALUE_MAX
            // "FAN_RAM_RPMREAD_OFFSET":0x218,
            // "FAN_RAM_RPMREAD_LENGTH":2,

            /*
            FAN_EC_CONFIG=[{
            "FAN_RAM_REG_ADDR":0x4E,
            "FAN_RAM_REG_DATA":0x4F,
            "FAN_RAM_MANUAL_OFFSET":0x275,
            "FAN_RAM_RPMWRITE_OFFSET":0x1809,
            "FAN_RAM_RPMREAD_OFFSET":0x218,
            "FAN_RAM_RPMREAD_LENGTH":2,

            "FAN_RPMWRITE_MAX":184,
            "FAN_RPMVALUE_MAX":4968
            }]
            */
        };

        GyrometerAxis = new Vector3(1.0f, -1.0f, -1.0f);
        GyrometerAxisSwap = new SortedDictionary<char, char>
        {
            { 'X', 'Y' },
            { 'Y', 'Z' },
            { 'Z', 'X' }
        };
        AccelerometerAxis = new Vector3(-1.0f, 1.0f, 1.0f);
        AccelerometerAxisSwap = new SortedDictionary<char, char>
        {
            { 'X', 'X' },
            { 'Y', 'Z' },
            { 'Z', 'Y' }
        };

        // Disabled this one as Win Max 2 also sends an Xbox guide input when Menu key is pressed.
        OEMChords.Add(new KeyboardChord("Menu",
            [KeyCode.LButton | KeyCode.XButton2],
            [KeyCode.LButton | KeyCode.XButton2],
            true, ButtonFlags.OEM1
        ));

        // note, need to manually configured in GPD app
        OEMChords.Add(new KeyboardChord("Bottom button left",
            [KeyCode.F11, KeyCode.L],
            [KeyCode.F11, KeyCode.L],
            false, ButtonFlags.OEM2
        ));

        OEMChords.Add(new KeyboardChord("Bottom button right",
            [KeyCode.F12, KeyCode.R],
            [KeyCode.F12, KeyCode.R],
            false, ButtonFlags.OEM3
        ));

        // prepare hotkeys
        DeviceHotkeys[typeof(MainWindowCommands)].inputsChord.ButtonState[ButtonFlags.Special] = true;
        DeviceHotkeys[typeof(MainWindowCommands)].InputsChordType = InputsChordType.Long;
        DeviceHotkeys[typeof(QuickToolsCommands)].inputsChord.ButtonState[ButtonFlags.Special] = true;
    }

    public override string GetGlyph(ButtonFlags button)
    {
        switch (button)
        {
            case ButtonFlags.OEM2:
                return "\u220E";
            case ButtonFlags.OEM3:
                return "\u220F";
        }

        return defaultGlyph;
    }

    public override void SetFanControl(bool enable, int mode = 0)
    {
        if (ECDetails.AddressFanControl == 0)
            return;

        if (!UseOpenLib || !IsOpen)
            return;

        var data = Convert.ToByte(enable);
        ECRamDirectWriteByte(ECDetails.AddressFanControl, ECDetails, data);
    }

    public override float ReadFanDuty()
    {
        if (ECDetails.AddressFanControl == 0)
            return 0;


        if (!UseOpenLib || !IsOpen)
            return 0;


        var value = ECRamDirectReadByte(ECDetails.AddressFanDuty, ECDetails);
        return (float)(100f * (Convert.ToDouble(value) / ECDetails.FanValueMax));
    }

    public override float ReadFanSpeed()
    {
        try
        {
            var sum = 0L;
            foreach (var len in Enumerable.Range(0, ECDetails.FanRPMLength))
            {
                var value = ECRamDirectReadByte((ushort)(ECDetails.AddressFanRPM + len), ECDetails);
                sum = (sum << 8) + value;
            }
            return sum;
        }
        catch { return 0; }
    }
}