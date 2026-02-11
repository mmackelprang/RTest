using Microsoft.Extensions.Logging;
using Radio.Core.Interfaces.Audio;
using SoundFlow.Abstracts;
using SoundFlow.Components;
using SoundFlow.Enums;
using SoundFlow.Interfaces;
using SoundFlow.Providers;
using SoundFlow.Structs;

namespace Radio.Infrastructure.Audio.SoundFlow;

/// <summary>
/// Service that manages SoundFlow playback for audio sources.
/// Provides methods to play audio through the SoundFlow engine.
/// </summary>
public class SoundFlowPlaybackService : IDisposable
{
  private readonly ILogger<SoundFlowPlaybackService> _logger;
  private readonly SoundFlowAudioEngine _audioEngine;
  private readonly IVisualizerService? _visualizerService;
  private readonly Dictionary<string, SoundPlayer> _activePlayers = new();
  private readonly Dictionary<string, SoundComponent> _activeComponents = new();
  private readonly Dictionary<string, VisualizationTapModifier> _visualizationTaps = new();
  private readonly object _playersLock = new();
  private bool _disposed;

  /// <summary>
  /// Initializes a new instance of the <see cref="SoundFlowPlaybackService"/> class.
  /// </summary>
  public SoundFlowPlaybackService(
    ILogger<SoundFlowPlaybackService> logger,
    SoundFlowAudioEngine audioEngine,
    IVisualizerService? visualizerService = null)
  {
    _logger = logger;
    _audioEngine = audioEngine;
    _visualizerService = visualizerService;

    if (_visualizerService != null)
    {
      _logger.LogInformation("SoundFlowPlaybackService initialized with visualization tap support");
    }
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

      // Create a sound player
      _logger.LogDebug("PlayFileAsync: Creating SoundPlayer...");
      soundPlayer = new SoundPlayer(engine, format, dataProvider);
      soundPlayer.Volume = volume;
      _logger.LogDebug("PlayFileAsync: SoundPlayer created, Volume: {Volume}", volume);

      // Add visualization tap if visualizer service is available
      VisualizationTapModifier? tapModifier = null;
      if (_visualizerService != null)
      {
        try
        {
          tapModifier = new VisualizationTapModifier(_visualizerService, format);
          soundPlayer.AddModifier(tapModifier);
          _logger.LogDebug("PlayFileAsync: Added visualization tap modifier");
        }
        catch (Exception ex)
        {
          _logger.LogWarning(ex, "PlayFileAsync: Failed to add visualization tap modifier");
          tapModifier = null;
        }
      }

      // Add to the playback device's mixer
      _logger.LogDebug("PlayFileAsync: Adding to mixer...");
      playbackDevice.MasterMixer.AddComponent(soundPlayer);
      _logger.LogDebug("PlayFileAsync: Added to mixer successfully");

      // Start playback
      _logger.LogDebug("PlayFileAsync: Starting playback...");
      soundPlayer.Play();
      _logger.LogDebug("PlayFileAsync: Playback started, State: {State}", soundPlayer.State);

      // Track the player and tap
      lock (_playersLock)
      {
        _activePlayers[sourceId] = soundPlayer;
        if (tapModifier != null)
        {
          _visualizationTaps[sourceId] = tapModifier;
        }
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

      // Create a sound player
      var soundPlayer = new SoundPlayer(engine, format, dataProvider);
      soundPlayer.Volume = volume;

      // Add visualization tap if visualizer service is available
      VisualizationTapModifier? tapModifier = null;
      if (_visualizerService != null)
      {
        try
        {
          tapModifier = new VisualizationTapModifier(_visualizerService, format);
          soundPlayer.AddModifier(tapModifier);
          _logger.LogDebug("PlayStreamAsync: Added visualization tap modifier");
        }
        catch (Exception ex)
        {
          _logger.LogWarning(ex, "PlayStreamAsync: Failed to add visualization tap modifier");
          tapModifier = null;
        }
      }

      // Add to the playback device's mixer
      playbackDevice.MasterMixer.AddComponent(soundPlayer);

      // Start playback
      soundPlayer.Play();

      // Track the player and tap
      lock (_playersLock)
      {
        _activePlayers[sourceId] = soundPlayer;
        if (tapModifier != null)
        {
          _visualizationTaps[sourceId] = tapModifier;
        }
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

      // Create a sound player from the existing data provider
      var soundPlayer = new SoundPlayer(engine, format, dataProvider);
      soundPlayer.Volume = volume;

      // Add visualization tap if visualizer service is available
      VisualizationTapModifier? tapModifier = null;
      if (_visualizerService != null)
      {
        try
        {
          tapModifier = new VisualizationTapModifier(_visualizerService, format);
          soundPlayer.AddModifier(tapModifier);
          _logger.LogDebug("PlayDataProviderAsync: Added visualization tap modifier");
        }
        catch (Exception ex)
        {
          _logger.LogWarning(ex, "PlayDataProviderAsync: Failed to add visualization tap modifier");
          tapModifier = null;
        }
      }

      // Add to the playback device's mixer
      playbackDevice.MasterMixer.AddComponent(soundPlayer);

      // Start playback
      soundPlayer.Play();

      // Track the player and tap
      lock (_playersLock)
      {
        _activePlayers[sourceId] = soundPlayer;
        if (tapModifier != null)
        {
          _visualizationTaps[sourceId] = tapModifier;
        }
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

      // Set volume on the component
      component.Volume = volume;

      // Add visualization tap if visualizer service is available
      VisualizationTapModifier? tapModifier = null;
      if (_visualizerService != null)
      {
        try
        {
          tapModifier = new VisualizationTapModifier(_visualizerService, component.Format);
          component.AddModifier(tapModifier);
          _logger.LogDebug("PlayComponentAsync: Added visualization tap modifier to {ComponentName}", component.Name ?? component.GetType().Name);
        }
        catch (Exception ex)
        {
          _logger.LogWarning(ex, "PlayComponentAsync: Failed to add visualization tap modifier");
          tapModifier = null;
        }
      }

      // Add to the playback device's mixer
      _logger.LogInformation(
        "🔊 AUDIO ROUTING: Adding component '{ComponentName}' to SoundFlow mixer (SourceId={SourceId}, Volume={Volume:P0})",
        component.Name ?? component.GetType().Name, sourceId, volume);

      playbackDevice.MasterMixer.AddComponent(component);

      // Track the component and tap
      lock (_playersLock)
      {
        _activeComponents[sourceId] = component;
        if (tapModifier != null)
        {
          _visualizationTaps[sourceId] = tapModifier;
        }
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
  /// Stops playback for a specific source.
  /// </summary>
  /// <param name="sourceId">The source identifier.</param>
  /// <param name="cancellationToken">Cancellation token.</param>
  public Task StopAsync(string sourceId, CancellationToken cancellationToken = default)
  {
    ThrowIfDisposed();

    SoundPlayer? player = null;
    SoundComponent? component = null;
    VisualizationTapModifier? tapModifier = null;
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
      if (_visualizationTaps.TryGetValue(sourceId, out tapModifier))
      {
        _visualizationTaps.Remove(sourceId);
      }
    }

    var playbackDevice = _audioEngine.GetPlaybackDevice();

    if (player != null)
    {
      try
      {
        _logger.LogInformation(
          "🔇 AUDIO ROUTING: Removing player from SoundFlow mixer (SourceId={SourceId})",
          sourceId);

        // Flush any remaining visualization data
        tapModifier?.Flush();

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

        // Flush any remaining visualization data
        tapModifier?.Flush();

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
  /// Sets the volume for a specific source.
  /// </summary>
  /// <param name="sourceId">The source identifier.</param>
  /// <param name="volume">Volume level (0.0 to 1.0).</param>
  public void SetVolume(string sourceId, float volume)
  {
    ThrowIfDisposed();

    lock (_playersLock)
    {
      if (_activePlayers.TryGetValue(sourceId, out var player))
      {
        player.Volume = Math.Clamp(volume, 0f, 1f);
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

    // Stop all first, then set disposed
    lock (_playersLock)
    {
      // Flush and clear visualization taps
      foreach (var tap in _visualizationTaps.Values)
      {
        try
        {
          tap.Flush();
        }
        catch
        {
          // Ignore flush errors
        }
      }
      _visualizationTaps.Clear();

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
