using System;

namespace HandheldCompanion.Devices;

public class GPDWinMax2_2023_7840U : GPDWinMax2AMD
{
    public GPDWinMax2_2023_7840U()
    {
        // https://www.amd.com/en/products/apu/amd-ryzen-7-7840u
        GfxClock = [400, 2700];
        CpuClock = 5100;
    }

    public override float ReadFanDuty()
    {
        if (!IsOpen)
            return 0f;

        var value = ECRamReadByte(ECDetails.AddressFanDuty, ECDetails);
        return (float)(100f * (Convert.ToDouble(value) / ECDetails.FanValueMax));
    }

    public override float ReadFanSpeed()
    {
        try
        {
            var value = ECRAMReadLong(
                ECDetails.AddressFanRPMOffset, 
                ECDetails.AddressFanRPMLength, 
                ECDetails);
            return value;
        }
        catch { return float.NaN; }
    }
}