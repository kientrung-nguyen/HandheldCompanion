using Newtonsoft.Json.Linq;
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace HandheldCompanion.Misc;

public enum PowerIndexType
{
    AC,
    DC
}

// For a reference on additional subgroup and setting GUIDs, run the command powercfg.exe /qh
// This will list all hidden settings with both the names and GUIDs, descriptions, current values, and allowed values.

public static class PowerSubGroup
{
    public static Guid SUB_PROCESSOR = new("54533251-82be-4824-96c1-47b60b740d00");
}

public static class PowerSetting
{
    public static Guid PERFBOOSTMODE = new("be337238-0d82-4146-a960-4f3749d470c7"); // Processor performance boost mode

    public static Guid PROCFREQMAX = new("75b0ae3f-bce0-45a7-8c89-c9611c25e100"); // Maximum processor frequency in MHz, 0 for no limit (default)
    public static Guid PROCFREQMAX1 = new("75b0ae3f-bce0-45a7-8c89-c9611c25e101");

    public static Guid PROCTHROTTLEMIN = new("893dee8e-2bef-41e0-89c6-b55d0929964c");
    public static Guid PROCTHROTTLEMIN1 = new("893dee8e-2bef-41e0-89c6-b55d0929964d");

    public static Guid PROCTHROTTLEMAX = new("bc5038f7-23e0-4960-96da-33abaf5935ec");

    public static Guid PROCTHROTTLEMAX1 = new("bc5038f7-23e0-4960-96da-33abaf5935ed");

    public static Guid
        CPMINCORES =
            new("0cc5b647-c1df-4637-891a-dec35c318583"); // Processor performance core parking min cores, expressed as a percent from 0 - 100

    public static Guid
        CPMAXCORES =
            new("ea062031-0e34-4ff1-9b6d-eb1059334028"); // Processor performance core parking max cores, expressed as a percent from 0 - 100

    public static Guid
        PERFEPP = new(
            "36687f9e-e3a5-4dbf-b1dc-15eb381c6863"); // Processor energy performance preference policy, expressed as a percent from 0 - 100

    public static Guid
        PERFEPP1 = new(
            "36687f9e-e3a5-4dbf-b1dc-15eb381c6864"); // Processor energy performance preference policy for Processor Power Efficiency Class 1, expressed as a percent from 0 - 100
}

public enum ErrorCode : uint
{
    SUCCESS = 0x000,
    FILE_NOT_FOUND = 0x002,
    ERROR_INVALID_PARAMETER = 0x057,
    ERROR_ALREADY_EXISTS = 0x0B7,
    MORE_DATA = 0x0EA,
    NO_MORE_ITEMS = 0x103
}


public enum PerfBoostMode
{
    Disabled = 0,
    Enabled = 1,
    Aggressive = 2,
    EfficientEnabled = 3,
    EfficientAggressive = 4,
    AggressiveAtGuaranteed = 5,
    EfficientAggressiveAtGuaranteed = 6
}

public static class PowerScheme
{
    // Wrapper for the actual PowerGetActiveScheme. Converts GUID to the built-in type on output and handles the LocalFree call.
    /// <summary>
    ///     Retrieves the active power scheme and returns a GUID that identifies the scheme.
    /// </summary>
    /// <param name="userRootPowerKey">This parameter is reserved for future use and must be set to zero.</param>
    /// <param name="activeSchemeGuid">A pointer that receives a GUID structure.</param>
    /// <returns>Returns zero if the call was successful, and a nonzero value if the call failed.</returns>
    private static uint PowerGetActiveScheme(nint userRootPowerKey, out Guid activeSchemeGuid)
    {
        var activeSchemeGuidPtr = IntPtr.Zero;
        activeSchemeGuid = Guid.Empty;

        var result = PowerGetActiveScheme(userRootPowerKey, out activeSchemeGuidPtr);
        if (result == (uint)ErrorCode.SUCCESS && activeSchemeGuidPtr != IntPtr.Zero)
        {
            activeSchemeGuid = (Guid)Marshal.PtrToStructure(activeSchemeGuidPtr, typeof(Guid));
            LocalFree(activeSchemeGuidPtr);
        }
        return result;
    }

    public static bool GetEffectiveOverlayScheme(out Guid EffectiveOverlayPolicyGuid)
    {
        return PowerGetEffectiveOverlayScheme(out EffectiveOverlayPolicyGuid) == 0;
    }

    public static bool SetActiveOverlayScheme(Guid overlaySchemeGuid)
    {
        return PowerSetActiveOverlayScheme(overlaySchemeGuid) == 0;
    }

    public static bool GetActiveScheme(out Guid activeSchemeGuid)
    {
        return PowerGetActiveScheme(nint.Zero, out activeSchemeGuid) == 0;
    }

    public static bool SetActiveScheme(Guid schemeGuid)
    {
        return PowerSetActiveScheme(nint.Zero, schemeGuid) == 0;
    }

    public static bool GetValue(PowerIndexType powerType, Guid schemeGuid, Guid subGroupOfPowerSettingsGuid,
        Guid powerSettingGuid, out uint value)
    {
        switch (powerType)
        {
            case PowerIndexType.AC:
                return PowerReadACValueIndex(nint.Zero, schemeGuid, subGroupOfPowerSettingsGuid, powerSettingGuid,
                    out value) == 0;
            case PowerIndexType.DC:
                return PowerReadDCValueIndex(nint.Zero, schemeGuid, subGroupOfPowerSettingsGuid, powerSettingGuid,
                    out value) == 0;
        }

        value = 0;
        return false;
    }

    public static bool SetValue(PowerIndexType powerType, Guid schemeGuid, Guid subGroupOfPowerSettingsGuid,
        Guid powerSettingGuid, uint value)
    {
        switch (powerType)
        {
            case PowerIndexType.AC:
                return PowerWriteACValueIndex(nint.Zero, schemeGuid, subGroupOfPowerSettingsGuid, powerSettingGuid,
                    value) == 0;
            case PowerIndexType.DC:
                return PowerWriteDCValueIndex(nint.Zero, schemeGuid, subGroupOfPowerSettingsGuid, powerSettingGuid,
                    value) == 0;
        }

        return false;
    }

    public static bool SetAttribute(Guid subGroupOfPowerSettingsGuid, Guid powerSettingGuid, bool hide)
    {
        return PowerWriteSettingAttributes(subGroupOfPowerSettingsGuid, powerSettingGuid, (uint)(hide ? 1 : 0)) == 0;
    }

    public static uint[] ReadPowerCfg(Guid subGroupGuid, Guid settingGuid)
    {
        var results = new uint[2];

        if (GetActiveScheme(out var currentSchemeGuid))
        {
            // read AC/DC values
            GetValue(PowerIndexType.AC, currentSchemeGuid, subGroupGuid, settingGuid,
                out results[(int)PowerIndexType.AC]);
            GetValue(PowerIndexType.DC, currentSchemeGuid, subGroupGuid, settingGuid,
                out results[(int)PowerIndexType.DC]);
        }

        return results;
    }

    public static void WritePowerCfg(Guid subGroupGuid, Guid settingGuid, uint ACValue, uint DCValue)
    {
        if (GetActiveScheme(out var schemeGuid))
        {
            // unhide attribute
            //SetAttribute(subGroupGuid, settingGuid, false);

            // set value(s)
            SetValue(PowerIndexType.AC, schemeGuid, subGroupGuid, settingGuid, ACValue);
            SetValue(PowerIndexType.DC, schemeGuid, subGroupGuid, settingGuid, DCValue);
            // activate scheme
            SetActiveScheme(schemeGuid);
        }
    }

    public static void WritePowerCfg(Guid subGroupGuid, Guid settingGuid, PowerIndexType powerIndex, uint value)
    {
        if (GetActiveScheme(out var schemeGuid))
        {
            // unhide attribute
            //SetAttribute(subGroupGuid, settingGuid, false);

            // set value
            SetValue(powerIndex, schemeGuid, subGroupGuid, settingGuid, value);

            // activate scheme
            SetActiveScheme(schemeGuid);
        }
    }


    #region imports

    /// <summary>
    ///     Retrieves the active power scheme and returns a GUID that identifies the scheme.
    /// </summary>
    /// <param name="UserRootPowerKey">This parameter is reserved for future use and must be set to zero.</param>
    /// <param name="ActivePolicyGuid">
    ///     A pointer that receives a pointer to a GUID structure. Use the LocalFree function to
    ///     free this memory.
    /// </param>
    /// <returns>Returns zero if the call was successful, and a nonzero value if the call failed.</returns>
    [DllImport("powrprof.dll", EntryPoint = "PowerGetActiveScheme")]
    private static extern uint PowerGetActiveScheme(nint UserRootPowerKey, out nint ActivePolicyGuid);

    /// <summary>
    ///     Sets the active power scheme for the current user.
    /// </summary>
    /// <param name="UserRootPowerKey">This parameter is reserved for future use and must be set to zero.</param>
    /// <param name="SchemeGuid">The identifier of the power scheme.</param>
    /// <returns>Returns zero if the call was successful, and a nonzero value if the call failed.</returns>
    [DllImport("powrprof.dll", EntryPoint = "PowerSetActiveScheme")]
    private static extern uint PowerSetActiveScheme(nint UserRootPowerKey, in Guid SchemeGuid);

    /// <summary>
    ///     Retrieves the active overlay power scheme and returns a GUID that identifies the scheme.
    /// </summary>
    /// <param name="EffectiveOverlayPolicyGuid">A pointer to a GUID structure.</param>
    /// <returns>Returns zero if the call was successful, and a nonzero value if the call failed.</returns>
    [DllImport("powrprof.dll", EntryPoint = "PowerGetEffectiveOverlayScheme")]
    private static extern uint PowerGetEffectiveOverlayScheme(out Guid EffectiveOverlayPolicyGuid);

    /// <summary>
    ///     Sets the active power overlay power scheme.
    /// </summary>
    /// <param name="OverlaySchemeGuid">The identifier of the overlay power scheme.</param>
    /// <returns>Returns zero if the call was successful, and a nonzero value if the call failed.</returns>
    [DllImport("powrprof.dll", EntryPoint = "PowerSetActiveOverlayScheme")]
    private static extern uint PowerSetActiveOverlayScheme(Guid OverlaySchemeGuid);


    /// <summary>
    ///     Retrieves the AC value index of the specified power setting.
    /// </summary>
    /// <param name="RootPowerKey">This parameter is reserved for future use and must be set to zero.</param>
    /// <param name="SchemeGuid">The identifier of the power scheme.</param>
    /// <param name="SubGroupOfPowerSettingsGuid">The subgroup of power settings.</param>
    /// <param name="PowerSettingGuid">The identifier of the power setting.</param>
    /// <param name="AcValueIndex">A pointer to a variable that receives the AC value index.</param>
    /// <returns>Returns zero if the call was successful, and a nonzero value if the call failed.</returns>
    [DllImport("powrprof.dll", EntryPoint = "PowerReadACValueIndex")]
    private static extern uint PowerReadACValueIndex(nint RootPowerKey, in Guid SchemeGuid,
        in Guid SubGroupOfPowerSettingsGuid, in Guid PowerSettingGuid, out uint AcValueIndex);

    /// <summary>
    ///     Retrieves the DC value index of the specified power setting.
    /// </summary>
    /// <param name="RootPowerKey">This parameter is reserved for future use and must be set to zero.</param>
    /// <param name="SchemeGuid">The identifier of the power scheme.</param>
    /// <param name="SubGroupOfPowerSettingsGuid">The subgroup of power settings.</param>
    /// <param name="PowerSettingGuid">The identifier of the power setting.</param>
    /// <param name="DcValueIndex">A pointer to a variable that receives the DC value index.</param>
    /// <returns>Returns zero if the call was successful, and a nonzero value if the call failed.</returns>
    [DllImport("powrprof.dll", EntryPoint = "PowerReadDCValueIndex")]
    private static extern uint PowerReadDCValueIndex(nint RootPowerKey, in Guid SchemeGuid,
        in Guid SubGroupOfPowerSettingsGuid, in Guid PowerSettingGuid, out uint DcValueIndex);

    /// <summary>
    ///     Sets the AC value index of the specified power setting.
    /// </summary>
    /// <param name="RootPowerKey">This parameter is reserved for future use and must be set to zero.</param>
    /// <param name="SchemeGuid">The identifier of the power scheme.</param>
    /// <param name="SubGroupOfPowerSettingsGuid">The subgroup of power settings.</param>
    /// <param name="PowerSettingGuid">The identifier of the power setting.</param>
    /// <param name="AcValueIndex">The AC value index.</param>
    /// <returns>Returns zero if the call was successful, and a nonzero value if the call failed.</returns>
    [DllImport("powrprof.dll", EntryPoint = "PowerWriteACValueIndex")]
    private static extern uint PowerWriteACValueIndex(nint RootPowerKey, in Guid SchemeGuid,
        in Guid SubGroupOfPowerSettingsGuid, in Guid PowerSettingGuid, uint AcValueIndex);

    /// <summary>
    ///     Sets the DC value index of the specified power setting.
    /// </summary>
    /// <param name="RootPowerKey">This parameter is reserved for future use and must be set to zero.</param>
    /// <param name="SchemeGuid">The identifier of the power scheme.</param>
    /// <param name="SubGroupOfPowerSettingsGuid">The subgroup of power settings.</param>
    /// <param name="PowerSettingGuid">The identifier of the power setting.</param>
    /// <param name="DcValueIndex">The DC value index.</param>
    /// <returns>Returns zero if the call was successful, and a nonzero value if the call failed.</returns>
    [DllImport("powrprof.dll", EntryPoint = "PowerWriteDCValueIndex")]
    private static extern uint PowerWriteDCValueIndex(nint RootPowerKey, in Guid SchemeGuid,
        in Guid SubGroupOfPowerSettingsGuid, in Guid PowerSettingGuid, uint DcValueIndex);

    /// <summary>
    ///     Frees the specified local memory object and invalidates its handle.
    /// </summary>
    /// <param name="hMem">A handle to the local memory object.</param>
    /// <returns>
    ///     If the function succeeds, the return value is zero, and if the function fails, the return value is equal to a
    ///     handle to the local memory object.
    /// </returns>
    [DllImport("kernel32.dll", EntryPoint = "LocalFree")]
    private static extern nint LocalFree(nint hMem);

    [DllImport("powrprof.dll", EntryPoint = "PowerWriteSettingAttributes")]
    private static extern uint PowerWriteSettingAttributes(in Guid SubGroupOfPowerSettingsGuid,
        in Guid PowerSettingGuid, uint Attributes);

    #endregion
}