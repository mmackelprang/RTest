using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Core.Events;
using Radio.Core.Interfaces;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Models.Audio;
using Radio.Infrastructure.Audio.Fingerprinting;
using Radio.Infrastructure.Audio.Sources.Primary;
using Radio.Infrastructure.Configuration.Abstractions;
using Radio.Infrastructure.Configuration.Models;
using SecretTagModel = Radio.Infrastructure.Configuration.Models.SecretTag;

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
  private readonly IServiceScopeFactory? _serviceScopeFactory;

  // Play history tracking
  private string? _currentPlayHistoryEntryId;

  // State
  private IAudioSource? _activeSource;
  private IAudioSource? _previousSource; // Track previous source for delayed cleanup
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
    SoundFlow.SoundFlowPlaybackService? playbackService = null,
    IServiceScopeFactory? serviceScopeFactory = null)
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
    _serviceScopeFactory = serviceScopeFactory;

    // Subscribe to track identification events for updating play history
    if (_identificationService != null)
    {
      _identificationService.TrackIdentified += OnTrackIdentified;
    }
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

      var mixer = _audioEngine.GetMasterMixer();
      var oldSource = _activeSource;

      // Determine if old source should keep playing during transition
      bool shouldKeepOldSourcePlaying = oldSource != null && 
                                         oldSource != source &&
                                         (oldSource.State == AudioSourceState.Playing || 
                                          oldSource.State == AudioSourceState.Paused);

      if (shouldKeepOldSourcePlaying)
      {
        _logger.LogInformation(
          "Keeping old source {OldSource} playing during transition to {NewSource}",
          oldSource!.Name, source.Name);
        _previousSource = oldSource;
      }

      // Ensure the new source is in the mixer
      var currentActiveSources = mixer.GetActiveSources();
      if (!currentActiveSources.Contains(source))
      {
        _logger.LogInformation("Adding new source {SourceName} to mixer", source.Name);
        mixer.AddSource(source);
      }

      // Update the active source reference early so new source becomes "active"
      _activeSource = source;

      // Determine if new source can auto-play
      var canAutoPlay = source.Type switch
      {
        AudioSourceType.Radio => true,      // Radio tunes to last frequency
        AudioSourceType.Vinyl => true,      // Vinyl captures from USB input
        AudioSourceType.GenericUSB => true, // Generic USB captures from input
        AudioSourceType.FilePlayer => false, // Requires file to be loaded first
        AudioSourceType.Spotify => false,   // Requires track/playlist selection
        _ => false
      };

      // Start playback on the new source if it can auto-play
      if (source is IPrimaryAudioSource newPrimary)
      {
        if (source.State == AudioSourceState.Created)
        {
          _logger.LogDebug("Initializing source: {SourceName}", source.Name);
        }

        if (canAutoPlay && source.State != AudioSourceState.Playing)
        {
          _logger.LogInformation(
            "Starting playback on new source: {SourceName}",
            source.Name);
          await newPrimary.PlayAsync(cancellationToken);
          
          // Cleanup of the old source (stop + mixer removal) is handled centrally
          // in the StateChanged event handler when the new source transitions to Playing.
          // This avoids duplicate cleanup paths and potential race conditions.
        }
        else if (!canAutoPlay)
        {
          _logger.LogInformation(
            "Source {SourceName} requires content selection before playback. Old source will keep playing until new content starts.",
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

    // Subscribe to state changes for play history tracking
    SubscribeToSourceStateChanges(source);

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

    // Check if secrets contain unresolved secret tags (meaning the actual secrets weren't stored)
    if (SecretTagModel.ContainsTag(secrets.ClientID))
    {
      _logger.LogError(
        "Spotify ClientID contains an unresolved secret tag. " +
        "The actual secret value must be stored using the Secrets Management UI or API.");
      throw new InvalidOperationException(
        "Spotify ClientID secret not found. Please store the actual Client ID value using Settings > Secrets in the Web UI.");
    }

    if (SecretTagModel.ContainsTag(secrets.ClientSecret))
    {
      _logger.LogError(
        "Spotify ClientSecret contains an unresolved secret tag. " +
        "The actual secret value must be stored using the Secrets Management UI or API.");
      throw new InvalidOperationException(
        "Spotify ClientSecret secret not found. Please store the actual Client Secret value using Settings > Secrets in the Web UI.");
    }

    if (SecretTagModel.ContainsTag(secrets.RefreshToken))
    {
      _logger.LogError(
        "Spotify RefreshToken contains an unresolved secret tag. " +
        "The actual secret value must be stored using the Secrets Management UI or API.");
      throw new InvalidOperationException(
        "Spotify RefreshToken secret not found. Please store the actual Refresh Token value using Settings > Secrets in the Web UI.");
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
      _playbackService,
      _configurationManager);
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

  /// <summary>
  /// Subscribes to state changes for the given source to track play history.
  /// </summary>
  private void SubscribeToSourceStateChanges(IAudioSource source)
  {
    source.StateChanged += OnSourceStateChanged;
  }

  /// <summary>
  /// Unsubscribes from state changes for the given source.
  /// </summary>
  private void UnsubscribeFromSourceStateChanges(IAudioSource source)
  {
    source.StateChanged -= OnSourceStateChanged;
  }

  /// <summary>
  /// Handles source state changes to record play history when playback starts.
  /// </summary>
  private async void OnSourceStateChanged(object? sender, AudioSourceStateChangedEventArgs e)
  {
    if (sender is not IAudioSource source)
    {
      return;
    }

    // Clean up previous source when active source starts playing
    if (e.NewState == AudioSourceState.Playing && source == _activeSource && _previousSource != null)
    {
      await _switchLock.WaitAsync();
      try
      {
        // Double-check after acquiring lock (state may have changed)
        if (source != _activeSource || _previousSource == null)
        {
          return;
        }

        // Guard: Prevent cleaning up the active source if it was also tracked as previous
        if (_previousSource == _activeSource || _previousSource.Id == _activeSource.Id)
        {
          _logger.LogInformation("Active source and previous source are the same. Skipping cleanup.");
          _previousSource = null;
          return;
        }

        _logger.LogInformation(
          "Active source {ActiveSource} is now playing. Cleaning up previous source {PreviousSource}",
          source.Name, _previousSource.Name);

        try
        {
          var mixer = _audioEngine.GetMasterMixer();
          
          // Stop the previous source
          if (_previousSource is IPrimaryAudioSource previousPrimary)
          {
            await previousPrimary.StopAsync();
            _logger.LogInformation("Stopped previous source: {SourceName}", _previousSource.Name);
          }

          // Remove previous source from mixer
          if (mixer.GetActiveSources().Contains(_previousSource))
          {
            mixer.RemoveSource(_previousSource);
            _logger.LogInformation("Removed previous source {SourceName} from mixer", _previousSource.Name);
          }

          _previousSource = null;
        }
        catch (Exception ex)
        {
          _logger.LogError(ex, "Failed to clean up previous source");
        }
      }
      finally
      {
        _switchLock.Release();
      }
    }

    // Only record when transitioning to Playing state
    if (e.NewState != AudioSourceState.Playing)
    {
      return;
    }

    // Only track primary sources that are the active source
    if (source != _activeSource)
    {
      return;
    }

    await RecordPlayStartAsync(source);
  }

  /// <summary>
  /// Records a play history entry when a track starts playing.
  /// </summary>
  private async Task RecordPlayStartAsync(IAudioSource source)
  {
    if (_serviceScopeFactory == null)
    {
      return;
    }

    try
    {
      using var scope = _serviceScopeFactory.CreateScope();
      var playHistoryRepository = scope.ServiceProvider.GetService<IPlayHistoryRepository>();
      if (playHistoryRepository == null)
      {
        _logger.LogDebug("IPlayHistoryRepository not available, skipping play history recording");
        return;
      }

      var playSource = MapSourceTypeToPlaySource(source.Type);
      var metadata = GetSourceMetadata(source);

      // Build source details including basic metadata for display
      // We don't set TrackMetadataId because that requires the metadata to be saved
      // to the TrackMetadata table first. The fingerprinting service will update
      // the entry with a proper TrackMetadataId after identification.
      var sourceDetails = source.Name;
      if (metadata != null)
      {
        sourceDetails = $"{metadata.Title} - {metadata.Artist}";
      }

      var entryId = Guid.NewGuid().ToString();
      var entry = new PlayHistoryEntry
      {
        Id = entryId,
        TrackMetadataId = null, // Don't set FK - metadata isn't saved to TrackMetadata table yet
        FingerprintId = null,
        PlayedAt = DateTime.UtcNow,
        Source = playSource,
        MetadataSource = metadata?.Source,
        SourceDetails = sourceDetails,
        DurationSeconds = null,
        IdentificationConfidence = null,
        WasIdentified = metadata != null,
        Track = metadata // Navigation property for in-memory use, not stored
      };

      await playHistoryRepository.RecordPlayAsync(entry);
      _currentPlayHistoryEntryId = entryId;

      _logger.LogDebug(
        "Recorded play history entry {EntryId} for source {SourceName} (identified: {WasIdentified})",
        entryId, source.Name, entry.WasIdentified);
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Failed to record play history for source {SourceName}", source.Name);
    }
  }

  /// <summary>
  /// Gets metadata from the source if available.
  /// </summary>
  private TrackMetadata? GetSourceMetadata(IAudioSource source)
  {
    // Try to get metadata from the source's Metadata dictionary
    if (source is not IPrimaryAudioSource primarySource)
    {
      return null;
    }

    var metadata = primarySource.Metadata;
    if (metadata == null || metadata.Count == 0)
    {
      return null;
    }

    // Get values from metadata dictionary
    var title = metadata.TryGetValue(StandardMetadataKeys.Title, out var titleObj)
      ? titleObj?.ToString() : null;
    var artist = metadata.TryGetValue(StandardMetadataKeys.Artist, out var artistObj)
      ? artistObj?.ToString() : null;
    var album = metadata.TryGetValue(StandardMetadataKeys.Album, out var albumObj)
      ? albumObj?.ToString() : null;
    var coverArt = metadata.TryGetValue(StandardMetadataKeys.AlbumArtUrl, out var coverObj)
      ? coverObj?.ToString() : null;

    // Don't create metadata if we only have defaults
    if (string.IsNullOrWhiteSpace(title) || title == StandardMetadataKeys.DefaultTitle)
    {
      // For file player, try to use the filename
      if (source is FilePlayerAudioSource filePlayer && !string.IsNullOrEmpty(filePlayer.CurrentFile))
      {
        title = System.IO.Path.GetFileNameWithoutExtension(filePlayer.CurrentFile);
      }
      else
      {
        return null;
      }
    }

    // Determine metadata source based on source type
    var metadataSource = source.Type switch
    {
      AudioSourceType.Spotify => MetadataSource.Spotify,
      AudioSourceType.FilePlayer => MetadataSource.FileTag,
      _ => MetadataSource.Manual
    };

    return new TrackMetadata
    {
      Id = Guid.NewGuid().ToString(),
      Title = title ?? "Unknown",
      Artist = string.IsNullOrWhiteSpace(artist) || artist == StandardMetadataKeys.DefaultArtist
        ? "Unknown" : artist,
      Album = string.IsNullOrWhiteSpace(album) || album == StandardMetadataKeys.DefaultAlbum
        ? null : album,
      CoverArtUrl = string.IsNullOrWhiteSpace(coverArt) || coverArt == StandardMetadataKeys.DefaultAlbumArtUrl
        ? null : coverArt,
      Source = metadataSource,
      CreatedAt = DateTime.UtcNow,
      UpdatedAt = DateTime.UtcNow
    };
  }

  /// <summary>
  /// Maps AudioSourceType to PlaySource enum.
  /// </summary>
  private static PlaySource MapSourceTypeToPlaySource(AudioSourceType sourceType)
  {
    return sourceType switch
    {
      AudioSourceType.Spotify => PlaySource.Spotify,
      AudioSourceType.Radio => PlaySource.Radio,
      AudioSourceType.Vinyl => PlaySource.Vinyl,
      AudioSourceType.FilePlayer => PlaySource.File,
      AudioSourceType.GenericUSB => PlaySource.GenericUSB,
      _ => PlaySource.File
    };
  }

  /// <summary>
  /// Handles track identification events to update play history with metadata.
  /// </summary>
  private async void OnTrackIdentified(object? sender, TrackIdentifiedEventArgs e)
  {
    if (_serviceScopeFactory == null || _activeSource == null)
    {
      return;
    }

    try
    {
      using var scope = _serviceScopeFactory.CreateScope();
      var playHistoryRepository = scope.ServiceProvider.GetService<IPlayHistoryRepository>();
      if (playHistoryRepository == null)
      {
        _logger.LogDebug("IPlayHistoryRepository not available, skipping play history update");
        return;
      }

      var playSource = MapSourceTypeToPlaySource(_activeSource.Type);

      // Try to find a recent unidentified entry for this source to update
      var existingEntry = await playHistoryRepository.GetRecentUnidentifiedAsync(playSource, 5);

      if (existingEntry != null)
      {
        // Update the existing entry with fingerprinting data
        var updatedEntry = existingEntry with
        {
          TrackMetadataId = e.Track.Id,
          FingerprintId = e.Track.FingerprintId,
          MetadataSource = MetadataSource.Fingerprinting,
          IdentificationConfidence = e.Confidence,
          WasIdentified = true,
          Track = e.Track
        };

        await playHistoryRepository.UpdateAsync(updatedEntry);
        _logger.LogInformation(
          "Updated play history entry {EntryId} with fingerprinting data: '{Title}' by '{Artist}' (confidence: {Confidence:P0})",
          existingEntry.Id, e.Track.Title, e.Track.Artist, e.Confidence);
      }
      else
      {
        _logger.LogDebug("No recent unidentified entry found to update with fingerprinting data");
      }
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Failed to update play history with fingerprinting data");
    }
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

    // Unsubscribe from track identification events
    if (_identificationService != null)
    {
      _identificationService.TrackIdentified -= OnTrackIdentified;
    }

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
        UnsubscribeFromSourceStateChanges(source);
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
