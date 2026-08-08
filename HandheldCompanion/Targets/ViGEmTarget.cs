using HandheldCompanion.Controllers;
using HandheldCompanion.Helpers;
using HandheldCompanion.Managers;
using HandheldCompanion.Shared;
using HandheldCompanion.Utils;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Exceptions;
using System;
using System.Threading;

namespace HandheldCompanion.Targets
{
    public abstract class ViGEmTarget : IDisposable
    {
        public HIDmode HID = HIDmode.NoController;

        protected IVirtualGamepad? virtualController;

        // Retry policy for VigemDeviceNotReadyException (driver not yet ready after resume)
        private const int ConnectMaxAttempts = 5;
        private static readonly TimeSpan ConnectRetryDelay = TimeSpan.FromSeconds(2);

        public event ConnectedEventHandler? Connected;
        public delegate void ConnectedEventHandler(ViGEmTarget target);

        public event DisconnectedEventHandler? Disconnected;
        public delegate void DisconnectedEventHandler(ViGEmTarget target);

        public event VibratedEventHandler? Vibrated;
        public delegate void VibratedEventHandler(byte LargeMotor, byte SmallMotor);

        public event ConnectStatusChangedEventHandler? StatusChanged;
        public delegate void ConnectStatusChangedEventHandler(ViGEmTarget target, VirtualManagerStatus status, int attempt, int maxAttempts);

        protected void RaiseConnected() => Connected?.Invoke(this);
        protected void RaiseDisconnected() => Disconnected?.Invoke(this);

        public bool IsConnected = false;

        public virtual int? MasterIntervalOverrideHz => null;
        private bool _disposed = false;

        ~ViGEmTarget()
        {
            Dispose(false);
        }

        public override string ToString()
        {
            return EnumUtils.GetDescriptionFromEnumValue(HID);
        }

        protected virtual void SendVibrate(byte LargeMotor, byte SmallMotor)
        {
            Vibrated?.Invoke(LargeMotor, SmallMotor);
        }

        public virtual bool Connect()
        {
            if (IsConnected)
                return true;

            string failureReason = "unknown error";

            for (int attempt = 1; attempt <= ConnectMaxAttempts; attempt++)
            {
                try
                {
                    virtualController?.Connect();

                    IsConnected = true;
                    Connected?.Invoke(this);
                    StatusChanged?.Invoke(this, VirtualManagerStatus.Connected, attempt, ConnectMaxAttempts);
                    LogManager.LogInformation("{0} connected", ToString());
                    return true;
                }
                catch (VigemDeviceNotReadyException) when (attempt < ConnectMaxAttempts)
                {
                    // Driver stack not ready yet (common on system resume) — retry after a delay
                    LogManager.LogWarning("{0} connect attempt {1}/{2} failed: device not ready, retrying in {3}s…", ToString(), attempt, ConnectMaxAttempts, ConnectRetryDelay.TotalSeconds);
                    StatusChanged?.Invoke(this, VirtualManagerStatus.Retrying, attempt, ConnectMaxAttempts);
                    Thread.Sleep(ConnectRetryDelay);
                }
                catch (VigemDeviceNotReadyException ex)
                {
                    // Exhausted all retries
                    failureReason = $"device not ready after {ConnectMaxAttempts} attempts. {ex.Message}";
                    break;
                }
                catch (VigemAlreadyConnectedException)
                {
                    // Target is already on the bus — treat as success
                    IsConnected = true;
                    Connected?.Invoke(this);
                    StatusChanged?.Invoke(this, VirtualManagerStatus.Connected, attempt, ConnectMaxAttempts);
                    LogManager.LogInformation("{0} already connected, treating as success", ToString());
                    return true;
                }
                catch (VigemCallbackAlreadyRegisteredException)
                {
                    // Notification callback already registered — treat as success
                    IsConnected = true;
                    Connected?.Invoke(this);
                    StatusChanged?.Invoke(this, VirtualManagerStatus.Connected, attempt, ConnectMaxAttempts);
                    LogManager.LogInformation("{0} callback already registered, treating as success", ToString());
                    return true;
                }
                catch (VigemNoFreeSlotException ex)
                {
                    // All XInput slots are occupied — nothing we can do, disable
                    failureReason = $"no free XInput slot available. {ex.Message}";
                    break;
                }
                catch (VigemBusNotFoundException ex)
                {
                    // ViGEmBus is not running — fatal
                    failureReason = $"ViGEm bus not found. {ex.Message}";
                    break;
                }
                catch (VigemTargetUninitializedException ex)
                {
                    // Target handle is invalid — should not happen under normal circumstances
                    failureReason = $"target uninitialized. {ex.Message}";
                    break;
                }
                catch (VigemInvalidTargetException ex)
                {
                    // Target became invalid between add and notification registration
                    failureReason = $"invalid target. {ex.Message}";
                    break;
                }
                catch (Exception ex)
                {
                    // Unexpected native or other error
                    failureReason = ex.Message;
                    break;
                }
            }

            LogManager.LogWarning("Failed to connect {0}: {1}", ToString(), failureReason);
            StatusChanged?.Invoke(this, VirtualManagerStatus.Failed, ConnectMaxAttempts, ConnectMaxAttempts);
            ManagerFactory.settingsManager.SetProperty("HIDstatus", 0);
            return false;
        }

        public virtual bool Disconnect()
        {
            if (!IsConnected)
                return false;

            string? failureReason = null;

            try
            {
                virtualController?.Disconnect();
            }
            catch (VigemTargetNotPluggedInException)
            {
                // Target was already removed from the bus — effectively disconnected, treat as success
                LogManager.LogInformation("{0} was already unplugged from bus, treating disconnect as success", ToString());
            }
            catch (VigemBusNotFoundException)
            {
                // Bus is gone entirely — device is effectively disconnected, treat as success
                LogManager.LogInformation("{0} disconnect: bus not found, device is already offline", ToString());
            }
            catch (VigemRemovalFailedException ex)
            {
                // Driver refused removal but there is nothing we can do — log and proceed
                LogManager.LogWarning("{0} removal failed, forcing disconnected state. {1}", ToString(), ex.Message);
            }
            catch (VigemTargetUninitializedException ex)
            {
                // Handle invalid — log and proceed
                LogManager.LogWarning("{0} disconnect: target uninitialized. {1}", ToString(), ex.Message);
            }
            catch (Exception ex)
            {
                failureReason = ex.Message;
            }

            if (failureReason is not null)
            {
                LogManager.LogWarning("Failed to disconnect {0}. {1}", ToString(), failureReason);
                ManagerFactory.settingsManager.SetProperty("HIDstatus", 1);
                return false;
            }

            IsConnected = false;
            Disconnected?.Invoke(this);
            LogManager.LogInformation("{0} disconnected", ToString());

            return true;
        }

        public virtual void UpdateInputs(ControllerState inputs, GamepadMotion gamepadMotion)
        { }

        public virtual unsafe void UpdateReport(long ticks, float delta)
        { }

        public virtual void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
            {
                Disconnect();
                Connected = null;
                Disconnected = null;
                Vibrated = null;
                StatusChanged = null;
            }

            _disposed = true;
        }
    }
}