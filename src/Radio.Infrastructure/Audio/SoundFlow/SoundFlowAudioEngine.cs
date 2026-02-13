using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Core.Interfaces;
using Radio.Core.Interfaces.Audio;
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
  private Timer? _hotPlugTimer;
  private AudioEngineState _state = AudioEngineState.Uninitialized;
  private int _currentDeviceIndex = -1;
  private bool _disposed;
  private readonly object _stateLock = new();

  /// <inheritdoc/>
  public event EventHandler<AudioEngineStateChangedEventArgs>? StateChanged;

  /// <inheritdoc/>
  public event EventHandler<AudioDeviceChangedEventArgs>? DeviceChanged;

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
      _logger.LogInformation(
        "Initializing SoundFlow audio engine (SampleRate: {SampleRate}, Channels: {Channels}, BufferSize: {BufferSize})",
        _options.SampleRate, _options.Channels, _options.BufferSize);

      // Initialize SoundFlow MiniAudioEngine
      _engine = new MiniAudioEngine();

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
        // Use the first (default) playback device
        var deviceInfo = playbackDevices[0];
        _logger.LogInformation("Initializing playback device: {DeviceName}", deviceInfo.Name);

        try
        {
          // Initialize the playback device with our format
          _playbackDevice = _engine.InitializePlaybackDevice(deviceInfo, _audioFormat);
          _currentDeviceIndex = 0;

          // Apply initial volume/mute state
          _playbackDevice.MasterMixer.Volume = _masterMixer.GetEffectiveVolume();

          _playbackDevice.Start();
          _logger.LogInformation("Playback device initialized and started: {DeviceName}", deviceInfo.Name);

          // Add fingerprint tap modifier to capture mixed audio for fingerprinting/streaming
          // This must be done AFTER output tap is created, so we defer it
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

      // Add modifiers to capture mixed audio for fingerprinting/streaming
      if (_playbackDevice != null)
      {
        // Add balance modifier first (before fingerprint tap)
        _balanceModifier = new BalanceModifier(_masterMixer);
        _playbackDevice.MasterMixer.AddModifier(_balanceModifier);
        _logger.LogInformation("Balance modifier added to MasterMixer");

        // Add fingerprint tap modifier after balance.
        // Use 2048-sample buffer (~21ms at 48kHz stereo) instead of default 4096 (~42ms)
        // to reduce latency for HTTP streaming to Cast devices.
        _fingerprintTap = new FingerprintTapModifier(this, _logger, bufferSize: 2048, metricsCollector: _metricsCollector);
        _playbackDevice.MasterMixer.AddModifier(_fingerprintTap);
        _logger.LogInformation("Fingerprint tap modifier added to MasterMixer");

        // Add visualization tap modifier for real-time spectrum/level/waveform data
        if (_visualizerService != null)
        {
          _visualizationTap = new VisualizationTapModifier(_visualizerService, _audioFormat);
          _playbackDevice.MasterMixer.AddModifier(_visualizationTap);
          _logger.LogInformation("Visualization tap modifier added to MasterMixer");
        }
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
  public Stream CreateStreamReader(string readerId)
  {
    ThrowIfDisposed();

    if (_outputTap == null)
    {
      throw new InvalidOperationException(
        "Audio engine not initialized. Call InitializeAsync first.");
    }

    // Start the reader 1 second behind the write position so Cast/HTTP clients
    // get an immediate burst of audio data instead of waiting for new writes.
    // Without this lag, Cast devices timeout before the first FingerprintTapModifier
    // batch arrives (~42ms) and the LAME encoder produces its first MP3 frames.
    var lagBytes = _options.SampleRate * _options.Channels * 2; // 1 second of 16-bit PCM
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
      return _playbackDevice;

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

      _playbackDevice.MasterMixer.Volume = _masterMixer.GetEffectiveVolume();
      _playbackDevice.Start();

      // Re-attach modifiers
      if (_balanceModifier != null)
      {
        _playbackDevice.MasterMixer.AddModifier(_balanceModifier);
      }
      else
      {
        _balanceModifier = new BalanceModifier(_masterMixer);
        _playbackDevice.MasterMixer.AddModifier(_balanceModifier);
      }

      if (_fingerprintTap != null)
      {
        _playbackDevice.MasterMixer.AddModifier(_fingerprintTap);
      }
      else
      {
        _fingerprintTap = new FingerprintTapModifier(this, _logger, metricsCollector: _metricsCollector);
        _playbackDevice.MasterMixer.AddModifier(_fingerprintTap);
      }

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
      // Stop and dispose current playback device
      if (_playbackDevice != null)
      {
        _playbackDevice.Stop();
        _playbackDevice.Dispose();
        _playbackDevice = null;
      }

      // Initialize new playback device
      _playbackDevice = _engine.InitializePlaybackDevice(newDevice, _audioFormat);
      _currentDeviceIndex = deviceIndex;

      // Apply current volume/mute state
      _playbackDevice.MasterMixer.Volume = _masterMixer.GetEffectiveVolume();

      // Re-attach balance modifier (before fingerprint tap)
      if (_balanceModifier != null)
      {
        _playbackDevice.MasterMixer.AddModifier(_balanceModifier);
        _logger.LogInformation("Balance modifier re-attached to new playback device");
      }
      else
      {
        _balanceModifier = new BalanceModifier(_masterMixer);
        _playbackDevice.MasterMixer.AddModifier(_balanceModifier);
        _logger.LogInformation("Balance modifier created and attached to new playback device");
      }

      // Re-attach fingerprint tap if it exists
      if (_fingerprintTap != null)
      {
        _playbackDevice.MasterMixer.AddModifier(_fingerprintTap);
        _logger.LogInformation("Fingerprint tap re-attached to new playback device");
      }
      // If it didn't exist (e.g. started with no device), create it now
      else
      {
        _fingerprintTap = new FingerprintTapModifier(this, _logger, metricsCollector: _metricsCollector);
        _playbackDevice.MasterMixer.AddModifier(_fingerprintTap);
        _logger.LogInformation("Fingerprint tap created and attached to new playback device");
      }

      // Re-attach visualization tap
      if (_visualizationTap != null)
      {
        _playbackDevice.MasterMixer.AddModifier(_visualizationTap);
        _logger.LogInformation("Visualization tap re-attached to new playback device");
      }
      else if (_visualizerService != null)
      {
        _visualizationTap = new VisualizationTapModifier(_visualizerService, _audioFormat);
        _playbackDevice.MasterMixer.AddModifier(_visualizationTap);
        _logger.LogInformation("Visualization tap created and attached to new playback device");
      }

      _playbackDevice.Start();

      _logger.LogInformation("Successfully switched to playback device: {DeviceName}", newDevice.Name);
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
      ModifierCount = _fingerprintTap != null ? 1 : 0,
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
    if (_disposed) return;

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
    if (_playbackDevice != null)
    {
      // Apply volume if not muted. If muted, Volume should be 0, but IsMuted handles that.
      // However, SoundFlow Mixer Volume is usually the gain.
      // If we use _masterMixer.GetEffectiveVolume(), it handles mute logic.
      _playbackDevice.MasterMixer.Volume = _masterMixer.GetEffectiveVolume();
    }
  }

  private void OnMuteStateChanged(object? sender, bool isMuted)
  {
    if (_playbackDevice != null)
    {
       _playbackDevice.MasterMixer.Volume = _masterMixer.GetEffectiveVolume();
    }
  }

  private void ThrowIfDisposed()
  {
    ObjectDisposedException.ThrowIf(_disposed, this);
  }

  /// <inheritdoc/>
  public async ValueTask DisposeAsync()
  {
    if (_disposed) return;

    _disposed = true;
    State = AudioEngineState.Disposed;

    _logger.LogInformation("Disposing audio engine");

    // Stop hot-plug detection
    if (_hotPlugTimer != null)
    {
      await _hotPlugTimer.DisposeAsync();
      _hotPlugTimer = null;
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
        _playbackDevice.Stop();
        _playbackDevice.Dispose();
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

    // Dispose the SoundFlow engine
    if (_engine != null)
    {
      _engine.Dispose();
      _engine = null;
    }

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
