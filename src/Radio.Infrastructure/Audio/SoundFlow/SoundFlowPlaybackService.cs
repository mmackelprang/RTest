using Microsoft.Extensions.Logging;
using Radio.Core.Interfaces.Audio;
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
  private readonly Dictionary<string, SoundPlayer> _activePlayers = new();
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
  }

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

      _logger.LogDebug("Starting file playback: {FilePath}", filePath);

      // Open the file stream
      var fileStream = File.OpenRead(filePath);

      // Create a data provider from the file
      var dataProvider = new StreamDataProvider(engine, format, fileStream);

      // Create a sound player
      var soundPlayer = new SoundPlayer(engine, format, dataProvider);
      soundPlayer.Volume = volume;

      // Add to the playback device's mixer
      playbackDevice.MasterMixer.AddComponent(soundPlayer);

      // Start playback
      soundPlayer.Play();

      // Track the player
      lock (_playersLock)
      {
        _activePlayers[sourceId] = soundPlayer;
      }

      _logger.LogInformation("Started playback for source {SourceId}: {FilePath}", sourceId, filePath);
      return true;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to start playback for {FilePath}", filePath);
      return false;
    }
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

      // Create a sound player from the existing data provider
      var soundPlayer = new SoundPlayer(engine, format, dataProvider);
      soundPlayer.Volume = volume;

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
  /// Stops playback for a specific source.
  /// </summary>
  /// <param name="sourceId">The source identifier.</param>
  /// <param name="cancellationToken">Cancellation token.</param>
  public Task StopAsync(string sourceId, CancellationToken cancellationToken = default)
  {
    ThrowIfDisposed();

    SoundPlayer? player = null;
    lock (_playersLock)
    {
      if (_activePlayers.TryGetValue(sourceId, out player))
      {
        _activePlayers.Remove(sourceId);
      }
    }

    if (player != null)
    {
      try
      {
        player.Stop();

        var playbackDevice = _audioEngine.GetPlaybackDevice();
        playbackDevice?.MasterMixer.RemoveComponent(player);

        player.Dispose();
        _logger.LogDebug("Stopped playback for source {SourceId}", sourceId);
      }
      catch (Exception ex)
      {
        _logger.LogWarning(ex, "Error stopping playback for source {SourceId}", sourceId);
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
      sourceIds = _activePlayers.Keys.ToList();
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
    }

    _disposed = true;
  }
}
