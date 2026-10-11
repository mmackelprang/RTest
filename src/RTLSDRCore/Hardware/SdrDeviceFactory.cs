using RTLSDRCore.Enums;
using RTLSDRCore.Models;
using Serilog;

namespace RTLSDRCore.Hardware;

  /// <summary>
  /// Factory for creating SDR device instances
  /// </summary>
  public static class SdrDeviceFactory
  {
      private static readonly ILogger Logger = Log.ForContext(typeof(SdrDeviceFactory));

      /// <summary>
      /// Enumerates all available SDR devices, with the HF decision of the default
      /// <see cref="HfDirectSamplingSetting.Auto"/> setting.
      /// </summary>
      /// <returns>List of device information</returns>
      public static IReadOnlyList<DeviceInfo> EnumerateDevices() => EnumerateDevices(HfDirectSamplingSetting.Auto);

      /// <summary>
      /// Enumerates all available SDR devices
      /// </summary>
      /// <param name="hfSetting">The <c>RTLSDR:HfDirectSampling</c> setting (AUD-99), which decides each RTL-SDR's HF input.</param>
      /// <returns>List of device information</returns>
      public static IReadOnlyList<DeviceInfo> EnumerateDevices(HfDirectSamplingSetting hfSetting)
      {
          var devices = new List<DeviceInfo>();

          // Try to enumerate RTL-SDR devices
          try
          {
              var rtlDevices = RtlSdrDevice.EnumerateDevices(hfSetting);
              devices.AddRange(rtlDevices);
          }
          catch (Exception ex)
          {
              Logger.Warning(ex, "Failed to enumerate RTL-SDR devices");
          }

          // Always add a mock device option
          devices.Add(MockSdrDevice.CreateDefaultDeviceInfo());

          return devices;
      }

      /// <summary>
      /// Creates an SDR device instance, with the default <see cref="HfDirectSamplingSetting.Auto"/> HF setting.
      /// </summary>
      /// <param name="deviceInfo">Device information</param>
      /// <returns>SDR device instance</returns>
      public static ISdrDevice CreateDevice(DeviceInfo deviceInfo) => CreateDevice(deviceInfo, HfDirectSamplingSetting.Auto);

      /// <summary>
      /// Creates an SDR device instance
      /// </summary>
      /// <param name="deviceInfo">Device information</param>
      /// <param name="hfSetting">The HF setting for an RTL-SDR (AUD-99). A mock device takes its HF input from <paramref name="deviceInfo"/>.</param>
      /// <returns>SDR device instance</returns>
      public static ISdrDevice CreateDevice(DeviceInfo deviceInfo, HfDirectSamplingSetting hfSetting)
      {
          Logger.Information("Creating device: {DeviceName} (Type: {DeviceType})",
              deviceInfo.Name, deviceInfo.Type);

          return deviceInfo.Type switch
          {
              DeviceType.Mock => new MockSdrDevice(deviceInfo),
              DeviceType.RTLSDR => new RtlSdrDevice(deviceInfo.Index, hfSetting),
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
      /// Creates the first available RTL-SDR device (unopened), with the default
      /// <see cref="HfDirectSamplingSetting.Auto"/> HF setting, or the mock device when there is none.
      /// </summary>
      /// <returns>SDR device instance</returns>
      public static ISdrDevice? CreateFirstAvailable() => CreateFirstAvailable(HfDirectSamplingSetting.Auto);

      /// <summary>
      /// Creates the first available RTL-SDR device (unopened), or the mock device when there is none.
      /// </summary>
      /// <param name="hfSetting">The <c>RTLSDR:HfDirectSampling</c> setting (AUD-99).</param>
      /// <returns>SDR device instance</returns>
      public static ISdrDevice? CreateFirstAvailable(HfDirectSamplingSetting hfSetting)
      {
          var devices = EnumerateDevices(hfSetting);
          var rtlDevice = devices.FirstOrDefault(d => d.Type == DeviceType.RTLSDR && d.IsAvailable);

          if (rtlDevice != null)
          {
              Logger.Information("Found RTL-SDR device: {DeviceName} (product {Product})", rtlDevice.Name, rtlDevice.Product);
              return CreateDevice(rtlDevice, hfSetting);
          }

          Logger.Warning("No RTL-SDR devices found, falling back to mock device");
          return CreateMockDevice();
      }
  }
