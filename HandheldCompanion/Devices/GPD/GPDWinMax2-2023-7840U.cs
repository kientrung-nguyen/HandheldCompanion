using System.Collections.Generic;
using System.Numerics;

namespace HandheldCompanion.Devices;

public class GPDWinMax2_2023_7840U : GPDWinMax2
{
    public GPDWinMax2_2023_7840U()
    {
        // https://www.amd.com/en/products/apu/amd-ryzen-7-7840u
        nTDP = [15, 15, 28];
        cTDP = [5, 30];
        GfxClock = [200, 2700];
        CpuClock = 5100;
        Tjmax = 95;

        GyrometerAxis = new Vector3(1.0f, 1.0f, -1.0f);
        GyrometerAxisSwap = new SortedDictionary<char, char>
        {
            { 'X', 'Y' },
            { 'Y', 'Z' },
            { 'Z', 'X' }
        };

        AccelerometerAxis = new Vector3(1.0f, -1.0f, 1.0f);
        AccelerometerAxisSwap = new SortedDictionary<char, char>
        {
            { 'X', 'X' },
            { 'Y', 'Z' },
            { 'Z', 'Y' }
        };
    }
}