using HandheldCompanion.Managers;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;

namespace HandheldCompanion
{
    // https://learn.microsoft.com/en-us/windows-hardware/drivers/devtest/pnputil-return-values
    public class PnPUtilResult
    {
        public int ExitCode;
        public string StandardOutput;

        public PnPUtilResult(int exitCode, string output)
        {
            ExitCode = exitCode;
            StandardOutput = output;
        }
    }

    public class PnPUtilDevice
    {
        public string InstanceID;
        public string Description;
        public string ClassName;
        public string ClassGUID;
        public string ManufacturerName;
        public string Status;
        public string DriverName;
    }

    public static class PnPUtil
    {
        private const int ERROR_SUCCESS = 0;
        private const int ERROR_NO_MORE_ITEMS = 259;
        private const int ERROR_SUCCESS_REBOOT_REQUIRED = 3010;
        private const int ERROR_SUCCESS_REBOOT_INITIATED = 1641;

        public static bool RestartDevice(string InstanceId)
        {
            var pnpResult = GetPnPUtilResult($"/restart-device \"{InstanceId}\"");
            return ValidateChangeDeviceStatusResult(InstanceId, pnpResult);
        }

        public static bool EnableDevice(string InstanceId)
        {
            var pnpResult = GetPnPUtilResult($"/enable-device \"{InstanceId}\"");
            return ValidateChangeDeviceStatusResult(InstanceId, pnpResult);
        }

        public static bool DisableDevice(string InstanceId)
        {
            var pnpResult = GetPnPUtilResult($"/disable-device \"{InstanceId}\"");
            return ValidateChangeDeviceStatusResult(InstanceId, pnpResult);
        }

        public static bool EnableDevices(string Class)
        {
            var pnpResult = GetPnPUtilResult($"/enable-device /class \"{Class}\"");
            return pnpResult.ExitCode == ERROR_SUCCESS;
        }

        public static List<string> GetDevices(string className, string status = "/connected")
        {
            // A list of string to store the Instance ID values
            List<string> instanceIDs = new List<string>();

            // A regular expression to match the Instance ID pattern
            Regex regex = new Regex(@"Instance ID:\s+(.*)");

            // Loop through each line of the input string
            string input = GetPnPUtilOutput($"/enum-devices {status} /class {className}");
            foreach (string line in input.Split('\r'))
            {
                // Try to match the line with the regular expression
                Match match = regex.Match(line);

                // If there is a match, add the Instance ID value to the list
                if (match.Success)
                {
                    instanceIDs.Add(match.Groups[1].Value);
                }
            }

            // Print the list of Instance ID values
            Debug.WriteLine("The Instance ID values are:");
            foreach (string id in instanceIDs)
            {
                Debug.WriteLine(id);
            }

            return instanceIDs;
        }

        public static List<PnPUtilDevice> GetDeviceDetails(string className, string status = "/connected")
        {
            var devices = new List<PnPUtilDevice>();
            var pattern = @"Instance ID:\s+(?<InstanceID>[^\r\n]+)\r?\n" +
                         @"Device Description:\s+(?<DeviceDescription>[^\r\n]+)\r?\n" +
                         @"Class Name:\s+(?<ClassName>[^\r\n]+)\r?\n" +
                         @"Class GUID:\s+(?<ClassGUID>[^\r\n]+)\r?\n" +
                         @"Manufacturer Name:\s+(?<ManufacturerName>[^\r\n]+)\r?\n" +
                         @"Status:\s+(?<Status>[^\r\n]+)\r?\n" +
                         @"Driver Name:\s+(?<DriverName>[^\r\n]+)";
            Regex regex = new Regex(pattern);

            // Loop through each line of the input string
            //
            string input = GetPnPUtilOutput($"/enum-devices {status} /class {className}");

            // Try to match the line with the regular expression
            //
            var matches = regex.Matches(input);
            foreach (Match match in matches)
            {
                // If there is a match, add the Instance ID value to the list
                devices.Add(new PnPUtilDevice
                {
                    InstanceID = match.Groups["InstanceID"].Value,
                    Description = match.Groups["DeviceDescription"].Value,
                    ClassName = match.Groups["ClassName"].Value,
                    ClassGUID = match.Groups["ClassGUID"].Value,
                    ManufacturerName = match.Groups["ManufacturerName"].Value,
                    Status = match.Groups["Status"].Value,
                    DriverName = match.Groups["DriverName"].Value
                });

            }
            return devices;
        }

        public static Process StartPnPUtil(string arguments)
        {
            Process process = new();

            process.StartInfo.FileName = "pnputil.exe";
            process.StartInfo.Arguments = arguments;

            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.CreateNoWindow = true;

            process.Start();

            return process;
        }

        private static string GetPnPUtilOutput(string arguments)
        {
            Process process = StartPnPUtil(arguments);
            var output = process.StandardOutput.ReadToEnd();

            return output;
        }

        private static PnPUtilResult GetPnPUtilResult(string arguments)
        {
            Process process = StartPnPUtil(arguments);
            var output = process.StandardOutput.ReadToEnd();
            var exitCode = process.ExitCode;

            return new PnPUtilResult(exitCode, output);
        }

        // this function validates the results for /enable-device, /disable-device and /restart-device.
        private static bool ValidateChangeDeviceStatusResult(string instanceId, PnPUtilResult pnpResult)
        {
            string[] output = pnpResult.StandardOutput.Split("\r\n");
            var exitCode = pnpResult.ExitCode;

            switch (exitCode)
            {
                case ERROR_SUCCESS:
                    if (output[2].Contains(instanceId))
                        // we assume the operation was successful if the instance id was found
                        return true;
                    break;
                default:
                    // operation was not successful or requires a reboot.
                    return false;
            }

            return true;
        }
    }
}
