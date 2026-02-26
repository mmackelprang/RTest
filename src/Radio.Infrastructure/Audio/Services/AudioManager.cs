using Microsoft.Extensions.Logging;
using Radio.Core.Events;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Models.Audio;
using Radio.Infrastructure.Audio.Fingerprinting;
using Radio.Infrastructure.Audio.SoundFlow;

namespace Radio.Infrastructure.Audio.Services;

/// <summary>
/// Central coordinator for audio sources and playback.
/// Manages source lifecycle, switching, and mixer integration.
/// </summary>
public class AudioManager : IAudioManager, IAsyncDisposable
{
  private readonly ILogger<AudioManager> _logger;
  private readonly IAudioEngine _audioEngine;
  private readonly IAudioSourceFactory _sourceFactory;
  private readonly BackgroundIdentificationService? _identificationService;
  private readonly AudioPreferencePersistence? _preferencePersistence;
  private readonly PlayHistoryTracker? _playHistoryTracker;
  private readonly SoundFlowPlaybackService? _playbackService;

  // State
  private IAudioSource? _activeSource;
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
    IAudioEngine audioEngine,
    IAudioSourceFactory sourceFactory,
    BackgroundIdentificationService? identificationService = null,
    AudioPreferencePersistence? preferencePersistence = null,
    PlayHistoryTracker? playHistoryTracker = null,
    SoundFlowPlaybackService? playbackService = null)
  {
    _logger = logger;
    _audioEngine = audioEngine;
    _sourceFactory = sourceFactory;
    _identificationService = identificationService;
    _preferencePersistence = preferencePersistence;
    _playHistoryTracker = playHistoryTracker;
    _playbackService = playbackService;
  }

  /// <inheritdoc/>
  public IAudioEngine Engine => _audioEngine;

  /// <inheritdoc/>
  public IAudioSource? ActiveSource => _activeSource;

  /// <inheritdoc/>
  public float MasterVolume
  {
    get => _audioEngine.GetMasterMixer().MasterVolume;
    set
    {
      _audioEngine.GetMasterMixer().MasterVolume = value;
      _preferencePersistence?.ScheduleVolumePersist();
    }
  }

  /// <inheritdoc/>
  public bool IsMuted
  {
    get => _audioEngine.GetMasterMixer().IsMuted;
    set
    {
      _audioEngine.GetMasterMixer().IsMuted = value;
      _preferencePersistence?.ScheduleVolumePersist();
    }
  }

  /// <inheritdoc/>
  public float Balance
  {
    get => _audioEngine.GetMasterMixer().Balance;
    set
    {
      _audioEngine.GetMasterMixer().Balance = value;
      _preferencePersistence?.ScheduleVolumePersist();
    }
  }

  /// <inheritdoc/>
  public float GetSourceGain(AudioSourceType sourceType)
  {
    return _preferencePersistence?.GetSourceGain(sourceType) ?? 1.0f;
  }

  /// <inheritdoc/>
  public void SetSourceGain(AudioSourceType sourceType, float gain)
  {
    gain = Math.Clamp(gain, AudioPreferencePersistence.MinGain, AudioPreferencePersistence.MaxGain);
    _preferencePersistence?.SetSourceGain(sourceType, gain);

    // If this source is currently active, update the live playback component gain
    if (_activeSource != null && _activeSource.Type == sourceType && _playbackService != null)
    {
      _playbackService.SetGainOffset(_activeSource.Id, gain);
      _logger.LogInformation(
        "Applied live gain offset {Gain:F2} to active source {SourceName}",
        gain, _activeSource.Name);
    }
  }

  /// <inheritdoc/>
  public Dictionary<string, float> GetAllSourceGains()
  {
    return _preferencePersistence?.GetAllSourceGains() ?? new Dictionary<string, float>();
  }

  /// <inheritdoc/>
  public void SetSourceGainInternal(AudioSourceType sourceType, float gain)
  {
    gain = Math.Clamp(gain, AudioPreferencePersistence.MinGain, AudioPreferencePersistence.MaxGain);

    // If this source is currently active, update the live playback component gain
    if (_activeSource != null && _activeSource.Type == sourceType && _playbackService != null)
    {
      _playbackService.SetGainOffset(_activeSource.Id, gain);
      _logger.LogDebug(
        "Applied auto-gain offset {Gain:F2} to active source {SourceName}",
        gain, _activeSource.Name);
    }
  }

  /// <inheritdoc/>
  public void ResetSourceGainToAuto(AudioSourceType sourceType)
  {
    if (_preferencePersistence == null) return;

    // Clear learned data so the source re-learns from scratch at unity gain
    _preferencePersistence.ClearSourceLearnedRms(sourceType);
    _preferencePersistence.SetSourceGainMode(sourceType, "auto");

    // Reset to unity gain — the learning service will re-measure and adjust
    _preferencePersistence.SetSourceGainInternal(sourceType, 1.0f);
    if (_activeSource != null && _activeSource.Type == sourceType && _playbackService != null)
    {
      _playbackService.SetGainOffset(_activeSource.Id, 1.0f);
    }

    _logger.LogInformation("Reset {SourceType} to auto gain (cleared learned data, unity)", sourceType);
  }

  /// <inheritdoc/>
  public Dictionary<string, AutoGainInfo> GetAutoGainStatus()
  {
    return _preferencePersistence?.GetAutoGainStatus() ?? new Dictionary<string, AutoGainInfo>();
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
    _preferencePersistence?.RestoreVolumePreferences();

    // Restore per-source gain offsets from persisted preferences
    _preferencePersistence?.RestoreSourceGainOffsets();

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

      // Stop the old source immediately to prevent simultaneous playback.
      // Only one primary source should ever produce audio at a time.
      if (oldSource != null && oldSource != source &&
          (oldSource.State == AudioSourceState.Playing ||
           oldSource.State == AudioSourceState.Paused))
      {
        _logger.LogInformation(
          "Stopping old source {OldSource} before switching to {NewSource}",
          oldSource.Name, source.Name);

        try
        {
          if (oldSource is IPrimaryAudioSource oldPrimary)
          {
            await oldPrimary.StopAsync(cancellationToken);
          }

          // Remove from mixer so its audio components are disconnected
          if (mixer.GetActiveSources().Contains(oldSource))
          {
            mixer.RemoveSource(oldSource);
            _logger.LogInformation("Removed old source {SourceName} from mixer", oldSource.Name);
          }
        }
        catch (Exception ex)
        {
          _logger.LogWarning(ex, "Error stopping old source {SourceName} during switch", oldSource.Name);
        }
      }

      // Ensure the new source is in the mixer
      var currentActiveSources = mixer.GetActiveSources();
      if (!currentActiveSources.Contains(source))
      {
        _logger.LogInformation("Adding new source {SourceName} to mixer", source.Name);
        mixer.AddSource(source);
      }

      // Update the active source reference
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
        }
        else if (!canAutoPlay)
        {
          _logger.LogInformation(
            "Source {SourceName} requires content selection before playback",
            source.Name);
        }
      }

      // Apply per-source gain offset (auto-gain aware)
      if (_playbackService != null && _preferencePersistence != null)
      {
        var mode = _preferencePersistence.GetSourceGainMode(source.Type);
        float gain;

        if (mode == "auto")
        {
          // In auto mode: use learned gain if available, else unity
          var learnedRms = _preferencePersistence.GetSourceLearnedRms(source.Type);
          if (learnedRms.HasValue && learnedRms.Value > 0.001f)
          {
            gain = Math.Clamp(AudioPreferencePersistence.TargetRms / learnedRms.Value, 0.1f, AudioPreferencePersistence.MaxGain);
            _preferencePersistence.SetSourceGainInternal(source.Type, gain);
          }
          else
          {
            gain = _preferencePersistence.GetSourceGain(source.Type);
          }
        }
        else
        {
          // Manual mode: use the user's stored value
          gain = _preferencePersistence.GetSourceGain(source.Type);
        }

        _playbackService.SetGainOffset(source.Id, gain);
        _logger.LogDebug("Applied gain offset {Gain:F2} ({Mode}) for source {SourceName} ({SourceType})",
          gain, mode, source.Name, source.Type);
      }

      // Persist the source selection
      if (_preferencePersistence != null)
      {
        await _preferencePersistence.PersistSourcePreferenceAsync(source.Type, cancellationToken);
      }

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

      try
      {
        source = _sourceFactory.CreateSource(sourceType);
      }
      catch (ArgumentOutOfRangeException)
      {
        _logger.LogWarning("Source type {SourceType} is not supported", sourceType);
        return null;
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

      // Subscribe to state changes for source cleanup and play history tracking
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
  /// Handles source state changes. Logs state transitions for diagnostics.
  /// Play history recording is handled separately by PlayHistoryTracker.
  /// </summary>
  private void OnSourceStateChanged(object? sender, AudioSourceStateChangedEventArgs e)
  {
    if (sender is not IAudioSource source)
      return;

    _logger.LogInformation(
      "Source state changed: {SourceName} ({SourceType}) {OldState} -> {NewState}, IsActiveSource={IsActive}",
      source.Name, source.Type, e.PreviousState, e.NewState, source == _activeSource);
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

    // Dispose preference persistence (timer cleanup)
    _preferencePersistence?.Dispose();

    _switchLock.Dispose();
    _createLock.Dispose();

    _logger.LogInformation("AudioManager disposed");
  }
}
