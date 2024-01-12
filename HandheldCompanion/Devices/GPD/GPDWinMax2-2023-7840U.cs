namespace HandheldCompanion.Devices;

public class GPDWinMax2_2023_7840U : GPDWinMax2AMD
{
    public GPDWinMax2_2023_7840U()
    {
        // https://www.amd.com/en/products/apu/amd-ryzen-7-7840u
        nTDP = new double[] { 15, 15, 28 };
        cTDP = new double[] { 5, 30 };
        GfxClock = new double[] { 800, 2700 };
        CpuClock = 5100;
    }
}