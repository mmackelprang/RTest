using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;
using SoundFlow.Backends.MiniAudio;

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
  private TappedOutputStream? _outputTap;
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

      // Create output tap for streaming
      _outputTap = new TappedOutputStream(
        _options.SampleRate,
        _options.Channels,
        _options.OutputBufferSizeSeconds);

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
