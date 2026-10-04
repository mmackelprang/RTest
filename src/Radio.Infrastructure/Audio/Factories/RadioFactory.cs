using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Core.Interfaces;
using Radio.Core.Interfaces.Audio;
using Radio.Infrastructure.Audio.Fingerprinting;
using Radio.Infrastructure.Audio.Services;
using Radio.Infrastructure.Audio.SoundFlow;
using Radio.Infrastructure.Audio.Sources.Primary;
using Radio.Metrics;
using Radio.Fingerprinting.Services;
using RTLSDRCore;
using RTLSDRCore.Hardware;
using RTLSDRCore.Models;

namespace Radio.Infrastructure.Audio.Factories;

/// <summary>
/// Factory for creating radio audio sources based on device type.
/// The only supported device type is RTL-SDR (<see cref="DeviceTypes.RTLSDRCore"/>); any other
/// value is unsupported — <see cref="IsDeviceAvailable"/> reports it unavailable and
/// <see cref="CreateRadioSource"/> throws <see cref="ArgumentException"/>.
/// </summary>
public class RadioFactory : IRadioFactory
{
  private readonly ILogger<RadioFactory> _logger;
  private readonly ILoggerFactory _loggerFactory;
  private readonly IOptionsMonitor<RadioOptions> _radioOptions;
  private readonly BackgroundIdentificationService? _identificationService;
  private readonly SoundFlowPlaybackService? _playbackService;
  private readonly IConfiguration _configuration;
  private readonly IMetricsCollector? _metricsCollector;
  private readonly Radio.Configuration.Abstractions.IConfigurationManager? _configurationManager;
  private readonly Func<IAudioSource?>? _getActiveSource;
  private readonly SdrDeviceGate? _deviceGate;
  private readonly IScanStationMap? _scanStationMap;

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
  }

  /// <summary>
  /// Initializes a new instance of the <see cref="RadioFactory"/> class.
  /// </summary>
  /// <param name="logger">The logger instance.</param>
  /// <param name="loggerFactory">Logger factory for creating device-specific loggers.</param>
  /// <param name="radioOptions">Radio configuration options.</param>
  /// <param name="configuration">Application configuration.</param>
  /// <param name="identificationService">Optional fingerprinting service.</param>
  /// <param name="playbackService">Optional SoundFlow playback service for audio output.</param>
  /// <param name="metricsCollector">Optional metrics collector.</param>
  /// <param name="configurationManager">Optional configuration manager for preference restoration.</param>
  /// <param name="getActiveSource">Optional accessor for the audio manager's active source, handed to every created source.</param>
  /// <param name="deviceGate">Optional SDR device gate handed to every created RTL-SDR source (AUD-76).</param>
  /// <param name="scanStationMap">Optional band maps handed to every created RTL-SDR source for Scan Up/Down (AUD-100).</param>
  public RadioFactory(
    ILogger<RadioFactory> logger,
    ILoggerFactory loggerFactory,
    IOptionsMonitor<RadioOptions> radioOptions,
    IConfiguration configuration,
    BackgroundIdentificationService? identificationService = null,
    SoundFlowPlaybackService? playbackService = null,
    IMetricsCollector? metricsCollector = null,
    Radio.Configuration.Abstractions.IConfigurationManager? configurationManager = null,
    Func<IAudioSource?>? getActiveSource = null,
    SdrDeviceGate? deviceGate = null,
    IScanStationMap? scanStationMap = null)
  {
    _scanStationMap = scanStationMap;
    _logger = logger;
    _loggerFactory = loggerFactory;
    _radioOptions = radioOptions;
    _configuration = configuration;
    _identificationService = identificationService;
    _playbackService = playbackService;
    _metricsCollector = metricsCollector;
    _configurationManager = configurationManager;
    _getActiveSource = getActiveSource;
    _deviceGate = deviceGate;
  }

  /// <summary>
  /// Gets the active-source accessor handed to every created radio source.
  /// Exposed for tests that verify DI actually wires the accessor — an
  /// unwired accessor silently restores the cross-source contamination bug.
  /// </summary>
  internal Func<IAudioSource?>? GetActiveSourceAccessor => _getActiveSource;

  /// <summary>
  /// Gets the SDR device gate handed to every created RTL-SDR source. Exposed for
  /// tests that verify DI hands the factory the same gate instance the band-map
  /// service uses.
  /// </summary>
  internal SdrDeviceGate? DeviceGate => _deviceGate;

  /// <summary>
  /// The band maps handed to each created RTL-SDR source (AUD-100); exposed for the test that
  /// verifies DI supplies them, since the parameter is optional and a missing registration would
  /// silently leave every scan seeking live.
  /// </summary>
  internal IScanStationMap? ScanStationMap => _scanStationMap;

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
        _configurationManager,
        _getActiveSource,
        _deviceGate,
        _scanStationMap);

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
}
