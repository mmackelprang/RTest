using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
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

  private MiniAudioEngine? _engine;
  private AudioPlaybackDevice? _playbackDevice;
  private AudioFormat _audioFormat;
  private TappedOutputStream? _outputTap;
  private FingerprintTapModifier? _fingerprintTap;
  private Timer? _hotPlugTimer;
  private AudioEngineState _state = AudioEngineState.Uninitialized;
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
  public SoundFlowAudioEngine(
    ILogger<SoundFlowAudioEngine> logger,
    IOptions<AudioEngineOptions> options,
    SoundFlowMasterMixer masterMixer,
    SoundFlowDeviceManager deviceManager)
  {
    _logger = logger;
    _options = options.Value;
    _masterMixer = masterMixer;
    _deviceManager = deviceManager;

    // Subscribe to device manager events
    _deviceManager.DevicesChanged += OnDeviceManagerDevicesChanged;
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
        _logger.LogWarning("No playback devices found. Audio output will not be available.");
      }

      // Create output tap for streaming
      _outputTap = new TappedOutputStream(
        _options.SampleRate,
        _options.Channels,
        _options.OutputBufferSizeSeconds);

      // Add fingerprint tap modifier to capture mixed audio for fingerprinting/streaming
      if (_playbackDevice != null)
      {
        _fingerprintTap = new FingerprintTapModifier(this, _logger);
        _playbackDevice.MasterMixer.AddModifier(_fingerprintTap);
        _logger.LogInformation("Fingerprint tap modifier added to MasterMixer");
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
  /// Gets the initialized playback device.
  /// </summary>
  /// <returns>The playback device, or null if not initialized.</returns>
  internal AudioPlaybackDevice? GetPlaybackDevice() => _playbackDevice;

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
      _playbackDevice.Start();

      _logger.LogInformation("Successfully switched to playback device: {DeviceName}", newDevice.Name);
      return true;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to switch to playback device: {DeviceName}", newDevice.Name);
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
