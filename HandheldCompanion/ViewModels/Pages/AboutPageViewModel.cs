using HandheldCompanion.Devices;
using HandheldCompanion.Managers;
using HandheldCompanion.Processors;
using HandheldCompanion.Views;
using Nefarius.Utilities.DeviceManagement.PnP;
using System;
using System.Windows.Media.Imaging;

namespace HandheldCompanion.ViewModels
{
    public class AboutPageViewModel : BaseViewModel
    {
        public string Manufacturer => IDevice.GetCurrent().ManufacturerName;
        public string ProductName => IDevice.GetCurrent().ProductName;
        public string SystemName => IDevice.GetCurrent().SystemName;
        public string MemoryName => $"{IDevice.GetCurrent().MemoryCapacity} GB {IDevice.GetCurrent().MemoryType} @ {IDevice.GetCurrent().MemorySpeed} MT/s";
        public string MemoryManufacturer => IDevice.GetCurrent().MemoryProduct;
        public string MemoryModel => IDevice.GetCurrent().MemoryModel;

        public string GraphicName => MotherboardInfo.GraphicName;
        public string GraphicDriverVersion => MotherboardInfo.GraphicDriverVersion;

        public string ProcessorName => string.Join(", ", [
            IDevice.GetCurrent().Processor,
            MotherboardInfo.ProcessorMaxClockSpeed + " MHz",
            IDevice.GetCurrent().NumberOfCores + " Core(s)",
            IDevice.GetCurrent().NumberOfLogicalProcessors + " Logical Processor(s)"
            ]);
        public string ProcessorManufacturer => IDevice.GetCurrent().ProcessorManufacturer;
        public int ProcessorNumberOfCores => IDevice.GetCurrent().NumberOfCores;
        public string BiosVersion => string.Join(" ", [
            IDevice.GetCurrent().BiosManufacturer,
            IDevice.GetCurrent().BiosName,
            IDevice.GetCurrent().BiosReleaseDate]);
        public string Version => MainWindow.CurrentVersion.ToString();

        public string InternalSensor => IDevice.GetCurrent().Capabilities.HasFlag(DeviceCapabilities.InternalSensor)
                ? IDevice.GetCurrent().InternalSensorName
                : "N/A";
        public string ExternalSensor => IDevice.GetCurrent().Capabilities.HasFlag(DeviceCapabilities.ExternalSensor)
                ? IDevice.GetCurrent().ExternalSensorName
                : "N/A";

        public bool IsUnsupportedDevice => IDevice.GetCurrent() is DefaultDevice;

        public BitmapImage DeviceImage
        {
            get
            {
                Uri uri = new Uri($"pack://application:,,,/Resources/DeviceImages/{IDevice.GetCurrent().ProductIllustration}.png");
                if (uri.IsFile)
                    return new(uri);

                return new();
            }
        }

        public AboutPageViewModel()
        {
            ManagerFactory.deviceManager.UsbDeviceArrived += GenericDeviceUpdated;
            ManagerFactory.deviceManager.UsbDeviceRemoved += GenericDeviceUpdated;
        }

        public override void Dispose()
        {
            ManagerFactory.deviceManager.UsbDeviceArrived -= GenericDeviceUpdated;
            ManagerFactory.deviceManager.UsbDeviceRemoved -= GenericDeviceUpdated;
            base.Dispose();
        }

        private void GenericDeviceUpdated(PnPDevice device, Guid IntefaceGuid)
        {
            // Update all bindings
            OnPropertyChanged(string.Empty);
        }
    }
}
