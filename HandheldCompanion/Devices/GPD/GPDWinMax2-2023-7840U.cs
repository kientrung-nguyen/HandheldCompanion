using HandheldCompanion.Managers;
using System;
using System.Configuration;

namespace HandheldCompanion.Devices;

public class GPDWinMax2_2023_7840U : GPDWinMax2AMD
{
    public GPDWinMax2_2023_7840U()
    {
        // https://www.amd.com/en/products/apu/amd-ryzen-7-7840u
        nTDP = new double[] { 15, 15, 28 };
        cTDP = new double[] { 5, 30 };
        GfxClock = new double[] { 400, 2700 };
        CpuClock = 5100;
    }

    public override float ReadFanDuty()
    {
        var value = ECRamReadByte(ECDetails.AddressFanDuty, ECDetails);
        return (float)(100f * (Convert.ToDouble(value) / ECDetails.FanValueMax));
    }

    public override int ReadFanSpeed()
    {
        try
        {
            LogManager.LogDebug("ReadFanSpeed");
            var value = ECRAMRead(ECDetails.AddressFanRPMOffset, ECDetails.AddressFanRPMLength, ECDetails);
            return value;
        }
        catch { return 0; }
    }
}