using Microsoft.Extensions.Logging;
using Radio.Infrastructure.Audio.Services;
using SoundFlow.Abstracts;
using SoundFlow.Abstracts.Devices;
using SoundFlow.Components;
using SoundFlow.Enums;
using SoundFlow.Interfaces;
using SoundFlow.Providers;
using SoundFlow.Structs;

namespace Radio.Infrastructure.Audio.SoundFlow;

/// <summary>
/// Service that manages SoundFlow playback for audio sources.
/// Provides methods to play audio through the SoundFlow engine.
/// Visualization is handled exclusively by the MasterMixer-level tap in SoundFlowAudioEngine,
/// which sees post-Volume (post-gain) audio — critical for correct auto-gain normalization.
/// </summary>
public class SoundFlowPlaybackService : IDisposable
{
  private readonly ILogger<SoundFlowPlaybackService> _logger;
  private readonly SoundFlowAudioEngine _audioEngine;
  private readonly Dictionary<string, SoundPlayer> _activePlayers = new();
  private readonly Dictionary<string, SoundComponent> _activeComponents = new();
  private readonly Dictionary<string, float> _baseVolumes = new();
  private readonly Dictionary<string, float> _gainOffsets = new();
  private readonly object _playersLock = new();
  private bool _disposed;

  /// <summary>
  /// Initializes a new instance of the <see cref="SoundFlowPlaybackService"/> class.
  /// </summary>
  public SoundFlowPlaybackService(
    ILogger<SoundFlowPlaybackService> logger,
    SoundFlowAudioEngine audioEngine)
  {
    _logger = logger;
    _audioEngine = audioEngine;

    // Re-attach active components/players when the playback device changes
    _audioEngine.PlaybackDeviceSwitched += OnPlaybackDeviceSwitched;
  }

  /// <summary>
  /// Gets the underlying SoundFlow audio engine.
  /// Use this when creating custom SoundComponents that need engine context.
  /// </summary>
  public AudioEngine? GetUnderlyingEngine() => _audioEngine.GetUnderlyingEngine();

  /// <summary>
  /// Gets the audio format used by the playback service.
  /// Use this when creating custom SoundComponents that need format information.
  /// </summary>
  public AudioFormat GetAudioFormat() => _audioEngine.GetAudioFormat();

  /// <summary>
  /// Plays an audio file through SoundFlow.
  /// </summary>
  /// <param name="sourceId">Unique identifier for this playback.</param>
  /// <param name="filePath">Path to the audio file.</param>
  /// <param name="volume">Volume level (0.0 to 1.0).</param>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>True if playback started successfully.</returns>
  public async Task<bool> PlayFileAsync(
    string sourceId,
    string filePath,
    float volume = 1.0f,
    CancellationToken cancellationToken = default)
  {
    ThrowIfDisposed();

    // Validate file exists
    if (!File.Exists(filePath))
    {
      _logger.LogError("PlayFileAsync: File does not exist: {FilePath}", filePath);
      return false;
    }

    // Get file info for diagnostics
    var fileInfo = new FileInfo(filePath);
    _logger.LogDebug(
      "PlayFileAsync: File info - Path: {FilePath}, Size: {Size} bytes, Extension: {Extension}",
      filePath, fileInfo.Length, fileInfo.Extension);

    var engine = _audioEngine.GetUnderlyingEngine();
    var playbackDevice = _audioEngine.GetPlaybackDevice();
    var format = _audioEngine.GetAudioFormat();

    if (engine == null)
    {
      _logger.LogError("PlayFileAsync: Audio engine is null - engine not initialized");
      return false;
    }

    if (playbackDevice == null)
    {
      _logger.LogError("PlayFileAsync: Playback device is null - no output device available");
      return false;
    }

    _logger.LogDebug(
      "PlayFileAsync: Audio format - SampleRate: {SampleRate}, Channels: {Channels}",
      format.SampleRate, format.Channels);

    FileStream? fileStream = null;
    StreamDataProvider? dataProvider = null;
    SoundPlayer? soundPlayer = null;

    try
    {
      // Stop any existing playback for this source
      await StopAsync(sourceId, cancellationToken);

      _logger.LogDebug("PlayFileAsync: Opening file stream for {FilePath}", filePath);

      // Open the file stream
      fileStream = File.OpenRead(filePath);
      _logger.LogDebug("PlayFileAsync: File stream opened, Length: {Length}", fileStream.Length);

      // Create a data provider from the file
      _logger.LogDebug("PlayFileAsync: Creating StreamDataProvider...");
      dataProvider = new StreamDataProvider(engine, format, fileStream);
      _logger.LogDebug("PlayFileAsync: StreamDataProvider created successfully");

      // Create a sound player (apply gain offset if set)
      _logger.LogDebug("PlayFileAsync: Creating SoundPlayer...");
      soundPlayer = new SoundPlayer(engine, format, dataProvider);
      float gainOffset;
      lock (_playersLock)
      {
        _baseVolumes[sourceId] = volume;
        gainOffset = _gainOffsets.GetValueOrDefault(sourceId, 1.0f);
      }
      soundPlayer.Volume = Math.Clamp(volume * gainOffset, AudioPreferencePersistence.MinGain, AudioPreferencePersistence.MaxGain);
      _logger.LogDebug("PlayFileAsync: SoundPlayer created, Volume: {Volume}, GainOffset: {Gain}", volume, gainOffset);

      // Add to the playback device's mixer
      _logger.LogDebug("PlayFileAsync: Adding to mixer...");
      playbackDevice.MasterMixer.AddComponent(soundPlayer);
      _logger.LogDebug("PlayFileAsync: Added to mixer successfully");

      // Start playback
      _logger.LogDebug("PlayFileAsync: Starting playback...");
      soundPlayer.Play();
      _logger.LogDebug("PlayFileAsync: Playback started, State: {State}", soundPlayer.State);

      // Track the player
      lock (_playersLock)
      {
        _activePlayers[sourceId] = soundPlayer;
      }

      var fileName = Path.GetFileName(filePath);
      _logger.LogInformation(
        "🔊 AUDIO ROUTING COMPLETE: File '{FileName}' now connected to audio output " +
        "(SourceId={SourceId}, SampleRate={SampleRate}Hz, Channels={Channels}, Volume={Volume:P0})",
        fileName, sourceId, format.SampleRate, format.Channels, volume);
      return true;
    }
    catch (FileNotFoundException ex)
    {
      _logger.LogError(ex, "PlayFileAsync: File not found: {FilePath}", filePath);
      CleanupOnError(fileStream, dataProvider, soundPlayer);
      return false;
    }
    catch (UnauthorizedAccessException ex)
    {
      _logger.LogError(ex, "PlayFileAsync: Access denied to file: {FilePath}", filePath);
      CleanupOnError(fileStream, dataProvider, soundPlayer);
      return false;
    }
    catch (IOException ex)
    {
      _logger.LogError(ex, "PlayFileAsync: IO error reading file: {FilePath} - {Message}", filePath, ex.Message);
      CleanupOnError(fileStream, dataProvider, soundPlayer);
      return false;
    }
    catch (InvalidOperationException ex)
    {
      _logger.LogError(ex, "PlayFileAsync: Invalid operation (possibly unsupported format): {FilePath} - {Message}", filePath, ex.Message);
      CleanupOnError(fileStream, dataProvider, soundPlayer);
      return false;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex,
        "PlayFileAsync: Unexpected error playing file: {FilePath} - Type: {ExType}, Message: {Message}",
        filePath, ex.GetType().Name, ex.Message);
      CleanupOnError(fileStream, dataProvider, soundPlayer);
      return false;
    }
  }

  private void CleanupOnError(FileStream? fileStream, StreamDataProvider? dataProvider, SoundPlayer? soundPlayer)
  {
    try
    {
      soundPlayer?.Dispose();
    }
    catch { /* ignore */ }

    try
    {
      if (dataProvider is IDisposable disposable)
        disposable.Dispose();
    }
    catch { /* ignore */ }

    try
    {
      fileStream?.Dispose();
    }
    catch { /* ignore */ }
  }

  /// <summary>
  /// Plays audio from a stream through SoundFlow.
  /// </summary>
  /// <param name="sourceId">Unique identifier for this playback.</param>
  /// <param name="audioStream">The audio stream to play.</param>
  /// <param name="volume">Volume level (0.0 to 1.0).</param>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>True if playback started successfully.</returns>
  public async Task<bool> PlayStreamAsync(
    string sourceId,
    Stream audioStream,
    float volume = 1.0f,
    CancellationToken cancellationToken = default)
  {
    ThrowIfDisposed();

    var engine = _audioEngine.GetUnderlyingEngine();
    var playbackDevice = _audioEngine.GetPlaybackDevice();
    var format = _audioEngine.GetAudioFormat();

    if (engine == null || playbackDevice == null)
    {
      _logger.LogWarning("Audio engine or playback device not initialized");
      return false;
    }

    try
    {
      // Stop any existing playback for this source
      await StopAsync(sourceId, cancellationToken);

      _logger.LogDebug("Starting stream playback for source {SourceId}", sourceId);

      // Create a data provider from the stream
      var dataProvider = new StreamDataProvider(engine, format, audioStream);

      // Create a sound player (apply gain offset if set)
      var soundPlayer = new SoundPlayer(engine, format, dataProvider);
      float streamGainOffset;
      lock (_playersLock)
      {
        _baseVolumes[sourceId] = volume;
        streamGainOffset = _gainOffsets.GetValueOrDefault(sourceId, 1.0f);
      }
      soundPlayer.Volume = Math.Clamp(volume * streamGainOffset, AudioPreferencePersistence.MinGain, AudioPreferencePersistence.MaxGain);

      // Add to the playback device's mixer
      playbackDevice.MasterMixer.AddComponent(soundPlayer);

      // Start playback
      soundPlayer.Play();

      // Track the player
      lock (_playersLock)
      {
        _activePlayers[sourceId] = soundPlayer;
      }

      _logger.LogInformation("Started stream playback for source {SourceId}", sourceId);
      return true;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to start stream playback for source {SourceId}", sourceId);
      return false;
    }
  }

  /// <summary>
  /// Plays audio from an existing data provider through SoundFlow.
  /// </summary>
  /// <param name="sourceId">Unique identifier for this playback.</param>
  /// <param name="dataProvider">The data provider to play.</param>
  /// <param name="volume">Volume level (0.0 to 1.0).</param>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>True if playback started successfully.</returns>
  public async Task<bool> PlayDataProviderAsync(
    string sourceId,
    ISoundDataProvider dataProvider,
    float volume = 1.0f,
    CancellationToken cancellationToken = default)
  {
    ThrowIfDisposed();

    var engine = _audioEngine.GetUnderlyingEngine();
    var playbackDevice = _audioEngine.GetPlaybackDevice();
    var format = _audioEngine.GetAudioFormat();

    if (engine == null || playbackDevice == null)
    {
      _logger.LogWarning("Audio engine or playback device not initialized");
      return false;
    }

    try
    {
      // Stop any existing playback for this source
      await StopAsync(sourceId, cancellationToken);

      _logger.LogDebug("Starting data provider playback for source {SourceId}", sourceId);

      // Create a sound player from the existing data provider (apply gain offset if set)
      var soundPlayer = new SoundPlayer(engine, format, dataProvider);
      float dpGainOffset;
      lock (_playersLock)
      {
        _baseVolumes[sourceId] = volume;
        dpGainOffset = _gainOffsets.GetValueOrDefault(sourceId, 1.0f);
      }
      soundPlayer.Volume = Math.Clamp(volume * dpGainOffset, AudioPreferencePersistence.MinGain, AudioPreferencePersistence.MaxGain);

      // Add to the playback device's mixer
      playbackDevice.MasterMixer.AddComponent(soundPlayer);

      // Start playback
      soundPlayer.Play();

      // Track the player
      lock (_playersLock)
      {
        _activePlayers[sourceId] = soundPlayer;
      }

      _logger.LogInformation("Started data provider playback for source {SourceId}", sourceId);
      return true;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to start data provider playback for source {SourceId}", sourceId);
      return false;
    }
  }

  /// <summary>
  /// Plays audio from a SoundComponent directly through SoundFlow.
  /// Use this for custom audio generators like SDR radio that provide raw PCM samples.
  /// </summary>
  /// <param name="sourceId">Unique identifier for this playback.</param>
  /// <param name="component">The SoundComponent that generates audio.</param>
  /// <param name="volume">Volume level (0.0 to 1.0).</param>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>True if playback started successfully.</returns>
  public async Task<bool> PlayComponentAsync(
    string sourceId,
    SoundComponent component,
    float volume = 1.0f,
    CancellationToken cancellationToken = default)
  {
    ThrowIfDisposed();

    var playbackDevice = _audioEngine.GetPlaybackDevice();

    if (playbackDevice == null)
    {
      _logger.LogWarning("Playback device not initialized");
      return false;
    }

    try
    {
      // Stop any existing playback for this source
      await StopAsync(sourceId, cancellationToken);

      _logger.LogDebug("Starting component playback for source {SourceId}", sourceId);

      // Set volume on the component (apply gain offset if set)
      float compGainOffset;
      lock (_playersLock)
      {
        _baseVolumes[sourceId] = volume;
        compGainOffset = _gainOffsets.GetValueOrDefault(sourceId, 1.0f);
      }
      component.Volume = Math.Clamp(volume * compGainOffset, AudioPreferencePersistence.MinGain, AudioPreferencePersistence.MaxGain);

      // Add to the playback device's mixer
      _logger.LogInformation(
        "🔊 AUDIO ROUTING: Adding component '{ComponentName}' to SoundFlow mixer (SourceId={SourceId}, Volume={Volume:P0})",
        component.Name ?? component.GetType().Name, sourceId, volume);

      playbackDevice.MasterMixer.AddComponent(component);

      // Track the component
      lock (_playersLock)
      {
        _activeComponents[sourceId] = component;
      }

      _logger.LogInformation(
        "🔊 AUDIO ROUTING COMPLETE: Component '{ComponentName}' now connected to audio output (SourceId={SourceId})",
        component.Name ?? component.GetType().Name, sourceId);
      return true;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to start component playback for source {SourceId}", sourceId);
      return false;
    }
  }

  /// <summary>
  /// Re-attaches all active players and components to the new playback device's mixer.
  /// Called when the audio engine switches to a different output device.
  /// </summary>
  private void OnPlaybackDeviceSwitched(object? sender, AudioPlaybackDevice newDevice)
  {
    lock (_playersLock)
    {
      var playerCount = _activePlayers.Count;
      var componentCount = _activeComponents.Count;

      if (playerCount == 0 && componentCount == 0)
      {
        _logger.LogDebug("PlaybackDeviceSwitched: No active players or components to re-attach");
        return;
      }

      _logger.LogInformation(
        "PlaybackDeviceSwitched: Re-attaching {PlayerCount} players and {ComponentCount} components to new device",
        playerCount, componentCount);

      foreach (var (sourceId, player) in _activePlayers)
      {
        try
        {
          newDevice.MasterMixer.AddComponent(player);
          _logger.LogInformation("Re-attached player to new device (SourceId={SourceId})", sourceId);
        }
        catch (Exception ex)
        {
          _logger.LogError(ex, "Failed to re-attach player (SourceId={SourceId})", sourceId);
        }
      }

      foreach (var (sourceId, component) in _activeComponents)
      {
        try
        {
          newDevice.MasterMixer.AddComponent(component);
          _logger.LogInformation(
            "Re-attached component '{ComponentName}' to new device (SourceId={SourceId})",
            component.Name ?? component.GetType().Name, sourceId);
        }
        catch (Exception ex)
        {
          _logger.LogError(ex, "Failed to re-attach component (SourceId={SourceId})", sourceId);
        }
      }
    }
  }

  /// <summary>
  /// Stops playback for a specific source.
  /// </summary>
  /// <param name="sourceId">The source identifier.</param>
  /// <param name="cancellationToken">Cancellation token.</param>
  public Task StopAsync(string sourceId, CancellationToken cancellationToken = default)
  {
    ThrowIfDisposed();

    SoundPlayer? player = null;
    SoundComponent? component = null;
    lock (_playersLock)
    {
      if (_activePlayers.TryGetValue(sourceId, out player))
      {
        _activePlayers.Remove(sourceId);
      }
      if (_activeComponents.TryGetValue(sourceId, out component))
      {
        _activeComponents.Remove(sourceId);
      }
      _baseVolumes.Remove(sourceId);
      // Keep _gainOffsets — they persist across stop/start for the same source
    }

    var playbackDevice = _audioEngine.GetPlaybackDevice();

    if (player != null)
    {
      try
      {
        _logger.LogInformation(
          "🔇 AUDIO ROUTING: Removing player from SoundFlow mixer (SourceId={SourceId})",
          sourceId);

        player.Stop();
        playbackDevice?.MasterMixer.RemoveComponent(player);
        player.Dispose();
        _logger.LogInformation(
          "🔇 AUDIO ROUTING REMOVED: Player disconnected from audio output (SourceId={SourceId})",
          sourceId);
      }
      catch (Exception ex)
      {
        _logger.LogWarning(ex, "Error stopping player playback for source {SourceId}", sourceId);
      }
    }

    if (component != null)
    {
      try
      {
        var componentName = component.Name ?? component.GetType().Name;
        _logger.LogInformation(
          "🔇 AUDIO ROUTING: Removing component '{ComponentName}' from SoundFlow mixer (SourceId={SourceId})",
          componentName, sourceId);

        playbackDevice?.MasterMixer.RemoveComponent(component);
        component.Dispose();
        _logger.LogInformation(
          "🔇 AUDIO ROUTING REMOVED: Component '{ComponentName}' disconnected from audio output (SourceId={SourceId})",
          componentName, sourceId);
      }
      catch (Exception ex)
      {
        _logger.LogWarning(ex, "Error stopping component playback for source {SourceId}", sourceId);
      }
    }

    return Task.CompletedTask;
  }

  /// <summary>
  /// Pauses playback for a specific source.
  /// </summary>
  /// <param name="sourceId">The source identifier.</param>
  public void Pause(string sourceId)
  {
    ThrowIfDisposed();

    lock (_playersLock)
    {
      if (_activePlayers.TryGetValue(sourceId, out var player))
      {
        player.Pause();
        _logger.LogDebug("Paused playback for source {SourceId}", sourceId);
      }
    }
  }

  /// <summary>
  /// Resumes playback for a specific source.
  /// </summary>
  /// <param name="sourceId">The source identifier.</param>
  public void Resume(string sourceId)
  {
    ThrowIfDisposed();

    lock (_playersLock)
    {
      if (_activePlayers.TryGetValue(sourceId, out var player))
      {
        player.Play();
        _logger.LogDebug("Resumed playback for source {SourceId}", sourceId);
      }
    }
  }

  /// <summary>
  /// Sets the volume for a specific source. The effective volume is base volume * gain offset.
  /// </summary>
  /// <param name="sourceId">The source identifier.</param>
  /// <param name="volume">Volume level (0.0 to 1.0).</param>
  public void SetVolume(string sourceId, float volume)
  {
    ThrowIfDisposed();

    lock (_playersLock)
    {
      _baseVolumes[sourceId] = Math.Clamp(volume, 0f, 1f);
      var gainOffset = _gainOffsets.GetValueOrDefault(sourceId, 1.0f);
      var effective = Math.Clamp(volume * gainOffset, AudioPreferencePersistence.MinGain, AudioPreferencePersistence.MaxGain);

      if (_activePlayers.TryGetValue(sourceId, out var player))
      {
        player.Volume = effective;
      }
      if (_activeComponents.TryGetValue(sourceId, out var component))
      {
        component.Volume = effective;
      }
    }
  }

  /// <summary>
  /// Sets the gain offset for a specific source. The effective volume is base volume * gain offset.
  /// </summary>
  /// <param name="sourceId">The source identifier.</param>
  /// <param name="gainOffset">Gain offset (0.0 to 2.0, where 1.0 = unity/0dB).</param>
  public void SetGainOffset(string sourceId, float gainOffset)
  {
    ThrowIfDisposed();

    lock (_playersLock)
    {
      gainOffset = Math.Clamp(gainOffset, AudioPreferencePersistence.MinGain, AudioPreferencePersistence.MaxGain);
      _gainOffsets[sourceId] = gainOffset;
      var baseVol = _baseVolumes.GetValueOrDefault(sourceId, 1.0f);
      var effective = Math.Clamp(baseVol * gainOffset, AudioPreferencePersistence.MinGain, AudioPreferencePersistence.MaxGain);

      if (_activePlayers.TryGetValue(sourceId, out var player))
      {
        player.Volume = effective;
        _logger.LogDebug("Applied gain offset {Gain:F2} to player (SourceId={SourceId}, EffectiveVolume={Volume:F2})",
          gainOffset, sourceId, effective);
      }
      if (_activeComponents.TryGetValue(sourceId, out var component))
      {
        component.Volume = effective;
        _logger.LogDebug("Applied gain offset {Gain:F2} to component (SourceId={SourceId}, EffectiveVolume={Volume:F2})",
          gainOffset, sourceId, effective);
      }
    }
  }

  /// <summary>
  /// Checks if a source is currently playing.
  /// </summary>
  /// <param name="sourceId">The source identifier.</param>
  /// <returns>True if the source is playing.</returns>
  public bool IsPlaying(string sourceId)
  {
    lock (_playersLock)
    {
      if (_activePlayers.TryGetValue(sourceId, out var player))
      {
        return player.State == PlaybackState.Playing;
      }
    }
    return false;
  }

  /// <summary>
  /// Gets the current playback position for a source.
  /// </summary>
  /// <param name="sourceId">The source identifier.</param>
  /// <returns>The current position, or null if not playing.</returns>
  public TimeSpan? GetPosition(string sourceId)
  {
    lock (_playersLock)
    {
      if (_activePlayers.TryGetValue(sourceId, out var player))
      {
        // Try to get position from data provider if available
        return TimeSpan.Zero; // Position tracking not available in current SoundFlow API
      }
    }
    return null;
  }

  /// <summary>
  /// Gets diagnostic info about active players and components (for debug markers).
  /// </summary>
  public (int ActivePlayers, int ActiveComponents, string[] PlayerIds) GetDiagnostics()
  {
    lock (_playersLock)
    {
      return (_activePlayers.Count, _activeComponents.Count, _activePlayers.Keys.ToArray());
    }
  }

  /// <summary>
  /// Stops all active playback.
  /// </summary>
  public void StopAll()
  {
    // Don't throw if disposed - just return
    if (_disposed) return;

    List<string> sourceIds;
    lock (_playersLock)
    {
      sourceIds = _activePlayers.Keys.Concat(_activeComponents.Keys).Distinct().ToList();
    }

    foreach (var sourceId in sourceIds)
    {
      _ = StopAsync(sourceId);
    }
  }

  private void ThrowIfDisposed()
  {
    ObjectDisposedException.ThrowIf(_disposed, this);
  }

  /// <inheritdoc/>
  public void Dispose()
  {
    if (_disposed) return;

    _audioEngine.PlaybackDeviceSwitched -= OnPlaybackDeviceSwitched;

    // Stop all first, then set disposed
    lock (_playersLock)
    {
      foreach (var player in _activePlayers.Values)
      {
        try
        {
          player.Stop();
          player.Dispose();
        }
        catch
        {
          // Ignore disposal errors
        }
      }
      _activePlayers.Clear();

      foreach (var component in _activeComponents.Values)
      {
        try
        {
          component.Dispose();
        }
        catch
        {
          // Ignore disposal errors
        }
      }
      _activeComponents.Clear();
    }

    _disposed = true;
  }
}
