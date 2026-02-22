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

namespace Radio.Infrastructure.Audio.Services;

/// <summary>
/// Central coordinator for audio sources and playback.
/// Manages source lifecycle, switching, and mixer integration.
/// </summary>
public class AudioManager : IAudioManager, IAsyncDisposable
{
  private readonly ILogger<AudioManager> _logger;
  private readonly ILoggerFactory _loggerFactory;
  private readonly IAudioEngine _audioEngine;
  private readonly IAudioDeviceManager _deviceManager;
  private readonly IRadioFactory _radioFactory;
  private readonly IBluetoothService _bluetoothService;
  private readonly IOptionsMonitor<BluetoothOptions> _bluetoothOptions;

  // Options for source creation
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
  private readonly AlbumArtCacheService? _albumArtCache;

  // Play history tracking (extracted to PlayHistoryTracker)
  private readonly PlayHistoryTracker? _playHistoryTracker;

  // Volume persistence debounce
  private Timer? _volumePersistTimer;
  private readonly object _volumePersistLock = new();

  // State
  private IAudioSource? _activeSource;
  private IAudioSource? _previousSource; // Track previous source for delayed cleanup
  private readonly Dictionary<AudioSourceType, IAudioSource> _sourceCache = new();
  private readonly SemaphoreSlim _switchLock = new(1, 1);
  private readonly SemaphoreSlim _createLock = new(1, 1);
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
    IBluetoothService bluetoothService,
    IOptionsMonitor<BluetoothOptions> bluetoothOptions,
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
    IServiceScopeFactory? serviceScopeFactory = null,
    AlbumArtCacheService? albumArtCache = null,
    PlayHistoryTracker? playHistoryTracker = null)
  {
    _logger = logger;
    _loggerFactory = loggerFactory;
    _audioEngine = audioEngine;
    _deviceManager = deviceManager;
    _radioFactory = radioFactory;
    _bluetoothService = bluetoothService;
    _bluetoothOptions = bluetoothOptions;
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
    _albumArtCache = albumArtCache;
    _playHistoryTracker = playHistoryTracker;

    _bluetoothService.DeviceConnected += OnBluetoothDeviceConnected;
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
    set
    {
      _audioEngine.GetMasterMixer().MasterVolume = value;
      ScheduleVolumePersist();
    }
  }

  /// <inheritdoc/>
  public bool IsMuted
  {
    get => _audioEngine.GetMasterMixer().IsMuted;
    set
    {
      _audioEngine.GetMasterMixer().IsMuted = value;
      ScheduleVolumePersist();
    }
  }

  /// <inheritdoc/>
  public float Balance
  {
    get => _audioEngine.GetMasterMixer().Balance;
    set
    {
      _audioEngine.GetMasterMixer().Balance = value;
      ScheduleVolumePersist();
    }
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

    // Restore volume/mute/balance from persisted preferences
    RestoreVolumePreferences();

    // Pre-warm Bluetooth if enabled on startup
    if (_bluetoothOptions.CurrentValue.EnableOnStartup)
    {
      _logger.LogInformation("Pre-initializing Bluetooth source (EnableOnStartup=true)");
      await GetOrCreateSourceAsync(AudioSourceType.Bluetooth, switchToSource: false, cancellationToken);
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

      // Reset song change detection state for the new source
      _identificationService?.ResetSongChangeState();

      // Determine if new source can auto-play
      var canAutoPlay = source.Type switch
      {
        AudioSourceType.Radio => true,      // Radio tunes to last frequency
        AudioSourceType.Vinyl => true,      // Vinyl captures from USB input
        AudioSourceType.GenericUSB => true, // Generic USB captures from input
        AudioSourceType.Bluetooth => true,  // Bluetooth is a live source (A2DP sink)
        AudioSourceType.FilePlayer => false, // Requires file to be loaded first
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

    // Check cache first (fast path, no lock)
    if (_sourceCache.TryGetValue(sourceType, out var cachedSource))
    {
      _logger.LogDebug("Returning cached source for type: {SourceType}", sourceType);

      if (switchToSource && cachedSource != _activeSource)
      {
        await SwitchSourceAsync(cachedSource, cancellationToken);
      }

      return cachedSource;
    }

    // Serialize source creation to prevent duplicate instances (e.g., two Radio
    // requests arriving simultaneously, both trying to open the RTL-SDR device)
    IAudioSource? source = null;
    await _createLock.WaitAsync(cancellationToken);
    try
    {
      // Double-check cache after acquiring lock
      if (_sourceCache.TryGetValue(sourceType, out cachedSource))
      {
        _logger.LogDebug("Returning cached source for type (after lock): {SourceType}", sourceType);
        if (switchToSource && cachedSource != _activeSource)
        {
          await SwitchSourceAsync(cachedSource, cancellationToken);
        }
        return cachedSource;
      }

      _logger.LogInformation("Creating new source for type: {SourceType}", sourceType);

      if (sourceType == AudioSourceType.Bluetooth && !_bluetoothService.IsAvailable)
      {
        _logger.LogWarning("Bluetooth service not available; cannot create Bluetooth source");
        return null;
      }

      try
      {
        source = sourceType switch
        {
          AudioSourceType.Radio => CreateRadioSource(),
          AudioSourceType.FilePlayer => CreateFilePlayerSource(),
          AudioSourceType.Vinyl => CreateVinylSource(),
          AudioSourceType.GenericUSB => CreateGenericUSBSource(),
          AudioSourceType.Bluetooth => CreateBluetoothSource(),
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
    }
    finally
    {
      _createLock.Release();
    }

    // Switch to the source if requested (outside _createLock to avoid deadlock with _switchLock)
    if (switchToSource && source != null)
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
  /// Schedules a debounced persistence of volume/mute/balance preferences.
  /// Waits 500ms after the last change before writing, to avoid SQLite churn during slider drags.
  /// </summary>
  private void ScheduleVolumePersist()
  {
    if (_configurationManager == null) return;

    lock (_volumePersistLock)
    {
      _volumePersistTimer?.Dispose();
      _volumePersistTimer = new Timer(
        _ => _ = PersistVolumePreferencesAsync(),
        null,
        TimeSpan.FromMilliseconds(500),
        Timeout.InfiniteTimeSpan);
    }
  }

  /// <summary>
  /// Persists current volume, mute, and balance to the configuration store.
  /// </summary>
  private async Task PersistVolumePreferencesAsync()
  {
    if (_configurationManager == null) return;

    try
    {
      var storeId = _configurationManager.CurrentStoreType == ConfigurationStoreType.Sqlite ? "sqlite" : "config";
      var volume = (int)Math.Round(MasterVolume * 100);
      var balance = (int)Math.Round(Balance * 100);

      await _configurationManager.SetValueAsync(storeId, "AudioPreferences:MasterVolume", volume.ToString());
      await _configurationManager.SetValueAsync(storeId, "AudioPreferences:IsMuted", IsMuted.ToString());
      await _configurationManager.SetValueAsync(storeId, "AudioPreferences:Balance", balance.ToString());

      _logger.LogDebug("Persisted volume preferences: Volume={Volume}%, Muted={Muted}, Balance={Balance}%",
        volume, IsMuted, balance);
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Failed to persist volume preferences");
    }
  }

  /// <summary>
  /// Restores volume, mute, and balance from persisted preferences.
  /// Reads from the config store directly (SQLite) since IOptionsMonitor only reflects appsettings.json defaults.
  /// Sets the mixer directly to avoid triggering re-persistence.
  /// </summary>
  private void RestoreVolumePreferences()
  {
    try
    {
      var mixer = _audioEngine.GetMasterMixer();

      // Try to read from config store (SQLite) first — this has the actual persisted runtime values
      int volumePercent = _audioPreferences.CurrentValue.MasterVolume;
      bool isMuted = _audioPreferences.CurrentValue.IsMuted;

      if (_configurationManager != null)
      {
        try
        {
          var storeId = _configurationManager.CurrentStoreType == ConfigurationStoreType.Sqlite ? "sqlite" : "config";
          _logger.LogDebug("Reading volume from config store '{StoreId}'", storeId);
          var store = _configurationManager.GetStoreAsync(storeId).GetAwaiter().GetResult();

          var volEntry = store.GetEntryAsync("AudioPreferences:MasterVolume").GetAwaiter().GetResult();
          _logger.LogDebug("Config store volume entry: {Entry}", volEntry?.Value ?? "null");
          if (volEntry != null && int.TryParse(volEntry.Value, out var storedVol))
            volumePercent = storedVol;

          var muteEntry = store.GetEntryAsync("AudioPreferences:IsMuted").GetAwaiter().GetResult();
          _logger.LogDebug("Config store mute entry: {Entry}", muteEntry?.Value ?? "null");
          if (muteEntry != null && bool.TryParse(muteEntry.Value, out var storedMuted))
            isMuted = storedMuted;
        }
        catch (Exception ex)
        {
          _logger.LogWarning(ex, "Could not read volume from config store, using defaults");
        }
      }
      else
      {
        _logger.LogWarning("No configuration manager available, cannot restore persisted volume");
      }

      mixer.MasterVolume = volumePercent / 100f;
      mixer.IsMuted = isMuted;
      mixer.Balance = 0f; // Always centered — balance control removed from UI

      _logger.LogInformation(
        "Restored volume preferences: Volume={Volume}%, Muted={Muted}",
        volumePercent, isMuted);
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Failed to restore volume preferences");
    }
  }

  /// <summary>
  /// Creates a Bluetooth audio source.
  /// </summary>
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
      _configurationManager,
      _albumArtCache);
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
  /// Subscribes to state changes for the given source for source cleanup
  /// and delegates play history tracking to PlayHistoryTracker.
  /// </summary>
  private void SubscribeToSourceStateChanges(IAudioSource source)
  {
    source.StateChanged += OnSourceStateChanged;
    _playHistoryTracker?.SubscribeToSource(source);
  }

  /// <summary>
  /// Unsubscribes from state changes for the given source.
  /// </summary>
  private void UnsubscribeFromSourceStateChanges(IAudioSource source)
  {
    source.StateChanged -= OnSourceStateChanged;
    _playHistoryTracker?.UnsubscribeFromSource(source);
  }

  /// <summary>
  /// Handles source state changes for previous source cleanup when a new source starts playing.
  /// Play history recording is handled separately by PlayHistoryTracker.
  /// </summary>
  private async void OnSourceStateChanged(object? sender, AudioSourceStateChangedEventArgs e)
  {
    if (sender is not IAudioSource source)
      return;

    _logger.LogInformation(
      "Source state changed: {SourceName} ({SourceType}) {OldState} -> {NewState}, IsActiveSource={IsActive}",
      source.Name, source.Type, e.PreviousState, e.NewState, source == _activeSource);

    // Clean up previous source when active source starts playing
    if (e.NewState == AudioSourceState.Playing && source == _activeSource && _previousSource != null)
    {
      await _switchLock.WaitAsync();
      try
      {
        // Double-check after acquiring lock (state may have changed)
        if (source != _activeSource || _previousSource == null)
          return;

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
  }

  /// <summary>
  /// Auto-switch to Bluetooth when a device connects if enabled.
  /// </summary>
  private async void OnBluetoothDeviceConnected(object? sender, BluetoothDeviceConnectedEventArgs e)
  {
    try
    {
      if (!_bluetoothOptions.CurrentValue.AutoSwitchOnConnect)
      {
        return;
      }

      if (!_bluetoothService.IsAvailable)
      {
        _logger.LogWarning("Bluetooth auto-switch skipped; adapter not available");
        return;
      }

      if (_activeSource?.Type == AudioSourceType.Bluetooth)
      {
        _logger.LogDebug("Bluetooth device connected but Bluetooth is already the active source; skipping switch");
        return;
      }

      await GetOrCreateSourceAsync(AudioSourceType.Bluetooth, switchToSource: true);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to auto-switch to Bluetooth after device connect {Device}", e.Device.Address);
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

    // Dispose play history tracker (unsubscribes from identification + BT metadata events)
    _playHistoryTracker?.Dispose();

    // Unsubscribe Bluetooth events
    _bluetoothService.DeviceConnected -= OnBluetoothDeviceConnected;

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

    // Dispose volume persistence timer
    lock (_volumePersistLock)
    {
      _volumePersistTimer?.Dispose();
      _volumePersistTimer = null;
    }

    _switchLock.Dispose();
    _createLock.Dispose();

    try
    {
      await _bluetoothService.DisposeAsync();
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Error disposing Bluetooth service");
    }

    _logger.LogInformation("AudioManager disposed");
  }
}
