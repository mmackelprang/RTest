using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Core.Interfaces;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Models.Audio;
using Radio.Infrastructure.Audio.Fingerprinting;
using Radio.Infrastructure.Audio.Sources.Primary;
using Radio.Infrastructure.Configuration;
using Radio.Infrastructure.Configuration.Abstractions;

namespace Radio.Infrastructure.Audio.Services;

/// <summary>
/// Creates audio sources with all required dependencies.
/// Consolidates source creation logic that was spread across AudioManager.
/// </summary>
public class AudioSourceFactory : IAudioSourceFactory
{
  private readonly ILogger<AudioSourceFactory> _logger;
  private readonly ILoggerFactory _loggerFactory;
  private readonly IAudioDeviceManager _deviceManager;
  private readonly IRadioFactory _radioFactory;
  private readonly IBluetoothService _bluetoothService;
  private readonly IOptionsMonitor<BluetoothOptions> _bluetoothOptions;
  private readonly IOptionsMonitor<FilePlayerOptions> _filePlayerOptions;
  private readonly IOptionsMonitor<FilePlayerPreferences> _filePlayerPreferences;
  private readonly IOptionsMonitor<DeviceOptions> _deviceOptions;
  private readonly IOptionsMonitor<GenericSourcePreferences> _genericSourcePreferences;
  private readonly IConfiguration _configuration;
  private readonly BackgroundIdentificationService? _identificationService;
  private readonly IMetricsCollector? _metricsCollector;
  private readonly Configuration.Abstractions.IConfigurationManager? _configurationManager;
  private readonly SoundFlow.SoundFlowPlaybackService? _playbackService;
  private readonly IServiceScopeFactory? _serviceScopeFactory;
  private readonly AlbumArtCacheService? _albumArtCache;
  private readonly DeviceOptionsResolver? _deviceOptionsResolver;

  public AudioSourceFactory(
    ILogger<AudioSourceFactory> logger,
    ILoggerFactory loggerFactory,
    IAudioDeviceManager deviceManager,
    IRadioFactory radioFactory,
    IBluetoothService bluetoothService,
    IOptionsMonitor<BluetoothOptions> bluetoothOptions,
    IOptionsMonitor<FilePlayerOptions> filePlayerOptions,
    IOptionsMonitor<FilePlayerPreferences> filePlayerPreferences,
    IOptionsMonitor<DeviceOptions> deviceOptions,
    IOptionsMonitor<GenericSourcePreferences> genericSourcePreferences,
    IConfiguration configuration,
    BackgroundIdentificationService? identificationService = null,
    IMetricsCollector? metricsCollector = null,
    Configuration.Abstractions.IConfigurationManager? configurationManager = null,
    SoundFlow.SoundFlowPlaybackService? playbackService = null,
    IServiceScopeFactory? serviceScopeFactory = null,
    AlbumArtCacheService? albumArtCache = null,
    DeviceOptionsResolver? deviceOptionsResolver = null)
  {
    _logger = logger;
    _loggerFactory = loggerFactory;
    _deviceManager = deviceManager;
    _radioFactory = radioFactory;
    _bluetoothService = bluetoothService;
    _bluetoothOptions = bluetoothOptions;
    _filePlayerOptions = filePlayerOptions;
    _filePlayerPreferences = filePlayerPreferences;
    _deviceOptions = deviceOptions;
    _genericSourcePreferences = genericSourcePreferences;
    _configuration = configuration;
    _identificationService = identificationService;
    _metricsCollector = metricsCollector;
    _configurationManager = configurationManager;
    _playbackService = playbackService;
    _serviceScopeFactory = serviceScopeFactory;
    _albumArtCache = albumArtCache;
    _deviceOptionsResolver = deviceOptionsResolver;
  }

  /// <inheritdoc/>
  public IAudioSource CreateSource(AudioSourceType sourceType)
  {
    return sourceType switch
    {
      AudioSourceType.Radio => CreateRadioSource(),
      AudioSourceType.FilePlayer => CreateFilePlayerSource(),
      AudioSourceType.Vinyl => CreateVinylSource(),
      AudioSourceType.GenericUSB => CreateGenericUSBSource(),
      AudioSourceType.Bluetooth => CreateBluetoothSource(),
      _ => throw new ArgumentOutOfRangeException(nameof(sourceType), sourceType, "Unsupported source type")
    };
  }

  private IAudioSource CreateRadioSource()
  {
    var deviceType = _radioFactory.GetDefaultDeviceType();
    _logger.LogDebug("Creating radio source with device type: {DeviceType}", deviceType);
    return _radioFactory.CreateRadioSource(deviceType);
  }

  private IAudioSource CreateBluetoothSource()
  {
    var logger = _loggerFactory.CreateLogger<BluetoothAudioSource>();
    return new BluetoothAudioSource(
      logger,
      _deviceManager,
      _bluetoothService,
      _bluetoothOptions,
      _identificationService,
      _metricsCollector,
      _playbackService,
      _serviceScopeFactory,
      _albumArtCache);
  }

  private IAudioSource CreateFilePlayerSource()
  {
    var rootDir = _configuration["RootDir"] ?? Directory.GetCurrentDirectory();
    var logger = _loggerFactory.CreateLogger<FilePlayerAudioSource>();
    return new FilePlayerAudioSource(
      logger,
      _filePlayerOptions,
      _filePlayerPreferences,
      rootDir,
      _identificationService,
      _metricsCollector,
      _playbackService,
      _configurationManager,
      _albumArtCache);
  }

  private IAudioSource CreateVinylSource()
  {
    // Resolve USB port from config store first, fall back to IOptionsMonitor
    var resolvedUSBPort = GetVinylUSBPort();
    if (string.IsNullOrWhiteSpace(resolvedUSBPort))
    {
      throw new InvalidOperationException(
        "Vinyl source is not configured. Please configure the USB port in System > Configuration > Devices.");
    }

    var logger = _loggerFactory.CreateLogger<VinylAudioSource>();
    return new VinylAudioSource(
      logger,
      _deviceOptions,
      _deviceManager,
      _identificationService,
      resolvedUSBPort,
      _playbackService);
  }

  /// <summary>
  /// Gets the Vinyl USB port from the config store (if available) or IOptionsMonitor.
  /// </summary>
  private string GetVinylUSBPort()
  {
    if (_deviceOptionsResolver != null)
    {
      try
      {
        var port = _deviceOptionsResolver.GetVinylUSBPortAsync().GetAwaiter().GetResult();
        if (!string.IsNullOrWhiteSpace(port))
        {
          return port;
        }
      }
      catch (Exception ex)
      {
        _logger.LogWarning(ex, "Failed to read Vinyl USB port from config store, using appsettings fallback");
      }
    }

    return _deviceOptions.CurrentValue.Vinyl?.USBPort ?? "";
  }

  private IAudioSource CreateGenericUSBSource()
  {
    var resolvedUSBPort = GetGenericUSBPort();
    if (string.IsNullOrWhiteSpace(resolvedUSBPort))
    {
      throw new InvalidOperationException(
        "Generic USB source is not configured. Please configure the USB port in System > Configuration > Generic Source.");
    }

    var logger = _loggerFactory.CreateLogger<GenericUSBAudioSource>();
    return new GenericUSBAudioSource(
      logger,
      _genericSourcePreferences,
      _deviceManager,
      resolvedUSBPort,
      _playbackService);
  }

  /// <summary>
  /// Gets the Generic USB port from the config store (if available) or IOptionsMonitor.
  /// </summary>
  private string GetGenericUSBPort()
  {
    if (_deviceOptionsResolver != null)
    {
      try
      {
        var port = _deviceOptionsResolver.GetGenericUSBPortAsync().GetAwaiter().GetResult();
        if (!string.IsNullOrWhiteSpace(port))
        {
          return port;
        }
      }
      catch (Exception ex)
      {
        _logger.LogWarning(ex, "Failed to read Generic USB port from config store, using appsettings fallback");
      }
    }

    return _genericSourcePreferences.CurrentValue?.USBPort ?? "";
  }
}
