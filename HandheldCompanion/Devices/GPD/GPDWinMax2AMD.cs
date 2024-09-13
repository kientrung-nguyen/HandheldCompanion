using System.Collections.Generic;
using System.Numerics;

namespace HandheldCompanion.Devices;

public class GPDWinMax2AMD : GPDWinMax2
{
    public GPDWinMax2AMD()
    {
        // https://www.amd.com/fr/products/apu/amd-ryzen-7-6800u
        nTDP = [10, 15, 28];
        cTDP = [15, 28];
        GfxClock = [100, 2200];
        CpuClock = 4700;

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
    }
}