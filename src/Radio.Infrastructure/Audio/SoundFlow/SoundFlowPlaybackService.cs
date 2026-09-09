using Microsoft.Extensions.Logging;
using Radio.Core.Extensions;
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
  private readonly Dictionary<string, float> _duckingMultipliers = new();
  private readonly object _playersLock = new();
  private bool _disposed;

  // Audio flow health monitoring
  private CancellationTokenSource? _flowMonitorCts;
  private readonly Dictionary<string, long> _lastOutputSamples = new();

  /// <summary>
  /// Fired when a generator is detected as stalled — receiving samples but not outputting.
  /// The string parameter is the sourceId of the stalled component.
  /// </summary>
  public event Action<string>? GeneratorStalled;

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

    // Start audio flow health monitor
    _flowMonitorCts = new CancellationTokenSource();
    _ = MonitorAudioFlowAsync(_flowMonitorCts.Token);
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
      var fileDuckMult = _duckingMultipliers.GetValueOrDefault(sourceId, 1.0f);
      soundPlayer.Volume = Math.Clamp(volume * gainOffset * fileDuckMult, AudioPreferencePersistence.MinGain, AudioPreferencePersistence.MaxGain);
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
      {
        disposable.Dispose();
      }
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
      var streamDuckMult = _duckingMultipliers.GetValueOrDefault(sourceId, 1.0f);
      soundPlayer.Volume = Math.Clamp(volume * streamGainOffset * streamDuckMult, AudioPreferencePersistence.MinGain, AudioPreferencePersistence.MaxGain);

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
      var dpDuckMult = _duckingMultipliers.GetValueOrDefault(sourceId, 1.0f);
      soundPlayer.Volume = Math.Clamp(volume * dpGainOffset * dpDuckMult, AudioPreferencePersistence.MinGain, AudioPreferencePersistence.MaxGain);

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
      var compDuckMult = _duckingMultipliers.GetValueOrDefault(sourceId, 1.0f);
      component.Volume = Math.Clamp(volume * compGainOffset * compDuckMult, AudioPreferencePersistence.MinGain, AudioPreferencePersistence.MaxGain);

      // Add to the playback device's mixer
      var generatorId = (component as BufferedSoundGenerator<float>)?.GeneratorId;
      _logger.LogInformation(
        "🔊 AUDIO ROUTING: Adding component '{ComponentName}' to SoundFlow mixer (SourceId={SourceId}, Volume={Volume:P0}, GeneratorId={GeneratorId})",
        component.Name ?? component.GetType().Name, sourceId, volume, generatorId?.ToString() ?? "n/a");

      playbackDevice.MasterMixer.AddComponent(component);

      // Track the component
      lock (_playersLock)
      {
        _activeComponents[sourceId] = component;
      }

      _logger.LogInformation(
        "🔊 AUDIO ROUTING COMPLETE: Component '{ComponentName}' now connected to audio output (SourceId={SourceId}, GeneratorId={GeneratorId})",
        component.Name ?? component.GetType().Name, sourceId, generatorId?.ToString() ?? "n/a");
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
      _duckingMultipliers.Remove(sourceId);
      // Keep _gainOffsets: they persist across a stop/start cycle for the same source.
      //
      // ⚠ This is true only because the playback key is the source's IAudioSource.Id, which
      // AudioSourceBase caches in _id on first read (AudioSourceBase.cs:28) and is therefore stable
      // for the lifetime of the source INSTANCE. Before AUD-2 the claim was FALSE for SDR and
      // FilePlayer, which minted a fresh GUID on every PlayCoreAsync: the retained offset was never
      // found again after a restart, AND every cycle leaked one entry here that nothing would ever
      // read or remove. If a future source registers under anything other than Id, this line stops
      // being true again — PlaybackKeyLintTests is what keeps that from happening quietly.
      //
      // ⚠ Per INSTANCE, not per source TYPE, and the distinction is not pedantry: _id is an instance
      // field, so disposing and recreating a source (a device re-enumeration, say) mints a new Id
      // and orphans the old instance's entry here. That orphan is pre-existing, is bounded by the
      // number of source instances ever created, and is NOT fixed by AUD-2.
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
        var generatorId = (component as BufferedSoundGenerator<float>)?.GeneratorId;
        _logger.LogInformation(
          "🔇 AUDIO ROUTING: Removing component '{ComponentName}' from SoundFlow mixer (SourceId={SourceId}, GeneratorId={GeneratorId})",
          componentName, sourceId, generatorId?.ToString() ?? "n/a");

        playbackDevice?.MasterMixer.RemoveComponent(component);
        component.Dispose();
        _logger.LogInformation(
          "🔇 AUDIO ROUTING REMOVED: Component '{ComponentName}' disconnected from audio output (SourceId={SourceId}, GeneratorId={GeneratorId})",
          componentName, sourceId, generatorId?.ToString() ?? "n/a");
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
  /// Seeks a source to an absolute position from the start of its content.
  /// </summary>
  /// <param name="sourceId">The source identifier.</param>
  /// <param name="position">The position to seek to, from the beginning of the content.</param>
  /// <returns>
  /// True when the player repositioned. False in exactly three cases: the position is negative,
  /// no player is registered under this id, OR the player's data provider refused the seek —
  /// SoundPlayerBase.Seek returns a bool and this method propagates it rather than reporting an
  /// unconditional success. A caller's IsSeekable contract depends on that difference being
  /// visible.
  /// </returns>
  public bool Seek(string sourceId, TimeSpan position)
  {
    ThrowIfDisposed();

    if (position < TimeSpan.Zero)
    {
      return false;
    }

    lock (_playersLock)
    {
      if (!_activePlayers.TryGetValue(sourceId, out var player))
      {
        return false;
      }

      // SoundPlayerBase.Seek(TimeSpan, SeekOrigin = Begin).
      var moved = player.Seek(position);
      _logger.LogDebug(
        "Seek for source {SourceId} to {Position} returned {Moved}", sourceId, position, moved);
      return moved;
    }
  }

  /// <summary>
  /// Sets the volume for a specific source. The effective volume is base volume * gain offset.
  /// </summary>
  /// <param name="sourceId">The source identifier.</param>
  /// <param name="volume">Volume level (0.0 to 1.0).</param>
  /// <returns>
  /// True if a live player or component received the new volume. See <see cref="ApplyEffectiveVolume"/>
  /// for why a false return is not by itself a defect.
  /// </returns>
  public bool SetVolume(string sourceId, float volume)
  {
    ThrowIfDisposed();

    lock (_playersLock)
    {
      _baseVolumes[sourceId] = Math.Clamp(volume, 0f, 1f);
      return ApplyEffectiveVolume(sourceId);
    }
  }

  /// <summary>
  /// Sets the gain offset for a specific source. The effective volume is base volume * gain offset.
  /// </summary>
  /// <param name="sourceId">The source identifier.</param>
  /// <param name="gainOffset">Gain offset (0.0 to 2.0, where 1.0 = unity/0dB).</param>
  /// <returns>
  /// True if a live player or component received the new gain. See <see cref="ApplyEffectiveVolume"/>
  /// for why a false return is not by itself a defect.
  /// </returns>
  public bool SetGainOffset(string sourceId, float gainOffset)
  {
    ThrowIfDisposed();

    lock (_playersLock)
    {
      gainOffset = Math.Clamp(gainOffset, AudioPreferencePersistence.MinGain, AudioPreferencePersistence.MaxGain);
      _gainOffsets[sourceId] = gainOffset;
      var applied = ApplyEffectiveVolume(sourceId);

      // ⚠ TWO OUTCOMES, TWO MESSAGES. The single line this replaces — "Applied gain offset {Gain:F2}
      // to source (SourceId={SourceId})" — was printed on BOTH paths, including the one where the
      // dictionary lookup matched nothing and no volume moved. It asserted a success it never
      // checked, which is the class CLAUDE.md § Pre-Merge Review names, and it is what made AUD-2
      // invisible for months: the only instrument anyone had reported success either way.
      //
      // Both arms stay at Debug, and that is deliberate rather than an oversight: neither is an
      // error at THIS layer (a stopped source legitimately carries a stored offset), and Radio.API's
      // Serilog "Radio" override floors at Information, so neither reaches the box at all. The
      // operator-visible signal is AudioManager's warning, which is the only layer that can tell a
      // stored offset from a broken key.
      if (applied)
      {
        _logger.LogDebug(
          "Applied gain offset {Gain:F2} to live playback (SourceId={SourceId})",
          gainOffset, sourceId);
      }
      else
      {
        _logger.LogDebug(
          "Stored gain offset {Gain:F2} for SourceId={SourceId}; nothing is registered under that " +
          "key, so no volume changed. It applies when the source next starts.",
          gainOffset, sourceId);
      }

      return applied;
    }
  }

  /// <summary>
  /// Sets a ducking multiplier for a specific source.
  /// Used by the ducking system to temporarily reduce volume of primary sources
  /// while event audio (TTS, notifications) plays.
  /// Effective volume = base volume * gain offset * ducking multiplier.
  /// </summary>
  /// <param name="sourceId">The source identifier.</param>
  /// <param name="multiplier">Ducking multiplier (0.0 to 1.0, where 1.0 = no ducking).</param>
  /// <returns>
  /// True if a live player or component had its volume attenuated. <b>False means the duck reached
  /// nothing</b> — the caller must not report that event audio is ducking this source. See
  /// <see cref="ApplyEffectiveVolume"/>.
  /// </returns>
  public bool SetDuckingMultiplier(string sourceId, float multiplier)
  {
    ThrowIfDisposed();

    lock (_playersLock)
    {
      multiplier = Math.Clamp(multiplier, 0f, 1f);
      _duckingMultipliers[sourceId] = multiplier;
      return ApplyEffectiveVolume(sourceId);
    }
  }

  /// <summary>
  /// Clears the ducking multiplier for a specific source, restoring full volume.
  /// </summary>
  /// <param name="sourceId">The source identifier.</param>
  /// <returns>
  /// True if a live player or component had its volume restored. False means nothing was registered
  /// under this id — which on the ducking-ended path means no volume was restored, and the caller
  /// must not claim otherwise. See <c>AudioManager.OnDuckingStateChanged</c>.
  /// </returns>
  public bool ClearDuckingMultiplier(string sourceId)
  {
    ThrowIfDisposed();

    lock (_playersLock)
    {
      _duckingMultipliers.Remove(sourceId);
      return ApplyEffectiveVolume(sourceId);
    }
  }

  /// <summary>
  /// Recalculates and applies the effective volume for a source.
  /// Must be called under _playersLock.
  /// </summary>
  /// <param name="sourceId">The playback key to recalculate.</param>
  /// <returns>
  /// True if a live player or component was registered under <paramref name="sourceId"/> and its
  /// volume was set; false if the id matched nothing.
  /// <para>
  /// ⚠ A false return is NOT by itself a defect, and no caller may treat it as one. A source can
  /// legitimately carry a stored gain offset while stopped — AudioManager applies the stored offset
  /// on every source switch (AudioManager.cs:288-295) and FilePlayer does not auto-play
  /// (AudioManager.cs:261) — and the offset is picked up at registration time by the
  /// <c>_gainOffsets.GetValueOrDefault</c> reads in PlayFileAsync (:148), PlayStreamAsync (:277),
  /// PlayDataProviderAsync (:343) and PlayComponentAsync (:407). This class cannot distinguish that
  /// from a key mismatch, because it knows its dictionaries and not whether the source was supposed
  /// to be live. AudioManager can, and does. AUD-2.
  /// </para>
  /// </returns>
  private bool ApplyEffectiveVolume(string sourceId)
  {
    var baseVol = _baseVolumes.GetValueOrDefault(sourceId, 1.0f);
    var gainOffset = _gainOffsets.GetValueOrDefault(sourceId, 1.0f);
    var duckMult = _duckingMultipliers.GetValueOrDefault(sourceId, 1.0f);
    var effective = Math.Clamp(baseVol * gainOffset * duckMult,
      AudioPreferencePersistence.MinGain, AudioPreferencePersistence.MaxGain);

    var applied = false;

    if (_activePlayers.TryGetValue(sourceId, out var player))
    {
      player.Volume = effective;
      applied = true;
    }
    if (_activeComponents.TryGetValue(sourceId, out var component))
    {
      component.Volume = effective;
      applied = true;
    }

    return applied;
  }

  /// <summary>
  /// Registers <paramref name="component"/> under <paramref name="sourceId"/> and applies the
  /// effective volume, exactly as <see cref="PlayComponentAsync"/> would, but without a playback
  /// device.
  /// </summary>
  /// <remarks>
  /// <b>Test seam (kind B — injection).</b> Called only from
  /// <c>Radio.Infrastructure.Tests</c> via <c>InternalsVisibleTo</c>; production code never calls it
  /// and no production behaviour branches on it.
  ///
  /// <b>Why the state is otherwise unreachable:</b> the only production path into
  /// <c>_activeComponents</c> is <see cref="PlayComponentAsync"/>, which returns early unless
  /// <c>SoundFlowAudioEngine.GetPlaybackDevice()</c> is non-null — and that is a MiniAudio
  /// <c>AudioPlaybackDevice</c>, created only by <c>InitializeAsync</c> against real hardware. The
  /// method is <c>internal</c> and non-virtual on a concrete class, so it cannot be substituted
  /// either. Without this seam the populated-dictionary half of <see cref="ApplyEffectiveVolume"/> is
  /// unreachable in a unit test, and AUD-2's tests could assert only that a call did not throw —
  /// which is exactly the shape of assertion that let AUD-2 survive for months.
  ///
  /// <b>What it does not cover:</b> everything <see cref="PlayComponentAsync"/> does either side of
  /// the dictionary write — <c>StopAsync</c> of any prior registration, the
  /// <c>MasterMixer.AddComponent</c> that actually makes the component audible, and the two
  /// AUDIO ROUTING log lines. A test using this seam proves that a key which matches moves a
  /// component's Volume; it proves nothing about whether that component is connected to a speaker.
  /// That remains UAT.
  /// </remarks>
  /// <param name="sourceId">The playback key to register under.</param>
  /// <param name="component">The component to register.</param>
  /// <param name="baseVolume">The base volume, as <c>PlayComponentAsync</c>'s <c>volume</c>.</param>
  internal void RegisterComponentForTests(
    string sourceId, SoundComponent component, float baseVolume = 1.0f)
  {
    lock (_playersLock)
    {
      _baseVolumes[sourceId] = baseVolume;
      _activeComponents[sourceId] = component;
      ApplyEffectiveVolume(sourceId);
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
  /// <returns>The current position, or null when no player is registered under this id.</returns>
  public TimeSpan? GetPosition(string sourceId)
  {
    lock (_playersLock)
    {
      if (_activePlayers.TryGetValue(sourceId, out var player))
      {
        // SoundPlayerBase.Time is a float, in SECONDS.
        return TimeSpan.FromSeconds(player.Time);
      }
    }
    return null;
  }

  /// <summary>
  /// Gets diagnostic info about active players and components (for debug markers).
  /// </summary>
  /// <remarks>
  /// ⚠ <c>ComponentIds</c> was added by AUD-2, and its absence was a real gap rather than an
  /// omission of convenience. This method returned <c>_activePlayers.Keys</c> ONLY, so every source
  /// that registers via <see cref="PlayComponentAsync"/> — SDR radio, the three USB sources and
  /// Bluetooth, i.e. everything AUD-2 was about except FilePlayer — was invisible here. AUD-2's own
  /// queue row proposed this method as the way to compare live playback keys against the
  /// <c>IAudioSource.Id</c> AudioManager holds; that check would have come back EMPTY and proved
  /// nothing. Both key spaces are now reported, so the comparison the row wanted actually works:
  /// <c>curl -s http://radio:5000/api/audio/diagnostics</c> and read <c>playback.componentIds</c> —
  /// post-AUD-2 they read <c>Radio-&lt;32 hex&gt;</c>, not <c>sdr-radio-&lt;32 hex&gt;</c>.
  /// </remarks>
  public (int ActivePlayers, int ActiveComponents, string[] PlayerIds, string[] ComponentIds)
    GetDiagnostics()
  {
    lock (_playersLock)
    {
      return (
        _activePlayers.Count,
        _activeComponents.Count,
        _activePlayers.Keys.ToArray(),
        _activeComponents.Keys.ToArray());
    }
  }

  /// <summary>
  /// Stops all active playback.
  /// </summary>
  public void StopAll()
  {
    // Don't throw if disposed - just return
    if (_disposed)
    {
        return;
    }

    List<string> sourceIds;
    lock (_playersLock)
    {
      sourceIds = _activePlayers.Keys.Concat(_activeComponents.Keys).Distinct().ToList();
    }

    foreach (var sourceId in sourceIds)
    {
      StopAsync(sourceId).SafeFireAndForget(_logger, $"StopAsync({sourceId})");
    }
  }

  private async Task MonitorAudioFlowAsync(CancellationToken cancellationToken)
  {
    _logger.LogInformation("Audio flow health monitor started (interval: 10s)");

    while (!cancellationToken.IsCancellationRequested)
    {
      try
      {
        await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
      }
      catch (OperationCanceledException)
      {
        break;
      }

      try
      {
        KeyValuePair<string, SoundComponent>[] snapshot;
        lock (_playersLock)
        {
          snapshot = _activeComponents.ToArray();
        }

        foreach (var (sourceId, component) in snapshot)
        {
          if (component is not BufferedSoundGenerator<float> generator)
            continue;

          if (generator.IsDisposed)
          {
            _logger.LogWarning(
              "Audio flow monitor: disposed generator #{GeneratorId} still in active components for {SourceId}",
              generator.GeneratorId, sourceId);
            continue;
          }

          var currentOutput = generator.TotalSamplesOutput;
          var currentReceived = generator.TotalSamplesReceived;

          _lastOutputSamples.TryGetValue(sourceId, out var previousOutput);
          _lastOutputSamples[sourceId] = currentOutput;

          // Skip first check (no previous baseline)
          if (previousOutput == 0)
            continue;

          var outputDelta = currentOutput - previousOutput;
          var isReceiving = currentReceived > 0;

          // Stalled: generator is in mixer, has received samples, but output hasn't increased
          if (outputDelta == 0 && isReceiving && currentReceived > currentOutput)
          {
            _logger.LogError(
              "🔴 STALLED GENERATOR: #{GeneratorId} for {SourceId} — received={Received}, output={Output}, delta=0 in last 10s. " +
              "Generator is in mixer but SoundFlow is not reading from it.",
              generator.GeneratorId, sourceId, currentReceived, currentOutput);

            GeneratorStalled?.Invoke(sourceId);
          }
        }

        // Clean up tracking for removed components
        var activeIds = snapshot.Select(s => s.Key).ToHashSet();
        foreach (var key in _lastOutputSamples.Keys.Except(activeIds).ToList())
        {
          _lastOutputSamples.Remove(key);
        }
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Error in audio flow health monitor");
      }
    }

    _logger.LogInformation("Audio flow health monitor stopped");
  }

  private void ThrowIfDisposed()
  {
    ObjectDisposedException.ThrowIf(_disposed, this);
  }

  /// <inheritdoc/>
  public void Dispose()
  {
    if (_disposed)
    {
        return;
    }

    _audioEngine.PlaybackDeviceSwitched -= OnPlaybackDeviceSwitched;

    _flowMonitorCts?.Cancel();
    _flowMonitorCts?.Dispose();

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
