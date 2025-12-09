using RTLSDRCore.Enums;
using RTLSDRCore.Models;
using Serilog;

namespace RTLSDRCore.Hardware
{
    /// <summary>
    /// Factory for creating SDR device instances
    /// </summary>
    public static class SdrDeviceFactory
    {
        private static readonly ILogger Logger = Log.ForContext(typeof(SdrDeviceFactory));

        /// <summary>
        /// Enumerates all available SDR devices
        /// </summary>
        /// <returns>List of device information</returns>
        public static IReadOnlyList<DeviceInfo> EnumerateDevices()
        {
            var devices = new List<DeviceInfo>();

            // Try to enumerate RTL-SDR devices
            try
            {
                var rtlDevices = RtlSdrDevice.EnumerateDevices();
                devices.AddRange(rtlDevices);
            }
            catch (Exception ex)
            {
                Logger.Warning(ex, "Failed to enumerate RTL-SDR devices");
            }

            // Always add a mock device option
            devices.Add(new DeviceInfo
            {
                Index = -1,
                Name = "Mock SDR Device",
                Manufacturer = "RTLSDRCore",
                Serial = "MOCK-001",
                Type = DeviceType.Mock,
                IsAvailable = true,
                TunerType = "Simulated",
                MinFrequencyHz = 24_000_000,
                MaxFrequencyHz = 1_766_000_000,
                SupportedSampleRates = new[] { 250000, 1024000, 1536000, 1792000, 1920000, 2048000, 2400000, 2560000, 2880000, 3200000 },
                AvailableGains = new[] { 0f, 0.9f, 1.4f, 2.7f, 3.7f, 7.7f, 8.7f, 12.5f, 14.4f, 15.7f, 16.6f, 19.7f, 20.7f, 22.9f, 25.4f, 28.0f, 29.7f, 32.8f, 33.8f, 36.4f, 37.2f, 38.6f, 40.2f, 42.1f, 43.4f, 43.9f, 44.5f, 48.0f, 49.6f }
            });

            return devices;
        }

        /// <summary>
        /// Creates an SDR device instance
        /// </summary>
        /// <param name="deviceInfo">Device information</param>
        /// <returns>SDR device instance</returns>
        public static ISdrDevice CreateDevice(DeviceInfo deviceInfo)
        {
            Logger.Information("Creating device: {DeviceName} (Type: {DeviceType})",
                deviceInfo.Name, deviceInfo.Type);

            return deviceInfo.Type switch
            {
                DeviceType.Mock => new MockSdrDevice(deviceInfo),
                DeviceType.RTLSDR => new RtlSdrDevice(deviceInfo.Index),
                _ => throw new NotSupportedException($"Device type {deviceInfo.Type} is not supported")
            };
        }

        /// <summary>
        /// Creates a mock SDR device for testing
        /// </summary>
        /// <returns>Mock SDR device instance</returns>
        public static ISdrDevice CreateMockDevice()
        {
            Logger.Information("Creating mock SDR device");
            return new MockSdrDevice();
        }

        /// <summary>
        /// Creates and opens the first available RTL-SDR device
        /// </summary>
        /// <returns>SDR device instance, or null if none available</returns>
        public static ISdrDevice? CreateFirstAvailable()
        {
            var devices = EnumerateDevices();
            var rtlDevice = devices.FirstOrDefault(d => d.Type == DeviceType.RTLSDR && d.IsAvailable);

            if (rtlDevice != null)
            {
                Logger.Information("Found RTL-SDR device: {DeviceName}", rtlDevice.Name);
                return CreateDevice(rtlDevice);
            }

            Logger.Warning("No RTL-SDR devices found, falling back to mock device");
            return CreateMockDevice();
        }
    }
}
