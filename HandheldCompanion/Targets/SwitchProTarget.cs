using HandheldCompanion.Controllers;
using HandheldCompanion.Helpers;
using HandheldCompanion.Inputs;
using HandheldCompanion.Shared;
using HandheldCompanion.Utils;
using System.Numerics;
using System.Threading.Tasks;

namespace HandheldCompanion.Targets
{
    internal class SwitchProTarget : VIIPERTarget
    {
        // Button bitmask constants (uint32: byte0 | byte1<<8 | byte2<<16)
        // Byte 0 — right-side buttons (maps to HID report byte 3)
        private const uint ButtonY = 0x000001;
        private const uint ButtonX = 0x000002;
        private const uint ButtonB = 0x000004;
        private const uint ButtonA = 0x000008;
        private const uint ButtonR = 0x000040;
        private const uint ButtonZR = 0x000080;
        // Byte 1 — shared buttons (maps to HID report byte 4)
        private const uint ButtonMinus = 0x000100;
        private const uint ButtonPlus = 0x000200;
        private const uint ButtonRStick = 0x000400;
        private const uint ButtonLStick = 0x000800;
        private const uint ButtonHome = 0x001000;
        private const uint ButtonCapture = 0x002000;
        // Byte 2 — left-side buttons (maps to HID report byte 5)
        private const uint ButtonDDown = 0x010000;
        private const uint ButtonDUp = 0x020000;
        private const uint ButtonDRight = 0x040000;
        private const uint ButtonDLeft = 0x080000;
        private const uint ButtonL = 0x400000;
        private const uint ButtonZL = 0x800000;

        // IMU scaling — values placed into InputState are raw sensor units:
        //   Accel: SDL calibration expects 4096 LSB/g  (coeff=0x4000, formula: raw/4096 = g)
        //   Gyro:  SDL calibration expects 16.384 LSB/dps (coeff=0x3BF7, formula: raw*0.061 = dps)
        private const float AccelCountsPerG = 4096.0f;
        private const float GyroUnitsPerDps = 16.384f;

        // The Go VIIPER layer duplicates the same InputState gyro/accel values across all 3 IMU
        // frames in the 0x30 HID report. SDL fires one sensor event per frame, so the consumer
        // sees 3 identical events instead of 3 distinct ones — tripling the effective rate.
        // Divide gyro by 3 so the integrated sum over 3 frames equals the intended single-frame value.
        private const float GyroFrameDivider = 3.0f;

        private const int InputStateSize = 24; // kept for any future local reference
        protected override int InputLength => InputStateSize;

        protected override string DeviceType => "switchpro";
        public override int? MasterIntervalOverrideHz => 125;

        public SwitchProTarget(ushort vendorId, ushort productId) : base(vendorId, productId)
        {
            HID = HIDmode.SwitchProController;
            _reportBuffer = new byte[InputLength];
            LogManager.LogInformation("{0} initialized for VIIPER ({1:X4}:{2:X4})", ToString(), vendorId, productId);
        }

        public override Task UpdateInputsAsync(ControllerState Inputs, GamepadMotion gamepadMotion)
        {
            UpdateInputs(Inputs, gamepadMotion);
            return Task.CompletedTask;
        }

        protected override byte[] BuildReport(ControllerState inputs, GamepadMotion gamepadMotion)
        {
            // --- Buttons ---
            uint buttons = 0;

            // Right-side buttons (byte 0 of bitmask)
            if (inputs.ButtonState[ButtonFlags.B1]) buttons |= ButtonB;
            if (inputs.ButtonState[ButtonFlags.B2]) buttons |= ButtonA;
            if (inputs.ButtonState[ButtonFlags.B3]) buttons |= ButtonY;
            if (inputs.ButtonState[ButtonFlags.B4]) buttons |= ButtonX;
            if (inputs.ButtonState[ButtonFlags.R1]) buttons |= ButtonR;
            if (inputs.AxisState[AxisFlags.R2] > 0) buttons |= ButtonZR;

            // Shared buttons (byte 1 of bitmask)
            if (inputs.ButtonState[ButtonFlags.Back]) buttons |= ButtonMinus;
            if (inputs.ButtonState[ButtonFlags.Start]) buttons |= ButtonPlus;
            if (inputs.ButtonState[ButtonFlags.RightStickClick]) buttons |= ButtonRStick;
            if (inputs.ButtonState[ButtonFlags.LeftStickClick]) buttons |= ButtonLStick;
            if (inputs.ButtonState[ButtonFlags.Special]) buttons |= ButtonHome;
            if (inputs.ButtonState[ButtonFlags.Special2]) buttons |= ButtonCapture;

            // Left-side buttons (byte 2 of bitmask)
            if (inputs.ButtonState[ButtonFlags.DPadDown]) buttons |= ButtonDDown;
            if (inputs.ButtonState[ButtonFlags.DPadUp]) buttons |= ButtonDUp;
            if (inputs.ButtonState[ButtonFlags.DPadRight]) buttons |= ButtonDRight;
            if (inputs.ButtonState[ButtonFlags.DPadLeft]) buttons |= ButtonDLeft;
            if (inputs.ButtonState[ButtonFlags.L1]) buttons |= ButtonL;
            if (inputs.AxisState[AxisFlags.L2] > 0) buttons |= ButtonZL;

            // --- Sticks ---
            short lx = (short)inputs.AxisState[AxisFlags.LeftStickX];
            short ly = (short)inputs.AxisState[AxisFlags.LeftStickY];
            short rx = (short)inputs.AxisState[AxisFlags.RightStickX];
            short ry = (short)inputs.AxisState[AxisFlags.RightStickY];

            // --- IMU ---
            short gyroX = 0, gyroY = 0, gyroZ = 0;
            short accelX = 0, accelY = 0, accelZ = 0;

            Vector3 gyro;
            Vector3 accel;
            if (gamepadMotion is not null)
            {
                gamepadMotion.GetRawGyro(out gyro.X, out gyro.Y, out gyro.Z);
                gamepadMotion.GetRawAcceleration(out accel.X, out accel.Y, out accel.Z);
            }
            else
            {
                gyro = inputs.GyroState.GetGyroscope(GyroState.SensorState.DSU);
                accel = inputs.GyroState.GetAccelerometer(GyroState.SensorState.DSU);
            }

            gyroX = (short)(-gyro.Z * GyroUnitsPerDps / GyroFrameDivider);
            gyroY = (short)(-gyro.X * GyroUnitsPerDps / GyroFrameDivider);
            gyroZ = (short)(gyro.Y * GyroUnitsPerDps / GyroFrameDivider);
            accelX = (short)(-accel.Z * AccelCountsPerG);
            accelY = (short)(-accel.X * AccelCountsPerG);
            accelZ = (short)(accel.Y * AccelCountsPerG);

            byte[] data = _reportBuffer;
            WriteU32(data, 0, buttons);
            WriteI16(data, 4, lx);
            WriteI16(data, 6, ly);
            WriteI16(data, 8, rx);
            WriteI16(data, 10, ry);
            WriteI16(data, 12, gyroX);
            WriteI16(data, 14, gyroY);
            WriteI16(data, 16, gyroZ);
            WriteI16(data, 18, accelX);
            WriteI16(data, 20, accelY);
            WriteI16(data, 22, accelZ);
            return data;
        }

        // The Go layer handles all subcommand/USB/HID protocol internally.
        // Feedback arrives as a 2-byte OutputState: [0]=RumbleLeft, [1]=RumbleRight.
        protected override void HandleOutput(byte[] buffer)
        {
            if (buffer is null || buffer.Length < 2)
                return;

            SendVibrate(buffer[0], buffer[1]);
        }

        private static void WriteU32(byte[] buf, int offset, uint value)
        {
            buf[offset] = (byte)(value & 0xFF);
            buf[offset + 1] = (byte)((value >> 8) & 0xFF);
            buf[offset + 2] = (byte)((value >> 16) & 0xFF);
            buf[offset + 3] = (byte)((value >> 24) & 0xFF);
        }

        private static void WriteI16(byte[] buf, int offset, short value)
        {
            buf[offset] = (byte)(value & 0xFF);
            buf[offset + 1] = (byte)((value >> 8) & 0xFF);
        }
    }
}
