using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;
using Radio.Infrastructure.Audio.Fingerprinting;
using Radio.Infrastructure.Audio.Sources.Primary;
using RTLSDRCore;

namespace Radio.Infrastructure.Audio.Factories;

/// <summary>
/// Factory for creating radio audio sources based on device type.
/// Supports RTL-SDR (software-defined radio) and RF320 (Bluetooth radio with USB audio).
/// </summary>
public class RadioFactory : IRadioFactory
{
  private readonly ILogger<RadioFactory> _logger;
  private readonly ILoggerFactory _loggerFactory;
  private readonly IOptionsMonitor<DeviceOptions> _deviceOptions;
  private readonly IOptionsMonitor<RadioOptions> _radioOptions;
  private readonly IAudioDeviceManager _deviceManager;
  private readonly BackgroundIdentificationService? _identificationService;
  private readonly IConfiguration _configuration;

  /// <summary>
  /// Supported device type identifiers.
  /// </summary>
  public static class DeviceTypes
  {
    /// <summary>RTL-SDR software-defined radio.</summary>
    public const string RTLSDRCore = "RTLSDRCore";

    /// <summary>Raddy RF320 Bluetooth radio with USB audio output.</summary>
    public const string RF320 = "RF320";
  }

  /// <summary>
  /// Initializes a new instance of the <see cref="RadioFactory"/> class.
  /// </summary>
  /// <param name="logger">The logger instance.</param>
  /// <param name="loggerFactory">Logger factory for creating device-specific loggers.</param>
  /// <param name="deviceOptions">Device configuration options.</param>
  /// <param name="radioOptions">Radio configuration options.</param>
  /// <param name="deviceManager">Audio device manager.</param>
  /// <param name="configuration">Application configuration.</param>
  /// <param name="identificationService">Optional fingerprinting service.</param>
  public RadioFactory(
    ILogger<RadioFactory> logger,
    ILoggerFactory loggerFactory,
    IOptionsMonitor<DeviceOptions> deviceOptions,
    IOptionsMonitor<RadioOptions> radioOptions,
    IAudioDeviceManager deviceManager,
    IConfiguration configuration,
    BackgroundIdentificationService? identificationService = null)
  {
    _logger = logger;
    _loggerFactory = loggerFactory;
    _deviceOptions = deviceOptions;
    _radioOptions = radioOptions;
    _deviceManager = deviceManager;
    _configuration = configuration;
    _identificationService = identificationService;
  }

  /// <inheritdoc/>
  public IPrimaryAudioSource CreateRadioSource(string deviceType)
  {
    if (string.IsNullOrWhiteSpace(deviceType))
    {
      throw new ArgumentException("Device type cannot be null or empty", nameof(deviceType));
    }

    _logger.LogInformation("Creating radio source for device type: {DeviceType}", deviceType);

    return deviceType switch
    {
      DeviceTypes.RTLSDRCore => CreateRTLSDRSource(),
      DeviceTypes.RF320 => CreateRF320Source(),
      _ => throw new ArgumentException($"Unsupported radio device type: {deviceType}", nameof(deviceType))
    };
  }

  /// <inheritdoc/>
  public IEnumerable<string> GetAvailableDeviceTypes()
  {
    var availableTypes = new List<string>();

    if (IsDeviceAvailable(DeviceTypes.RTLSDRCore))
    {
      availableTypes.Add(DeviceTypes.RTLSDRCore);
    }

    if (IsDeviceAvailable(DeviceTypes.RF320))
    {
      availableTypes.Add(DeviceTypes.RF320);
    }

    _logger.LogInformation("Available radio devices: {Devices}", string.Join(", ", availableTypes));
    return availableTypes;
  }

  /// <inheritdoc/>
  public string GetDefaultDeviceType()
  {
    // Read from configuration, default to RTLSDRCore as specified in requirements
    var defaultDevice = _configuration.GetValue<string>("Radio:DefaultDevice") ?? DeviceTypes.RTLSDRCore;
    
    // Validate that the default device is available
    if (!IsDeviceAvailable(defaultDevice))
    {
      _logger.LogWarning(
        "Configured default device {DefaultDevice} is not available. Falling back to first available device.",
        defaultDevice);

      var availableDevices = GetAvailableDeviceTypes().ToList();
      if (availableDevices.Count == 0)
      {
        throw new InvalidOperationException("No radio devices are available");
      }

      defaultDevice = availableDevices[0];
    }

    _logger.LogInformation("Default radio device: {DefaultDevice}", defaultDevice);
    return defaultDevice;
  }

  /// <inheritdoc/>
  public bool IsDeviceAvailable(string deviceType)
  {
    return deviceType switch
    {
      DeviceTypes.RTLSDRCore => IsRTLSDRAvailable(),
      DeviceTypes.RF320 => IsRF320Available(),
      _ => false
    };
  }

  /// <summary>
  /// Creates an RTL-SDR radio source.
  /// </summary>
  private IPrimaryAudioSource CreateRTLSDRSource()
  {
    try
    {
      // Try to create a RadioReceiver with the first available device
      var radioReceiver = RadioReceiver.CreateWithFirstAvailableDevice();
      
      if (radioReceiver == null)
      {
        throw new InvalidOperationException("No RTL-SDR devices found");
      }

      var logger = _loggerFactory.CreateLogger<SDRRadioAudioSource>();
      var source = new SDRRadioAudioSource(logger, radioReceiver, _radioOptions);

      _logger.LogInformation("Successfully created RTL-SDR radio source");
      return source;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to create RTL-SDR radio source");
      throw new InvalidOperationException("Failed to create RTL-SDR radio source", ex);
    }
  }

  /// <summary>
  /// Creates an RF320 radio source.
  /// </summary>
  private IPrimaryAudioSource CreateRF320Source()
  {
    try
    {
      var logger = _loggerFactory.CreateLogger<RadioAudioSource>();
      var source = new RadioAudioSource(
        logger,
        _deviceOptions,
        _radioOptions,
        _deviceManager,
        _identificationService);

      _logger.LogInformation("Successfully created RF320 radio source");
      return source;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to create RF320 radio source");
      throw new InvalidOperationException("Failed to create RF320 radio source", ex);
    }
  }

  /// <summary>
  /// Checks if RTL-SDR devices are available.
  /// </summary>
  private bool IsRTLSDRAvailable()
  {
    try
    {
      // Try to enumerate RTL-SDR devices without creating a receiver
      // For now, we'll just check if we can create one - in a real implementation
      // we'd use a device enumeration API
      var receiver = RadioReceiver.CreateWithFirstAvailableDevice();
      if (receiver != null)
      {
        receiver.Dispose();
        return true;
      }
      return false;
    }
    catch
    {
      return false;
    }
  }

  /// <summary>
  /// Checks if RF320 device is available.
  /// </summary>
  private bool IsRF320Available()
  {
    try
    {
      // RF320 is available if the USB port is configured and not in use
      var usbPort = _deviceOptions.CurrentValue.Radio?.USBPort;
      if (string.IsNullOrWhiteSpace(usbPort))
      {
        return false;
      }

      return !_deviceManager.IsUSBPortInUse(usbPort);
    }
    catch
    {
      return false;
    }
  }
}
