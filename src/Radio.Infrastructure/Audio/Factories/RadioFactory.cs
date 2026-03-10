using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Core.Interfaces;
using Radio.Core.Interfaces.Audio;
using Radio.Infrastructure.Audio.Fingerprinting;
using Radio.Infrastructure.Audio.SoundFlow;
using Radio.Infrastructure.Audio.Sources.Primary;
using Radio.Infrastructure.Configuration;
using Radio.Metrics;
using RTLSDRCore;
using RTLSDRCore.Hardware;
using RTLSDRCore.Models;

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
  private readonly SoundFlowPlaybackService? _playbackService;
  private readonly IConfiguration _configuration;
  private readonly IMetricsCollector? _metricsCollector;
  private readonly DeviceOptionsResolver? _deviceOptionsResolver;
  private readonly Radio.Configuration.Abstractions.IConfigurationManager? _configurationManager;

  // Device enumeration cache
  private IReadOnlyList<DeviceInfo>? _cachedDevices;
  private DateTime _deviceCacheExpiry = DateTime.MinValue;
  private readonly TimeSpan _deviceCacheDuration = TimeSpan.FromSeconds(30);
  private readonly object _deviceCacheLock = new();

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
  /// <param name="playbackService">Optional SoundFlow playback service for audio output.</param>
  /// <param name="metricsCollector">Optional metrics collector.</param>
  /// <param name="deviceOptionsResolver">Optional resolver for config store device options.</param>
  /// <param name="configurationManager">Optional configuration manager for preference restoration.</param>
  public RadioFactory(
    ILogger<RadioFactory> logger,
    ILoggerFactory loggerFactory,
    IOptionsMonitor<DeviceOptions> deviceOptions,
    IOptionsMonitor<RadioOptions> radioOptions,
    IAudioDeviceManager deviceManager,
    IConfiguration configuration,
    BackgroundIdentificationService? identificationService = null,
    SoundFlowPlaybackService? playbackService = null,
    IMetricsCollector? metricsCollector = null,
    DeviceOptionsResolver? deviceOptionsResolver = null,
    Radio.Configuration.Abstractions.IConfigurationManager? configurationManager = null)
  {
    _logger = logger;
    _loggerFactory = loggerFactory;
    _deviceOptions = deviceOptions;
    _radioOptions = radioOptions;
    _deviceManager = deviceManager;
    _configuration = configuration;
    _identificationService = identificationService;
    _playbackService = playbackService;
    _metricsCollector = metricsCollector;
    _deviceOptionsResolver = deviceOptionsResolver;
    _configurationManager = configurationManager;
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
      var source = new SDRRadioAudioSource(
        logger,
        radioReceiver,
        _radioOptions,
        _metricsCollector,
        _identificationService,
        _playbackService,
        _configurationManager);

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
      var resolvedUSBPort = GetRadioUSBPort();
      var source = new RadioAudioSource(
        logger,
        _deviceOptions,
        _radioOptions,
        _deviceManager,
        _identificationService,
        resolvedUSBPort,
        _playbackService);

      _logger.LogInformation("Successfully created RF320 radio source with USB port: {USBPort}", resolvedUSBPort);
      return source;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to create RF320 radio source");
      throw new InvalidOperationException("Failed to create RF320 radio source", ex);
    }
  }

  /// <summary>
  /// Checks if RTL-SDR devices are available using cached enumeration.
  /// </summary>
  private bool IsRTLSDRAvailable()
  {
    try
    {
      var devices = GetCachedRTLSDRDevices();
      var rtlDevice = devices.FirstOrDefault(d => 
        d.Type == RTLSDRCore.Enums.DeviceType.RTLSDR && d.IsAvailable);
      
      if (rtlDevice != null)
      {
        _logger.LogDebug("RTL-SDR device available: {DeviceName}", rtlDevice.Name);
        return true;
      }

      _logger.LogDebug("No RTL-SDR devices found");
      return false;
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Error checking RTL-SDR availability");
      return false;
    }
  }

  /// <summary>
  /// Gets RTL-SDR devices from cache or enumerates if cache is expired.
  /// </summary>
  /// <returns>List of device information.</returns>
  public IReadOnlyList<DeviceInfo> GetRTLSDRDevices()
  {
    return GetCachedRTLSDRDevices();
  }

  /// <summary>
  /// Gets cached RTL-SDR device list, refreshing if expired.
  /// </summary>
  private IReadOnlyList<DeviceInfo> GetCachedRTLSDRDevices()
  {
    lock (_deviceCacheLock)
    {
      if (_cachedDevices != null && DateTime.UtcNow < _deviceCacheExpiry)
      {
        return _cachedDevices;
      }

      try
      {
        _logger.LogDebug("Enumerating RTL-SDR devices...");
        _cachedDevices = SdrDeviceFactory.EnumerateDevices();
        _deviceCacheExpiry = DateTime.UtcNow.Add(_deviceCacheDuration);
        
        _logger.LogInformation(
          "Found {Count} SDR devices (cache expires in {Duration}s)",
          _cachedDevices.Count, _deviceCacheDuration.TotalSeconds);
        
        return _cachedDevices;
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Failed to enumerate RTL-SDR devices");
        return Array.Empty<DeviceInfo>();
      }
    }
  }

  /// <summary>
  /// Invalidates the device cache, forcing re-enumeration on next access.
  /// </summary>
  public void InvalidateDeviceCache()
  {
    lock (_deviceCacheLock)
    {
      _cachedDevices = null;
      _deviceCacheExpiry = DateTime.MinValue;
      _logger.LogDebug("RTL-SDR device cache invalidated");
    }
  }

  /// <summary>
  /// Checks if RF320 device is available.
  /// Reads from the config store first (so UI-saved values are picked up),
  /// falling back to IOptionsMonitor (appsettings.json).
  /// </summary>
  private bool IsRF320Available()
  {
    try
    {
      var usbPort = GetRadioUSBPort();
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

  /// <summary>
  /// Gets the Radio USB port from the config store (if available) or IOptionsMonitor.
  /// </summary>
  internal string GetRadioUSBPort()
  {
    // Try config store first (synchronous wait — acceptable here since
    // this is only called during source creation, not on hot paths)
    if (_deviceOptionsResolver != null)
    {
      try
      {
        var port = _deviceOptionsResolver.GetRadioUSBPortAsync().GetAwaiter().GetResult();
        if (!string.IsNullOrWhiteSpace(port))
        {
          return port;
        }
      }
      catch (Exception ex)
      {
        _logger.LogWarning(ex, "Failed to read Radio USB port from config store, using appsettings fallback");
      }
    }

    return _deviceOptions.CurrentValue.Radio?.USBPort ?? "";
  }
}
