using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Core.Interfaces;
using Radio.Core.Interfaces.Audio;
using Radio.Infrastructure.Audio.Fingerprinting;
using Radio.Infrastructure.Audio.Sources.Primary;
using Radio.Infrastructure.Configuration.Abstractions;
using Radio.Infrastructure.Configuration.Models;

namespace Radio.Infrastructure.Audio.Services;

/// <summary>
/// Central coordinator for audio sources and playback.
/// Manages source lifecycle, switching, and mixer integration.
/// </summary>
public class AudioManager : IAudioManager
{
  private readonly ILogger<AudioManager> _logger;
  private readonly ILoggerFactory _loggerFactory;
  private readonly IAudioEngine _audioEngine;
  private readonly IAudioDeviceManager _deviceManager;
  private readonly IRadioFactory _radioFactory;

  // Options for source creation
  private readonly IOptionsMonitor<SpotifySecrets> _spotifySecrets;
  private readonly IOptionsMonitor<SpotifyPreferences> _spotifyPreferences;
  private readonly IOptionsMonitor<FilePlayerOptions> _filePlayerOptions;
  private readonly IOptionsMonitor<FilePlayerPreferences> _filePlayerPreferences;
  private readonly IOptionsMonitor<DeviceOptions> _deviceOptions;
  private readonly IOptionsMonitor<GenericSourcePreferences> _genericSourcePreferences;
  private readonly IOptionsMonitor<AudioPreferences> _audioPreferences;
  private readonly IConfiguration _configuration;

  // Optional services
  private readonly BackgroundIdentificationService? _identificationService;
  private readonly IMetricsCollector? _metricsCollector;
  private readonly Configuration.Abstractions.IConfigurationManager? _configurationManager;
  private readonly SoundFlow.SoundFlowPlaybackService? _playbackService;

  // State
  private IAudioSource? _activeSource;
  private readonly Dictionary<AudioSourceType, IAudioSource> _sourceCache = new();
  private readonly SemaphoreSlim _switchLock = new(1, 1);
  private bool _initialized;
  private bool _disposed;

  /// <summary>
  /// Initializes a new instance of the <see cref="AudioManager"/> class.
  /// </summary>
  public AudioManager(
    ILogger<AudioManager> logger,
    ILoggerFactory loggerFactory,
    IAudioEngine audioEngine,
    IAudioDeviceManager deviceManager,
    IRadioFactory radioFactory,
    IOptionsMonitor<SpotifySecrets> spotifySecrets,
    IOptionsMonitor<SpotifyPreferences> spotifyPreferences,
    IOptionsMonitor<FilePlayerOptions> filePlayerOptions,
    IOptionsMonitor<FilePlayerPreferences> filePlayerPreferences,
    IOptionsMonitor<DeviceOptions> deviceOptions,
    IOptionsMonitor<GenericSourcePreferences> genericSourcePreferences,
    IOptionsMonitor<AudioPreferences> audioPreferences,
    IConfiguration configuration,
    BackgroundIdentificationService? identificationService = null,
    IMetricsCollector? metricsCollector = null,
    Configuration.Abstractions.IConfigurationManager? configurationManager = null,
    SoundFlow.SoundFlowPlaybackService? playbackService = null)
  {
    _logger = logger;
    _loggerFactory = loggerFactory;
    _audioEngine = audioEngine;
    _deviceManager = deviceManager;
    _radioFactory = radioFactory;
    _spotifySecrets = spotifySecrets;
    _spotifyPreferences = spotifyPreferences;
    _filePlayerOptions = filePlayerOptions;
    _filePlayerPreferences = filePlayerPreferences;
    _deviceOptions = deviceOptions;
    _genericSourcePreferences = genericSourcePreferences;
    _audioPreferences = audioPreferences;
    _configuration = configuration;
    _identificationService = identificationService;
    _metricsCollector = metricsCollector;
    _configurationManager = configurationManager;
    _playbackService = playbackService;
  }

  /// <inheritdoc/>
  public IAudioEngine Engine => _audioEngine;

  /// <inheritdoc/>
  public IAudioDeviceManager DeviceManager => _deviceManager;

  /// <inheritdoc/>
  public IAudioSource? ActiveSource => _activeSource;

  /// <inheritdoc/>
  public float MasterVolume
  {
    get => _audioEngine.GetMasterMixer().MasterVolume;
    set => _audioEngine.GetMasterMixer().MasterVolume = value;
  }

  /// <inheritdoc/>
  public bool IsMuted
  {
    get => _audioEngine.GetMasterMixer().IsMuted;
    set => _audioEngine.GetMasterMixer().IsMuted = value;
  }

  /// <inheritdoc/>
  public async Task InitializeAsync(CancellationToken cancellationToken = default)
  {
    if (_initialized)
    {
      return;
    }

    _logger.LogInformation("Initializing AudioManager");

    // Ensure the audio engine is initialized
    if (!_audioEngine.IsReady)
    {
      await _audioEngine.InitializeAsync(cancellationToken);
    }

    _initialized = true;
    _logger.LogInformation("AudioManager initialized successfully");
  }

  /// <inheritdoc/>
  public async Task SwitchSourceAsync(IAudioSource source, CancellationToken cancellationToken = default)
  {
    ObjectDisposedException.ThrowIf(_disposed, this);
    ArgumentNullException.ThrowIfNull(source);

    if (source.Category != AudioSourceCategory.Primary)
    {
      throw new ArgumentException("Only primary sources can be switched to", nameof(source));
    }

    await _switchLock.WaitAsync(cancellationToken);
    try
    {
      _logger.LogInformation(
        "Switching from source {OldSource} to {NewSource} ({NewSourceType})",
        _activeSource?.Name ?? "none",
        source.Name,
        source.Type);

      // Pause current source if it's playing
      if (_activeSource != null && _activeSource != source)
      {
        if (_activeSource.State == AudioSourceState.Playing &&
            _activeSource is IPrimaryAudioSource currentPrimary)
        {
          _logger.LogDebug("Pausing current source: {SourceName}", _activeSource.Name);
          await currentPrimary.PauseAsync(cancellationToken);
        }
      }

      // Ensure the new source is in the mixer
      var mixer = _audioEngine.GetMasterMixer();
      var activeSources = mixer.GetActiveSources();
      if (!activeSources.Contains(source))
      {
        _logger.LogDebug("Adding source {SourceName} to mixer", source.Name);
        mixer.AddSource(source);
      }

      // Update the active source reference
      _activeSource = source;

      // Start playback on the new source if it's ready and can auto-play
      // Sources like FilePlayer and Spotify require content to be selected first
      if (source is IPrimaryAudioSource newPrimary)
      {
        if (source.State == AudioSourceState.Created)
        {
          _logger.LogDebug("Initializing source: {SourceName}", source.Name);
        }

        // Only auto-play sources that can play immediately without content selection
        // FilePlayer needs a file loaded, Spotify needs a track/playlist selected
        var canAutoPlay = source.Type switch
        {
          AudioSourceType.Radio => true,      // Radio tunes to last frequency
          AudioSourceType.Vinyl => true,      // Vinyl captures from USB input
          AudioSourceType.GenericUSB => true, // Generic USB captures from input
          AudioSourceType.FilePlayer => false, // Requires file to be loaded first
          AudioSourceType.Spotify => false,   // Requires track/playlist selection
          _ => false
        };

        if (canAutoPlay && source.State != AudioSourceState.Playing)
        {
          _logger.LogDebug("Starting playback on source: {SourceName}", source.Name);
          await newPrimary.PlayAsync(cancellationToken);
        }
        else if (!canAutoPlay)
        {
          _logger.LogDebug(
            "Source {SourceName} requires content selection before playback",
            source.Name);
        }
      }

      // Persist the source selection
      await PersistSourcePreferenceAsync(source.Type, cancellationToken);

      _logger.LogInformation(
        "Successfully switched to source: {SourceName} ({SourceType})",
        source.Name, source.Type);
    }
    finally
    {
      _switchLock.Release();
    }
  }

  /// <inheritdoc/>
  public async Task StopAsync(CancellationToken cancellationToken = default)
  {
    ObjectDisposedException.ThrowIf(_disposed, this);

    _logger.LogInformation("Stopping audio playback");

    if (_activeSource is IPrimaryAudioSource primarySource)
    {
      await primarySource.StopAsync(cancellationToken);
    }

    _logger.LogInformation("Audio playback stopped");
  }

  /// <inheritdoc/>
  public async Task<IAudioSource?> GetOrCreateSourceAsync(
    AudioSourceType sourceType,
    bool switchToSource = true,
    CancellationToken cancellationToken = default)
  {
    ObjectDisposedException.ThrowIf(_disposed, this);

    // Check cache first
    if (_sourceCache.TryGetValue(sourceType, out var cachedSource))
    {
      _logger.LogDebug("Returning cached source for type: {SourceType}", sourceType);
      
      if (switchToSource && cachedSource != _activeSource)
      {
        await SwitchSourceAsync(cachedSource, cancellationToken);
      }
      
      return cachedSource;
    }

    _logger.LogInformation("Creating new source for type: {SourceType}", sourceType);

    IAudioSource? source = null;
    try
    {
      source = sourceType switch
      {
        AudioSourceType.Radio => CreateRadioSource(),
        AudioSourceType.Spotify => CreateSpotifySource(),
        AudioSourceType.FilePlayer => CreateFilePlayerSource(),
        AudioSourceType.Vinyl => CreateVinylSource(),
        AudioSourceType.GenericUSB => CreateGenericUSBSource(),
        _ => null
      };
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to create source for type: {SourceType}", sourceType);
      return null;
    }

    if (source == null)
    {
      _logger.LogWarning("Source type {SourceType} is not supported", sourceType);
      return null;
    }

    // Initialize the source before adding to mixer
    if (source is IPrimaryAudioSource primarySource)
    {
      _logger.LogDebug("Initializing source: {SourceName}", source.Name);
      await primarySource.InitializeAsync(cancellationToken);

      if (source.State == AudioSourceState.Error)
      {
        _logger.LogWarning("Source {SourceName} failed to initialize", source.Name);
        return null;
      }
    }

    // Cache the source for reuse
    _sourceCache[sourceType] = source;

    // Add to mixer
    var mixer = _audioEngine.GetMasterMixer();
    mixer.AddSource(source);

    _logger.LogInformation(
      "Created and registered source: {SourceName} ({SourceType})",
      source.Name, source.Type);

    // Switch to the source if requested
    if (switchToSource)
    {
      await SwitchSourceAsync(source, cancellationToken);
    }

    return source;
  }

  /// <summary>
  /// Restores the last active audio source based on saved preferences.
  /// Falls back to Radio if the preferred source is unavailable.
  /// </summary>
  /// <param name="cancellationToken">Cancellation token.</param>
  public async Task RestoreLastSourceAsync(CancellationToken cancellationToken = default)
  {
    ObjectDisposedException.ThrowIf(_disposed, this);

    var prefs = _audioPreferences.CurrentValue;
    var lastSource = prefs.CurrentSource ?? "Radio";

    _logger.LogInformation("Attempting to restore last source: {LastSource}", lastSource);

    if (!Enum.TryParse<AudioSourceType>(lastSource, true, out var sourceType))
    {
      _logger.LogWarning("Invalid source type in preferences: {LastSource}, defaulting to Radio", lastSource);
      sourceType = AudioSourceType.Radio;
    }

    try
    {
      var source = await GetOrCreateSourceAsync(sourceType, switchToSource: true, cancellationToken);
      if (source == null)
      {
        throw new InvalidOperationException($"Failed to create source for type: {sourceType}");
      }
      _logger.LogInformation("Successfully restored source: {SourceType}", sourceType);
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Failed to restore source {SourceType}, falling back to Radio", sourceType);

      // Don't try to fallback to Radio if Radio was what failed
      if (sourceType != AudioSourceType.Radio)
      {
        try
        {
          var radioSource = await GetOrCreateSourceAsync(AudioSourceType.Radio, switchToSource: true, cancellationToken);
          if (radioSource == null)
          {
            throw new InvalidOperationException("Failed to create fallback Radio source");
          }
          _logger.LogInformation("Fell back to Radio source");
        }
        catch (Exception radioEx)
        {
          _logger.LogError(radioEx, "Failed to create fallback Radio source");
          throw;
        }
      }
      else
      {
        throw;
      }
    }
  }

  /// <summary>
  /// Persists the current source selection to preferences.
  /// </summary>
  private async Task PersistSourcePreferenceAsync(AudioSourceType sourceType, CancellationToken cancellationToken)
  {
    if (_configurationManager == null)
    {
      _logger.LogDebug("ConfigurationManager not available, skipping preference persistence");
      return;
    }

    try
    {
      // Use the main configuration store ("config" or "sqlite") to ensure IOptionsMonitor picks up the change
      // Key must match the section name defined in AudioPreferences
      var storeId = _configurationManager.CurrentStoreType == ConfigurationStoreType.Sqlite ? "sqlite" : "config";
      
      await _configurationManager.SetValueAsync(
        storeId,
        "AudioPreferences:CurrentSource",
        sourceType.ToString(),
        cancellationToken);

      _logger.LogDebug("Persisted source preference: {SourceType}", sourceType);
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Failed to persist source preference for {SourceType}", sourceType);
    }
  }

  /// <summary>
  /// Creates a radio audio source using the RadioFactory.
  /// </summary>
  private IAudioSource CreateRadioSource()
  {
    var deviceType = _radioFactory.GetDefaultDeviceType();
    _logger.LogDebug("Creating radio source with device type: {DeviceType}", deviceType);
    return _radioFactory.CreateRadioSource(deviceType);
  }

  /// <summary>
  /// Creates a Spotify audio source.
  /// </summary>
  /// <exception cref="InvalidOperationException">Thrown if Spotify credentials are not configured.</exception>
  private IAudioSource CreateSpotifySource()
  {
    var secrets = _spotifySecrets.CurrentValue;

    // Validate Spotify configuration
    if (string.IsNullOrWhiteSpace(secrets.ClientID) ||
        string.IsNullOrWhiteSpace(secrets.ClientSecret))
    {
      throw new InvalidOperationException(
        "Spotify is not configured. Please configure ClientId and ClientSecret in the SpotifySecrets section.");
    }

    if (string.IsNullOrWhiteSpace(secrets.RefreshToken))
    {
      throw new InvalidOperationException(
        "Spotify authentication required. Please complete the Spotify OAuth flow to obtain a refresh token.");
    }

    var logger = _loggerFactory.CreateLogger<SpotifyAudioSource>();
    return new SpotifyAudioSource(
      logger,
      _spotifySecrets,
      _spotifyPreferences,
      _deviceOptions,
      _playbackService,
      _metricsCollector,
      _loggerFactory);
  }

  /// <summary>
  /// Creates a file player audio source.
  /// </summary>
  private IAudioSource CreateFilePlayerSource()
  {
    // Use the same root directory logic as FileBrowser for consistency
    // The FilePlayerOptions.RootDirectory is a subdirectory relative to this root
    var rootDir = _configuration["RootDir"] ?? Directory.GetCurrentDirectory();

    var logger = _loggerFactory.CreateLogger<FilePlayerAudioSource>();
    return new FilePlayerAudioSource(
      logger,
      _filePlayerOptions,
      _filePlayerPreferences,
      rootDir,
      _identificationService,
      _metricsCollector,
      _playbackService);
  }

  /// <summary>
  /// Creates a vinyl turntable audio source.
  /// </summary>
  /// <exception cref="InvalidOperationException">Thrown if vinyl USB port is not configured.</exception>
  private IAudioSource CreateVinylSource()
  {
    var vinylConfig = _deviceOptions.CurrentValue.Vinyl;

    if (vinylConfig == null || string.IsNullOrWhiteSpace(vinylConfig.USBPort))
    {
      throw new InvalidOperationException(
        "Vinyl source is not configured. Please configure the USB port in DeviceOptions.Vinyl section.");
    }

    var logger = _loggerFactory.CreateLogger<VinylAudioSource>();
    return new VinylAudioSource(
      logger,
      _deviceOptions,
      _deviceManager,
      _identificationService);
  }

  /// <summary>
  /// Creates a generic USB audio source.
  /// </summary>
  /// <exception cref="InvalidOperationException">Thrown if generic USB port is not configured.</exception>
  private IAudioSource CreateGenericUSBSource()
  {
    var prefs = _genericSourcePreferences.CurrentValue;

    if (string.IsNullOrWhiteSpace(prefs.USBPort))
    {
      throw new InvalidOperationException(
        "Generic USB source is not configured. Please configure the USB port in GenericSourcePreferences section.");
    }

    var logger = _loggerFactory.CreateLogger<GenericUSBAudioSource>();
    return new GenericUSBAudioSource(
      logger,
      _genericSourcePreferences,
      _deviceManager);
  }

  /// <inheritdoc/>
  public async ValueTask DisposeAsync()
  {
    if (_disposed)
    {
      return;
    }

    _disposed = true;

    _logger.LogInformation("Disposing AudioManager");

    // Stop current playback (don't call StopAsync as it checks disposed flag)
    try
    {
      if (_activeSource is IPrimaryAudioSource primarySource)
      {
        await primarySource.StopAsync();
      }
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Error stopping playback during disposal");
    }

    // Dispose cached sources
    foreach (var source in _sourceCache.Values)
    {
      try
      {
        await source.DisposeAsync();
      }
      catch (Exception ex)
      {
        _logger.LogWarning(ex, "Error disposing source {SourceName}", source.Name);
      }
    }

    _sourceCache.Clear();
    _switchLock.Dispose();

    _logger.LogInformation("AudioManager disposed");
  }
}
