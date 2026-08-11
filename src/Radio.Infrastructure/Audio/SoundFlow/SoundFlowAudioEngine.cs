using System.Runtime;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Core.Interfaces;
using Radio.Core.Interfaces.Audio;
using Radio.Metrics;
using SoundFlow.Abstracts;
using SoundFlow.Abstracts.Devices;
using SoundFlow.Backends.MiniAudio;
using SoundFlow.Enums;
using SoundFlow.Structs;

namespace Radio.Infrastructure.Audio.SoundFlow;

// Import our local AudioEngineState to avoid collision with SoundFlow's
using AudioEngineState = Radio.Core.Interfaces.Audio.AudioEngineState;

/// <summary>
/// SoundFlow audio engine implementation.
/// Manages the audio graph, device connection, and real-time audio processing.
/// </summary>
public class SoundFlowAudioEngine : IAudioEngine
{
  private readonly ILogger<SoundFlowAudioEngine> _logger;
  private readonly AudioEngineOptions _options;
  private readonly SoundFlowMasterMixer _masterMixer;
  private readonly SoundFlowDeviceManager _deviceManager;
  private readonly IMetricsCollector? _metricsCollector;

  private readonly IVisualizerService? _visualizerService;

  private MiniAudioEngine? _engine;
  private AudioPlaybackDevice? _playbackDevice;
  private AudioFormat _audioFormat;
  private TappedOutputStream? _outputTap;
  private FingerprintTapModifier? _fingerprintTap;
  private VisualizationTapModifier? _visualizationTap;
  private BalanceModifier? _balanceModifier;
  private LimiterModifier? _limiterModifier;
  private Timer? _hotPlugTimer;
  private Timer? _limiterStatsTimer;
  private AudioEngineState _state = AudioEngineState.Uninitialized;
  private int _currentDeviceIndex = -1;
  private bool _disposed;
  private bool _localOutputMuted;
  private readonly object _stateLock = new();
  private GCLatencyMode _previousLatencyMode;

  // Optional virtual-output references for SetActiveOutputAsync. Injected via
  // AttachOutputCoordination (not the constructor) to avoid a chicken-and-egg
  // dependency cycle: GoogleCastOutput can hold the engine indirectly when
  // DirectChannel mode is enabled.
  private IAudioOutput? _castOutput;
  private IAudioOutput? _httpOutput;
  private Radio.Configuration.Abstractions.IConfigurationManager? _configManager;
  private string? _activeOutputId;
  private readonly SemaphoreSlim _activeOutputLock = new(1, 1);

  // Incremented under _activeOutputLock on every output transition. A Cast
  // connect runs OUTSIDE that lock (it is far too long to hold it — see
  // TryCommitCastConnectAsync), so it captures this value when it starts and
  // hands it back on completion to prove nothing reselected the output in the
  // meantime.
  private int _castConnectEpoch;

  /// <inheritdoc/>
  public event EventHandler<AudioEngineStateChangedEventArgs>? StateChanged;

  /// <inheritdoc/>
  public event EventHandler<AudioDeviceChangedEventArgs>? DeviceChanged;

  /// <summary>
  /// Raised after a playback device switch completes, so that services holding
  /// active SoundComponents can re-attach them to the new device's mixer.
  /// The sender is this engine; the argument is the new <see cref="AudioPlaybackDevice"/>.
  /// </summary>
  internal event EventHandler<AudioPlaybackDevice>? PlaybackDeviceSwitched;

  /// <summary>
  /// Initializes a new instance of the <see cref="SoundFlowAudioEngine"/> class.
  /// </summary>
  /// <param name="logger">The logger instance.</param>
  /// <param name="options">The audio engine options.</param>
  /// <param name="masterMixer">The master mixer instance.</param>
  /// <param name="deviceManager">The device manager instance.</param>
  /// <param name="metricsCollector">Optional metrics collector for pipeline metrics.</param>
  /// <param name="visualizerService">Optional visualizer service for real-time audio visualization.</param>
  public SoundFlowAudioEngine(
    ILogger<SoundFlowAudioEngine> logger,
    IOptions<AudioEngineOptions> options,
    SoundFlowMasterMixer masterMixer,
    SoundFlowDeviceManager deviceManager,
    IMetricsCollector? metricsCollector = null,
    IVisualizerService? visualizerService = null)
  {
    _logger = logger;
    _options = options.Value;
    _masterMixer = masterMixer;
    _deviceManager = deviceManager;
    _metricsCollector = metricsCollector;
    _visualizerService = visualizerService;

    // Subscribe to device manager events
    _deviceManager.DevicesChanged += OnDeviceManagerDevicesChanged;

    // Subscribe to master mixer events to sync with playback device
    _masterMixer.MasterVolumeChanged += OnMasterVolumeChanged;
    _masterMixer.MuteStateChanged += OnMuteStateChanged;
    // Balance requires custom handling, currently not synced to engine directly
  }

  /// <inheritdoc/>
  public AudioEngineState State
  {
    get
    {
      lock (_stateLock)
      {
        return _state;
      }
    }
    private set
    {
      AudioEngineState previousState;
      lock (_stateLock)
      {
        previousState = _state;
        _state = value;
      }

      if (previousState != value)
      {
        _logger.LogInformation(
          "Audio engine state changed from {PreviousState} to {NewState}",
          previousState, value);

        StateChanged?.Invoke(this, new AudioEngineStateChangedEventArgs
        {
          PreviousState = previousState,
          NewState = value
        });
      }
    }
  }

  /// <inheritdoc/>
  public bool IsReady => State == AudioEngineState.Ready || State == AudioEngineState.Running;

  /// <inheritdoc/>
  public bool IsLocalOutputMuted => _localOutputMuted;

  /// <inheritdoc/>
  public void SetLocalOutputMuted(bool muted)
  {
    _localOutputMuted = muted;
    UpdatePlaybackDeviceVolume();
    _logger.LogInformation("Local output {State}", muted ? "muted (casting to external device)" : "unmuted");
  }

  /// <inheritdoc/>
  public string? ActiveOutputId => _activeOutputId;

  /// <summary>
  /// Wires the virtual outputs + configuration manager so
  /// <see cref="SetActiveOutputAsync"/> can activate/deactivate them and persist
  /// the choice. Called from DI startup after all singletons are constructed
  /// (avoids a constructor-time cycle with Cast/HTTP outputs).
  /// </summary>
  /// <param name="castOutput">Optional Google Cast output instance.</param>
  /// <param name="httpOutput">Optional HTTP stream output instance.</param>
  /// <param name="configManager">Optional configuration manager for persisting the choice.</param>
  public void AttachOutputCoordination(
    IAudioOutput? castOutput,
    IAudioOutput? httpOutput,
    Radio.Configuration.Abstractions.IConfigurationManager? configManager)
  {
    _castOutput = castOutput;
    _httpOutput = httpOutput;
    _configManager = configManager;
    _logger.LogDebug(
      "Output coordination attached (cast={HasCast}, http={HasHttp}, config={HasConfig})",
      castOutput != null, httpOutput != null, configManager != null);
  }

  /// <inheritdoc/>
  public async Task SetActiveOutputAsync(string outputId, CancellationToken cancellationToken = default)
  {
    if (string.IsNullOrWhiteSpace(outputId))
    {
      throw new ArgumentException("outputId is required", nameof(outputId));
    }

    await _activeOutputLock.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      var previous = _activeOutputId;
      _logger.LogInformation(
        "SetActiveOutputAsync: {Previous} -> {Next}", previous ?? "<none>", outputId);

      // Any output transition supersedes a Cast connect that is still in flight.
      _castConnectEpoch++;

      var isCast = string.Equals(outputId, "google-cast", StringComparison.OrdinalIgnoreCase);
      var isHttp = string.Equals(outputId, "http-stream", StringComparison.OrdinalIgnoreCase);
      var isLocal = !isCast && !isHttp;

      // Detect a transition AWAY from Cast — Cast needs a full tear-down
      // (media STOP + CLOSE_APP + disconnect receiver) so the Chromecast
      // returns to its default state. The bare DeactivateVirtualOutputAsync
      // only sends media STOP via output.StopAsync; without DisconnectAsync
      // the receiver app keeps the session and audio keeps playing on Cast
      // (the user has to manually disconnect via the Cast UI, which was the
      // bug observed in UAT scenario D).
      var leavingCast = !isCast && string.Equals(previous, "google-cast", StringComparison.OrdinalIgnoreCase);

      // Order: deactivate -> mute-state -> activate. Muting before activation
      // avoids a brief dual-output blip on the playback device's next callback.
      if (isLocal)
      {
        // Going to local: stop Cast + HTTP, then unmute local.
        // The local-device switch (native MiniAudio swap) is the caller's
        // responsibility via IAudioDeviceManager.SetOutputDeviceAsync — this
        // gate only owns mute state, virtual-output lifecycle, and persistence.
        if (leavingCast)
        {
          await TearDownCastOutputAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
          await DeactivateVirtualOutputAsync(_castOutput, "Google Cast", cancellationToken).ConfigureAwait(false);
        }
        await DeactivateVirtualOutputAsync(_httpOutput, "HTTP Stream", cancellationToken).ConfigureAwait(false);
        SetLocalOutputMuted(false);
      }
      else if (isCast)
      {
        // Cast needs HTTP active too (HttpMp3 mode wires audio through it).
        // DirectChannel mode tolerates HTTP being active — HttpStreamOutput is
        // a no-op if there are no readers. No current caller relies on HTTP
        // being explicitly stopped while Cast is active.
        SetLocalOutputMuted(true);
        await ActivateVirtualOutputAsync(_httpOutput, "HTTP Stream", cancellationToken).ConfigureAwait(false);
        await ActivateVirtualOutputAsync(_castOutput, "Google Cast", cancellationToken).ConfigureAwait(false);
      }
      else // isHttp
      {
        // HTTP without Cast: HTTP active, Cast deactivated, local muted.
        SetLocalOutputMuted(true);
        if (leavingCast)
        {
          await TearDownCastOutputAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
          await DeactivateVirtualOutputAsync(_castOutput, "Google Cast", cancellationToken).ConfigureAwait(false);
        }
        await ActivateVirtualOutputAsync(_httpOutput, "HTTP Stream", cancellationToken).ConfigureAwait(false);
      }

      _activeOutputId = outputId;
      await PersistActiveOutputAsync(outputId, cancellationToken).ConfigureAwait(false);
    }
    finally
    {
      _activeOutputLock.Release();
    }
  }

  private static async Task ActivateVirtualOutputAsync(
    IAudioOutput? output, string name, CancellationToken ct)
  {
    if (output == null)
    {
      return;
    }
    if (output.State == AudioOutputState.Error || output.State == AudioOutputState.Created)
    {
      await output.InitializeAsync(ct).ConfigureAwait(false);
    }
    if (output.State == AudioOutputState.Ready || output.State == AudioOutputState.Stopped)
    {
      await output.StartAsync(ct).ConfigureAwait(false);
    }
  }

  private static async Task DeactivateVirtualOutputAsync(
    IAudioOutput? output, string name, CancellationToken ct)
  {
    if (output == null)
    {
      return;
    }
    if (output.State == AudioOutputState.Streaming || output.State == AudioOutputState.Ready)
    {
      await output.StopAsync(ct).ConfigureAwait(false);
    }
  }

  /// <summary>
  /// Full graceful tear-down of the Cast output: sends media STOP via
  /// <c>StopAsync</c> AND <c>CLOSE_APP</c> + receiver-channel disconnect via
  /// <c>DisconnectAsync</c>. Required when transitioning away from Cast
  /// (output picker switching to soundbar / http-stream) or on engine
  /// shutdown — without the DisconnectAsync step the Chromecast receiver
  /// app keeps the session and audio keeps streaming.
  ///
  /// Best-effort: capped at 5 s, swallows exceptions, never blocks the gate.
  /// Shares the same shutdown sequence used by AudioEngineInitializationService.StopAsync.
  /// </summary>
  public async Task TearDownCastOutputAsync(CancellationToken cancellationToken)
  {
    if (_castOutput == null)
    {
      return;
    }

    try
    {
      using var castCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
      castCts.CancelAfter(TimeSpan.FromSeconds(5));

      // StopAsync sends MediaChannel.StopAsync (terminates media session) and
      // tears down DirectChannel streaming if active. Safe to call regardless
      // of current state — the override has its own ValidateCanStop guard.
      if (_castOutput.State == AudioOutputState.Streaming ||
          _castOutput.State == AudioOutputState.Ready ||
          _castOutput.State == AudioOutputState.Connecting)
      {
        await _castOutput.StopAsync(castCts.Token).ConfigureAwait(false);
      }

      // DisconnectAsync sends CLOSE_APP and closes the receiver-channel
      // connection. Only GoogleCastOutput knows how to do this — the
      // IAudioOutput interface doesn't expose it. Runtime cast keeps the
      // engine's _castOutput field typed as IAudioOutput? for testability.
      if (_castOutput is Radio.Infrastructure.Audio.Outputs.GoogleCastOutput cast)
      {
        await cast.DisconnectAsync(castCts.Token).ConfigureAwait(false);
      }

      _logger.LogInformation("Cast output stopped + disconnected gracefully");
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Graceful Cast tear-down failed; continuing");
    }
  }

  /// <summary>
  /// Captures the current output-selection epoch before starting a Cast connect.
  /// Pass the result to <see cref="TryCommitCastConnectAsync"/> when the connect
  /// finishes.
  ///
  /// The connect itself deliberately runs OUTSIDE <c>_activeOutputLock</c>: it can
  /// take tens of seconds inside SharpCaster calls that do not observe
  /// cancellation, and holding the gate for that long would block the output
  /// picker — turning a data race into a hang. The epoch is what makes the
  /// unlocked connect safe: it cannot silently win a race it already lost.
  /// </summary>
  public async Task<int> BeginCastConnectAsync(CancellationToken cancellationToken = default)
  {
    await _activeOutputLock.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      return _castConnectEpoch;
    }
    finally
    {
      _activeOutputLock.Release();
    }
  }

  /// <summary>
  /// Commits a Cast connect that began at <paramref name="epoch"/>.
  ///
  /// Re-checks — atomically, under the same lock that owns output selection —
  /// that Cast is still the intended output. If anything reselected the output
  /// while the connect was on the network, the connection is torn back down and
  /// this returns false. Doing the check and the teardown under one lock
  /// acquisition is the point: a check-then-act without it leaves a window in
  /// which a newly-connected Cast starts streaming while the local sink has
  /// already been unmuted, which is the dual-output bug the startup mute exists
  /// to prevent.
  /// </summary>
  /// <returns>True if the connection is live and Cast is still the active output.</returns>
  public async Task<bool> TryCommitCastConnectAsync(int epoch, CancellationToken cancellationToken = default)
  {
    await _activeOutputLock.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      var stillCurrent = _castConnectEpoch == epoch &&
        string.Equals(_activeOutputId, "google-cast", StringComparison.OrdinalIgnoreCase);

      if (stillCurrent)
      {
        return true;
      }

      _logger.LogWarning(
        "Cast connect superseded (epoch {Started} -> {Now}, active output now {Active}) — tearing it back down",
        epoch, _castConnectEpoch, _activeOutputId ?? "<none>");

      // TearDownCastOutputAsync does not take the lock (SetActiveOutputAsync
      // already calls it while holding it), so this is safe and non-reentrant.
      await TearDownCastOutputAsync(cancellationToken).ConfigureAwait(false);
      return false;
    }
    finally
    {
      _activeOutputLock.Release();
    }
  }

  private async Task PersistActiveOutputAsync(string outputId, CancellationToken ct)
  {
    if (_configManager == null)
    {
      return;
    }
    try
    {
      var storeId = _configManager.CurrentStoreType ==
        Radio.Configuration.Models.ConfigurationStoreType.Sqlite ? "sqlite" : "config";
      await _configManager.SetValueAsync(storeId, "AudioPreferences:CurrentOutput", outputId, ct).ConfigureAwait(false);
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Failed to persist AudioPreferences:CurrentOutput = {Id}", outputId);
    }
  }

  /// <inheritdoc/>
  public async Task InitializeAsync(CancellationToken cancellationToken = default)
  {
    ThrowIfDisposed();

    if (State != AudioEngineState.Uninitialized)
    {
      throw new InvalidOperationException(
        $"Cannot initialize engine in state {State}. Engine must be uninitialized.");
    }

    State = AudioEngineState.Initializing;

    try
    {
      // Reduce GC pause duration during audio processing. SustainedLowLatency
      // tells .NET to avoid full blocking Gen2 collections, which are the primary
      // cause of audio callback stalls (40-740ms pauses observed).
      _previousLatencyMode = GCSettings.LatencyMode;
      GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;
      _logger.LogInformation(
        "GC latency mode set to SustainedLowLatency (was {Previous})", _previousLatencyMode);

      _logger.LogInformation(
        "Initializing SoundFlow audio engine (SampleRate: {SampleRate}, Channels: {Channels}, BufferSize: {BufferSize})",
        _options.SampleRate, _options.Channels, _options.BufferSize);

      // Initialize SoundFlow MiniAudioEngine.
      //
      // SerializedMiniAudioEngine, not a raw MiniAudioEngine: its UpdateAudioDevicesInfo
      // override routes native device enumeration through NativeAudioDeviceGate. That is
      // what serializes this class's own enumeration call sites — the one below and the one
      // in TryRecoverPlaybackDevice — against the device manager's and the 30-second
      // hot-plug timer's, without either call site having to take a lock itself. Two threads
      // inside MiniAudio's PulseAudio main loop abort the process; see NativeAudioDeviceGate.
      _engine = SerializedMiniAudioEngine.Create();

      // Share the engine with device manager so hot-plug detection reuses it
      // instead of creating/disposing temporary engines (which leak native memory
      // and cause SIGSEGV after ~300 cycles)
      if (_deviceManager is SoundFlowDeviceManager sfDeviceManager)
      {
        sfDeviceManager.SetSharedEngine(_engine);
      }

      // Create audio format for playback with explicit sample format
      _audioFormat = new AudioFormat
      {
        SampleRate = _options.SampleRate,
        Channels = _options.Channels,
        Format = SampleFormat.F32  // Use 32-bit float samples
      };

      // Update device info and get available playback devices
      _engine.UpdateAudioDevicesInfo();
      var playbackDevices = _engine.PlaybackDevices;
      _logger.LogInformation("Found {DeviceCount} playback devices after UpdateAudioDevicesInfo", playbackDevices.Length);

      if (playbackDevices.Length > 0)
      {
        // Log all available devices for diagnostics
        for (int i = 0; i < playbackDevices.Length; i++)
        {
          _logger.LogInformation("  Playback device [{Index}]: {Name} (IsDefault={IsDefault})",
            i, playbackDevices[i].Name ?? "(null)", playbackDevices[i].IsDefault);
        }

        // Select the best playback device with this priority:
        // 1. System default device (IsDefault=true)
        // 2. First real device (skip null/discard backends)
        // 3. First device as last resort
        var deviceIndex = 0;
        var deviceInfo = playbackDevices[0];

        // Prefer the system default device
        for (int i = 0; i < playbackDevices.Length; i++)
        {
          if (playbackDevices[i].IsDefault)
          {
            deviceIndex = i;
            deviceInfo = playbackDevices[i];
            _logger.LogInformation("Selected system default playback device at index {Index}: {Name}", i, deviceInfo.Name);
            break;
          }
        }

        // If no default found, fall back to first non-discard device
        if (!deviceInfo.IsDefault)
        {
          for (int i = 0; i < playbackDevices.Length; i++)
          {
            var name = playbackDevices[i].Name ?? "";
            if (name.Contains("Discard all samples", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("generate zero samples", StringComparison.OrdinalIgnoreCase))
            {
              _logger.LogDebug("Skipping null device at index {Index}: {Name}", i, name);
              continue;
            }
            deviceIndex = i;
            deviceInfo = playbackDevices[i];
            break;
          }
        }
        _logger.LogInformation("Initializing playback device: {DeviceName} (index {Index} of {Total})",
          deviceInfo.Name, deviceIndex, playbackDevices.Length);

        try
        {
          // Initialize the playback device with our format
          _playbackDevice = _engine.InitializePlaybackDevice(deviceInfo, _audioFormat);
          _currentDeviceIndex = deviceIndex;

          // Apply initial volume/mute state
          _playbackDevice.MasterMixer.Volume = _localOutputMuted ? 0f : _masterMixer.GetEffectiveVolume();

          // NOTE: Do NOT call _playbackDevice.Start() here.
          // Modifiers must be attached BEFORE starting the device, otherwise
          // SoundFlow's audio callback may not process them correctly.
          // Start() is called below after modifiers are added. This matches
          // the order used in SwitchPlaybackDevice().
          _logger.LogInformation("Playback device initialized: {DeviceName} (will start after modifiers attached)", deviceInfo.Name);
        }
        catch (Exception ex)
        {
          _logger.LogError(ex, "Failed to initialize playback device: {DeviceName}", deviceInfo.Name);
        }
      }
      else
      {
        _logger.LogError("No playback devices found! Audio playback will fail until a device becomes available. " +
          "Check that an audio output device is connected and the OS audio service is running.");
      }

      // Create output tap for streaming
      _outputTap = new TappedOutputStream(
        _options.SampleRate,
        _options.Channels,
        _options.OutputBufferSizeSeconds,
        _metricsCollector);

      // Add modifiers to capture mixed audio for fingerprinting/streaming.
      // Start the playback device AFTER all modifiers are attached.
      // SoundFlow's audio callback must see the full modifier chain from
      // the first callback invocation, otherwise modifiers receive silence.
      if (_playbackDevice != null)
      {
        AttachModifiersToPlaybackDevice();

        // Starting a device drives the same PulseAudio main loop that enumeration does
        // (`ma_device_start__pulse` corks/uncorks the stream and waits by iterating it), so
        // it takes NativeAudioDeviceGate like every other native device call. One call per
        // gated region on purpose — the abort needs two threads inside the main loop at the
        // same time, not a particular interleaving, so serializing each native call is
        // sufficient and keeps the hold time to that call alone.
        NativeAudioDeviceGate.Run(_playbackDevice.Start);
        _logger.LogInformation("Playback device started with all modifiers attached");
      }

      // Refresh device list
      await _deviceManager.RefreshDevicesAsync(cancellationToken);

      // Set up hot-plug detection timer if enabled
      if (_options.EnableHotPlugDetection)
      {
        var interval = TimeSpan.FromSeconds(_options.HotPlugIntervalSeconds);
        _hotPlugTimer = new Timer(
          CheckForDeviceChanges,
          null,
          interval,
          interval);

        _logger.LogDebug(
          "Hot-plug detection enabled with {Interval}s interval",
          _options.HotPlugIntervalSeconds);
      }

      // Periodically log limiter engagement stats (every 30s)
      _limiterStatsTimer = new Timer(_ =>
      {
        if (_limiterModifier == null)
        {
            return;
        }
        var stats = _limiterModifier.GetAndResetStats();
        if (stats == null)
        {
            return;
        }
        var s = stats.Value;
        if (s.LimitedSamples > 0)
        {
          _logger.LogInformation(
            "🔊 Limiter engaged: {Percent:F1}% of samples compressed ({Limited}/{Total}), " +
            "max input {MaxInput:F3} ({MaxInputDb:F1} dBFS), max reduction {MaxReduction:F1} dB",
            s.EngagementPercent, s.LimitedSamples, s.TotalSamples,
            s.MaxInputAbs, 20f * MathF.Log10(s.MaxInputAbs),
            s.MaxReductionDb);
        }
      }, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));

      State = AudioEngineState.Ready;
      _metricsCollector?.Gauge("audio.engine.buffer_size_samples", _options.BufferSize);
      _logger.LogInformation("SoundFlow audio engine initialized successfully");
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to initialize audio engine");
      State = AudioEngineState.Error;
      throw;
    }
  }

  /// <inheritdoc/>
  public Task StartAsync(CancellationToken cancellationToken = default)
  {
    ThrowIfDisposed();

    if (State != AudioEngineState.Ready)
    {
      throw new InvalidOperationException(
        $"Cannot start engine in state {State}. Engine must be in Ready state.");
    }

    try
    {
      _logger.LogInformation("Starting audio engine");

      // The MiniAudioEngine starts processing automatically when sources are added
      // Here we just transition the state

      State = AudioEngineState.Running;
      _logger.LogInformation("Audio engine started");
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to start audio engine");
      State = AudioEngineState.Error;
      throw;
    }

    return Task.CompletedTask;
  }

  /// <inheritdoc/>
  public Task StopAsync(CancellationToken cancellationToken = default)
  {
    ThrowIfDisposed();

    if (State != AudioEngineState.Running)
    {
      _logger.LogWarning("Stop requested but engine is not running (state: {State})", State);
      return Task.CompletedTask;
    }

    try
    {
      State = AudioEngineState.Stopping;
      _logger.LogInformation("Stopping audio engine");

      // Clear all sources from the mixer
      _masterMixer.ClearSources();

      // Clear the output tap
      _outputTap?.Clear();

      State = AudioEngineState.Ready;
      _logger.LogInformation("Audio engine stopped");
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to stop audio engine");
      State = AudioEngineState.Error;
      throw;
    }

    return Task.CompletedTask;
  }

  /// <inheritdoc/>
  public IMasterMixer GetMasterMixer()
  {
    ThrowIfDisposed();
    return _masterMixer;
  }

  /// <inheritdoc/>
  public Stream GetMixedOutputStream()
  {
    ThrowIfDisposed();

    if (_outputTap == null)
    {
      throw new InvalidOperationException(
        "Audio engine not initialized. Call InitializeAsync first.");
    }

    return _outputTap;
  }

  /// <inheritdoc/>
  public Stream CreateStreamReader(string readerId, double? lagSeconds = null)
  {
    ThrowIfDisposed();

    if (_outputTap == null)
    {
      throw new InvalidOperationException(
        "Audio engine not initialized. Call InitializeAsync first.");
    }

    // Start the reader behind the write position so Cast/HTTP clients
    // get an immediate burst of audio data instead of waiting for new writes.
    // Without this lag, Cast devices timeout before the first FingerprintTapModifier
    // batch arrives (~42ms) and the LAME encoder produces its first MP3 frames.
    var lag = lagSeconds ?? _options.StreamReaderLagSeconds;
    var lagBytes = (int)(_options.SampleRate * _options.Channels * 2 * lag);
    return _outputTap.CreateReader(readerId, lagBytes);
  }

  /// <summary>
  /// Gets the audio device manager.
  /// </summary>
  /// <returns>The device manager instance.</returns>
  public IAudioDeviceManager GetDeviceManager() => _deviceManager;

  /// <summary>
  /// Gets the underlying SoundFlow MiniAudioEngine instance.
  /// </summary>
  /// <returns>The MiniAudioEngine, or null if not initialized.</returns>
  internal MiniAudioEngine? GetUnderlyingEngine() => _engine;

  /// <summary>
  /// Gets the initialized playback device. If the device is null but the engine
  /// is initialized, attempts to recover by re-enumerating and initializing a device.
  /// </summary>
  /// <returns>The playback device, or null if not initialized and recovery failed.</returns>
  internal AudioPlaybackDevice? GetPlaybackDevice()
  {
    if (_playbackDevice != null)
    {
        return _playbackDevice;
    }

    // Attempt lazy recovery if engine is running but device is missing
    if (_engine != null && (State == AudioEngineState.Ready || State == AudioEngineState.Running))
    {
      _logger.LogWarning("Playback device is null but engine is {State} — attempting recovery", State);
      TryRecoverPlaybackDevice();
    }

    return _playbackDevice;
  }

  /// <summary>
  /// Attempts to recover a missing playback device by re-enumerating
  /// audio devices and initializing the first available one.
  /// Called automatically by <see cref="GetPlaybackDevice"/> when the device is null.
  /// </summary>
  private void TryRecoverPlaybackDevice()
  {
    try
    {
      _engine!.UpdateAudioDevicesInfo();
      var playbackDevices = _engine.PlaybackDevices;
      _logger.LogInformation("Recovery: Found {Count} playback devices", playbackDevices.Length);

      if (playbackDevices.Length == 0)
      {
        _logger.LogError("Recovery failed: no playback devices available");
        return;
      }

      var deviceInfo = playbackDevices[0];
      _logger.LogInformation("Recovery: Initializing playback device: {Name}", deviceInfo.Name);

      _playbackDevice = _engine.InitializePlaybackDevice(deviceInfo, _audioFormat);
      _currentDeviceIndex = 0;

      _playbackDevice.MasterMixer.Volume = _localOutputMuted ? 0f : _masterMixer.GetEffectiveVolume();

      // Native main-loop call — gated. See the note in InitializeAsync.
      NativeAudioDeviceGate.Run(_playbackDevice.Start);

      // Re-attach all modifiers (including visualization tap, which was previously missing here)
      AttachModifiersToPlaybackDevice();

      _logger.LogInformation("Recovery successful: playback device {Name} initialized with modifiers", deviceInfo.Name);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Recovery failed: could not initialize playback device");
    }
  }

  /// <summary>
  /// Gets the audio format used by the engine.
  /// </summary>
  /// <returns>The audio format.</returns>
  internal AudioFormat GetAudioFormat() => _audioFormat;

  /// <summary>
  /// Switches to a different playback device by index.
  /// </summary>
  /// <param name="deviceIndex">The index of the device to switch to (from PlaybackDevices array).</param>
  /// <returns>True if the device was switched successfully.</returns>
  public bool SwitchPlaybackDevice(int deviceIndex)
  {
    if (_engine == null)
    {
      _logger.LogWarning("Cannot switch playback device: engine not initialized");
      return false;
    }

    var playbackDevices = _engine.PlaybackDevices;
    if (deviceIndex < 0 || deviceIndex >= playbackDevices.Length)
    {
      _logger.LogWarning("Invalid device index {Index}, available devices: {Count}",
        deviceIndex, playbackDevices.Length);
      return false;
    }

    var newDevice = playbackDevices[deviceIndex];

    // Skip if already on the requested device
    if (deviceIndex == _currentDeviceIndex && _playbackDevice != null)
    {
      _logger.LogDebug("Already on playback device index {Index} ({DeviceName}), skipping switch",
        deviceIndex, newDevice.Name);
      return true;
    }

    _logger.LogInformation("Switching playback device to: {DeviceName} (index {Index})",
      newDevice.Name, deviceIndex);

    try
    {
      // Stop and dispose current playback device. Both drive the PulseAudio main loop
      // (`ma_device_stop`/`ma_device_uninit` wait on pa_operations by iterating it), and this
      // whole method runs on a thread pool thread — DevicesController dispatches it
      // fire-and-forget — so it can land on top of the 30s hot-plug enumeration.
      if (_playbackDevice != null)
      {
        var retiringDevice = _playbackDevice;
        NativeAudioDeviceGate.Run(retiringDevice.Stop);
        NativeAudioDeviceGate.Run(retiringDevice.Dispose);
        _playbackDevice = null;
      }

      // Initialize new playback device
      _playbackDevice = _engine.InitializePlaybackDevice(newDevice, _audioFormat);
      _currentDeviceIndex = deviceIndex;

      // Apply current volume/mute state
      _playbackDevice.MasterMixer.Volume = _localOutputMuted ? 0f : _masterMixer.GetEffectiveVolume();

      // Re-attach all modifiers to the new device. Deliberately outside the gate: it takes
      // the mixer's own lock, and nesting a mixer lock inside the gate would introduce a
      // gate -> mixer lock ordering that nothing else in the codebase needs.
      AttachModifiersToPlaybackDevice();

      // Native main-loop call — gated. See the note in InitializeAsync.
      NativeAudioDeviceGate.Run(_playbackDevice.Start);

      _logger.LogInformation("Successfully switched to playback device: {DeviceName}", newDevice.Name);

      // Notify services (e.g. SoundFlowPlaybackService) to re-attach active
      // source components to the new device's mixer.
      try
      {
        PlaybackDeviceSwitched?.Invoke(this, _playbackDevice);
      }
      catch (Exception evtEx)
      {
        _logger.LogError(evtEx, "Error in PlaybackDeviceSwitched handler");
      }

      return true;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to switch to playback device: {DeviceName}", newDevice.Name);
      _currentDeviceIndex = -1;
      return false;
    }
  }

  /// <summary>
  /// Attaches all audio modifiers (balance, limiter, fingerprint tap, visualization tap)
  /// to the current playback device's mixer. Creates modifiers if they don't exist yet.
  /// </summary>
  private void AttachModifiersToPlaybackDevice()
  {
    if (_playbackDevice == null)
    {
        return;
    }

    // Balance modifier (first in chain)
    if (_balanceModifier != null)
    {
      _playbackDevice.MasterMixer.AddModifier(_balanceModifier);
    }
    else
    {
      _balanceModifier = new BalanceModifier(_masterMixer);
      _playbackDevice.MasterMixer.AddModifier(_balanceModifier);
    }

    // Limiter (after balance, before taps)
    if (_limiterModifier != null)
    {
      _playbackDevice.MasterMixer.AddModifier(_limiterModifier);
    }
    else
    {
      _limiterModifier = new LimiterModifier();
      _playbackDevice.MasterMixer.AddModifier(_limiterModifier);
    }

    // Fingerprint tap (after limiter)
    if (_fingerprintTap != null)
    {
      _playbackDevice.MasterMixer.AddModifier(_fingerprintTap);
    }
    else
    {
      _fingerprintTap = new FingerprintTapModifier(this, _logger, bufferSize: 2048, metricsCollector: _metricsCollector);
      _playbackDevice.MasterMixer.AddModifier(_fingerprintTap);
    }

    // Visualization tap (last in chain)
    if (_visualizationTap != null)
    {
      _playbackDevice.MasterMixer.AddModifier(_visualizationTap);
    }
    else if (_visualizerService != null)
    {
      _visualizationTap = new VisualizationTapModifier(_visualizerService, _audioFormat);
      _playbackDevice.MasterMixer.AddModifier(_visualizationTap);
    }

    _logger.LogDebug("All modifiers attached to playback device");
  }

  /// <summary>
  /// Adds a diagnostic modifier to the playback device's mixer.
  /// Used by <see cref="Radio.Infrastructure.Audio.Diagnostics.DiagnosticCaptureService"/>
  /// to attach capture taps during diagnostic sessions.
  /// </summary>
  internal void AddDiagnosticModifier(SoundModifier modifier)
  {
    if (_playbackDevice == null)
    {
        return;
    }
    _playbackDevice.MasterMixer.AddModifier(modifier);
    _logger.LogDebug("Diagnostic modifier {Name} attached to mixer", modifier.Name);
  }

  /// <summary>
  /// Removes a diagnostic modifier from the playback device's mixer.
  /// </summary>
  internal void RemoveDiagnosticModifier(SoundModifier modifier)
  {
    if (_playbackDevice == null)
    {
        return;
    }
    _playbackDevice.MasterMixer.RemoveModifier(modifier);
    _logger.LogDebug("Diagnostic modifier {Name} removed from mixer", modifier.Name);
  }

  /// <summary>
  /// Gets the active BufferedSoundGenerator from the given audio source, if it uses one.
  /// Returns null if the source doesn't use a BufferedSoundGenerator.
  /// </summary>
  internal static BufferedSoundGenerator<float>? GetGeneratorFromSource(IAudioSource? source)
  {
    if (source == null)
    {
        return null;
    }
    try
    {
      return source.GetSoundComponent() as BufferedSoundGenerator<float>;
    }
    catch
    {
      return null;
    }
  }

  /// <summary>
  /// Gets the index of a playback device by its ID.
  /// </summary>
  /// <param name="deviceId">The device ID (e.g., "playback-0").</param>
  /// <returns>The device index, or -1 if not found.</returns>
  public int GetDeviceIndexById(string deviceId)
  {
    if (string.IsNullOrEmpty(deviceId) || !deviceId.StartsWith("playback-"))
    {
      return -1;
    }

    if (int.TryParse(deviceId.AsSpan("playback-".Length), out var index))
    {
      return index;
    }

    return -1;
  }

  /// <summary>
  /// Writes audio samples to the output tap for streaming.
  /// This is called during audio processing to capture the mixed output.
  /// </summary>
  /// <param name="samples">The audio samples to write.</param>
  public void WriteToOutputTap(float[] samples)
  {
    _outputTap?.WriteFromEngine(samples);
  }

  /// <summary>
  /// Writes audio samples to the output tap for streaming.
  /// This is called during audio processing to capture the mixed output.
  /// </summary>
  /// <param name="samples">The audio samples span to write.</param>
  /// <param name="count">The number of samples to write.</param>
  public void WriteToOutputTap(Span<float> samples, int count)
  {
    _outputTap?.WriteFromEngine(samples, count);
  }

  /// <summary>
  /// Gets diagnostic information about the audio pipeline state.
  /// </summary>
  public PipelineDiagnostics GetPipelineDiagnostics()
  {
    return new PipelineDiagnostics
    {
      EngineState = State.ToString(),
      PlaybackDeviceActive = _playbackDevice != null,
      ModifierCount = (_balanceModifier != null ? 1 : 0)
        + (_limiterModifier != null ? 1 : 0)
        + (_fingerprintTap != null ? 1 : 0)
        + (_visualizationTap != null ? 1 : 0),
      OutputTapAvailableBytes = 0,
      FingerprintTapTotalSamples = _fingerprintTap?.TotalSamplesProcessed ?? 0,
      FingerprintTapLastProcessedTime = _fingerprintTap?.LastProcessedTime
    };
  }

  /// <summary>
  /// Gets diagnostic information from the output tap stream.
  /// </summary>
  public OutputTapDiagnostics? GetOutputTapDiagnostics()
  {
    return _outputTap?.GetDiagnostics();
  }

  private void CheckForDeviceChanges(object? state)
  {
    if (_disposed)
    {
        return;
    }

    try
    {
      // This runs on a timer thread, so we use Task.Run to avoid blocking
      Task.Run(async () =>
      {
        try
        {
          await _deviceManager.RefreshDevicesAsync();
        }
        catch (Exception ex)
        {
          _logger.LogWarning(ex, "Error during hot-plug device check");
        }
      });
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Error scheduling hot-plug device check");
    }
  }

  private void OnDeviceManagerDevicesChanged(object? sender, AudioDeviceChangedEventArgs e)
  {
    // Forward device change events
    DeviceChanged?.Invoke(this, e);
  }

  private void OnMasterVolumeChanged(object? sender, float volume)
  {
    UpdatePlaybackDeviceVolume();
  }

  private void OnMuteStateChanged(object? sender, bool isMuted)
  {
    UpdatePlaybackDeviceVolume();
  }

  private void UpdatePlaybackDeviceVolume()
  {
    if (_playbackDevice != null)
    {
      // When local output is muted (e.g. casting), set device volume to 0.
      // SoundFlow applies Volume AFTER modifiers, so audio taps (HTTP streaming,
      // visualization, fingerprinting) still receive full-volume audio.
      _playbackDevice.MasterMixer.Volume = _localOutputMuted ? 0f : _masterMixer.GetEffectiveVolume();
    }
  }

  private void ThrowIfDisposed()
  {
    ObjectDisposedException.ThrowIf(_disposed, this);
  }

  /// <inheritdoc/>
  public async ValueTask DisposeAsync()
  {
    if (_disposed)
    {
        return;
    }

    _disposed = true;
    State = AudioEngineState.Disposed;

    _logger.LogInformation("Disposing audio engine");

    // Stop timers
    if (_hotPlugTimer != null)
    {
      await _hotPlugTimer.DisposeAsync();
      _hotPlugTimer = null;
    }
    if (_limiterStatsTimer != null)
    {
      await _limiterStatsTimer.DisposeAsync();
      _limiterStatsTimer = null;
    }

    // Unsubscribe from device manager events
    _deviceManager.DevicesChanged -= OnDeviceManagerDevicesChanged;

    // Unsubscribe from master mixer events
    _masterMixer.MasterVolumeChanged -= OnMasterVolumeChanged;
    _masterMixer.MuteStateChanged -= OnMuteStateChanged;

    // Clear sources
    _masterMixer.ClearSources();

    // Flush and cleanup fingerprint tap
    if (_fingerprintTap != null)
    {
      _fingerprintTap.Flush();
      _fingerprintTap = null;
    }

    // Stop and dispose playback device
    if (_playbackDevice != null)
    {
      try
      {
        // Gated: shutdown teardown can overlap an in-flight hot-plug enumeration, and both
        // drive the same PulseAudio main loop.
        var retiringDevice = _playbackDevice;
        NativeAudioDeviceGate.Run(retiringDevice.Stop);
        NativeAudioDeviceGate.Run(retiringDevice.Dispose);
      }
      catch (Exception ex)
      {
        _logger.LogWarning(ex, "Error stopping playback device during disposal");
      }
      _playbackDevice = null;
    }

    // Dispose output tap
    if (_outputTap != null)
    {
      await _outputTap.DisposeAsync();
      _outputTap = null;
    }

    // Clear shared engine reference before disposing so device manager
    // doesn't try to use a disposed engine
    if (_deviceManager is SoundFlowDeviceManager sfDeviceManager)
    {
      sfDeviceManager.SetSharedEngine(null);
    }

    // Dispose the SoundFlow engine
    if (_engine != null)
    {
      _engine.Dispose();
      _engine = null;
    }

    // Restore previous GC latency mode
    GCSettings.LatencyMode = _previousLatencyMode;
    _logger.LogInformation(
      "GC latency mode restored to {Mode}", _previousLatencyMode);

    _logger.LogInformation("Audio engine disposed");
  }
}

/// <summary>
/// Diagnostic snapshot of the audio pipeline state.
/// </summary>
public struct PipelineDiagnostics
{
  public string EngineState { get; set; }
  public bool PlaybackDeviceActive { get; set; }
  public int ModifierCount { get; set; }
  public long OutputTapAvailableBytes { get; set; }
  public long FingerprintTapTotalSamples { get; set; }
  public DateTime? FingerprintTapLastProcessedTime { get; set; }
}
