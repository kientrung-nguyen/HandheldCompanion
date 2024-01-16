using HandheldCompanion.Inputs;
using System.Collections.Generic;
using WindowsInput.Events;

namespace HandheldCompanion.Devices;

public class GPDWinMax2 : IDevice
{
    public GPDWinMax2()
    {
        // device specific settings
        ProductIllustration = "device_gpd_winmax2";

        // device specific capacities
        Capabilities = DeviceCapabilities.FanControl;

        ECDetails = new ECDetails
        {
            AddressFanControl = 0x275,
            AddressFanDuty = 0x1809,
            AddressStatusCommandPort = 0x4E,
            AddressDataPort = 0x4F,
            FanValueMin = 0,
            FanValueMax = 184, // FAN__RPMWRITE_MAX
            AddressFanRPMOffset = 0x218,
            AddressFanRPMLength = 2
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

        // Disabled this one as Win Max 2 also sends an Xbox guide input when Menu key is pressed.
        OEMChords.Add(new DeviceChord("Menu",
        new List<KeyCode> { KeyCode.LButton | KeyCode.XButton2 },
        new List<KeyCode> { KeyCode.LButton | KeyCode.XButton2 },
        true, ButtonFlags.OEM1
        ));

        // note, need to manually configured in GPD app
        OEMChords.Add(new DeviceChord("Bottom button left",
        new List<KeyCode> { KeyCode.F11, KeyCode.L },
        new List<KeyCode> { KeyCode.F11, KeyCode.L },
        false, ButtonFlags.OEM2
        ));

        OEMChords.Add(new DeviceChord("Bottom button right",
        new List<KeyCode> { KeyCode.F12, KeyCode.R },
        new List<KeyCode> { KeyCode.F12, KeyCode.R },
        false, ButtonFlags.OEM3
        ));
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
}