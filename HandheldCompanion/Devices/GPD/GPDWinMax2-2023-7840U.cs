using System;
using System.Collections.Generic;
using System.Numerics;

namespace HandheldCompanion.Devices;

public class GPDWinMax2_2023_7840U : GPDWinMax2
{
    public GPDWinMax2_2023_7840U()
    {
        // https://www.amd.com/en/products/processors/laptop/ryzen/7000-series/amd-ryzen-7-7840u.html
        nTDP = new double[] { 15, 15, 28 };
        cTDP = new double[] { 3, 28 };
        GfxClock = new double[] { 100, 2700 };
        CpuClock = 5200;

        GyrometerAxis = new Vector3(1.0f, -1.0f, -1.0f);
        GyrometerAxisSwap = new SortedDictionary<char, char>
        {
            { 'X', 'Y' },
            { 'Y', 'Z' },
            { 'Z', 'X' }
        };

        AccelerometerAxis = new Vector3(-1.0f, -1.0f, 1.0f);
        AccelerometerAxisSwap = new SortedDictionary<char, char>
        {
            { 'X', 'X' },
            { 'Y', 'Z' },
            { 'Z', 'Y' }
        };
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