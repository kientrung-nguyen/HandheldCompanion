using System.Collections.Generic;
using System.Numerics;

namespace HandheldCompanion.Devices;

public class GPDWinMax2_2023_7640U : GPDWinMax2_2023_7840U
{
    public GPDWinMax2_2023_7640U()
    {
        // https://www.amd.com/en/products/apu/amd-ryzen-5-7640u
        GfxClock = new double[] { 200, 2600 };
        CpuClock = 4900;
    }
}