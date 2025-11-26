using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;

namespace Radio.Infrastructure.Audio.Outputs;

/// <summary>
/// Local audio output implementation using SoundFlow's default output device.
/// Routes mixed audio to the system's local speakers or selected audio device.
/// </summary>
public class LocalAudioOutput : IAudioOutput
{
  private readonly ILogger<LocalAudioOutput> _logger;
  private readonly IAudioDeviceManager _deviceManager;
  private readonly LocalAudioOutputOptions _options;
  private readonly object _stateLock = new();

  private AudioOutputState _state = AudioOutputState.Created;
  private float _volume;
  private bool _isMuted;
  private bool _isEnabled;
  private bool _disposed;
  private string? _currentDeviceId;

  /// <inheritdoc />
  public string Id { get; }

  /// <inheritdoc />
  public string Name { get; private set; }

  /// <inheritdoc />
  public AudioOutputType Type => AudioOutputType.Local;

  /// <inheritdoc />
  public AudioOutputState State
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
      AudioOutputState previousState;
      lock (_stateLock)
      {
        previousState = _state;
        _state = value;
      }

      if (previousState != value)
      {
        _logger.LogInformation(
          "Local audio output state changed from {PreviousState} to {NewState}",
          previousState, value);

        StateChanged?.Invoke(this, new AudioOutputStateChangedEventArgs
        {
          PreviousState = previousState,
          NewState = value,
          OutputId = Id
        });
      }
    }
  }

  /// <inheritdoc />
  public float Volume
  {
    get => _volume;
    set
    {
      var clamped = Math.Clamp(value, 0f, 1f);
      if (Math.Abs(_volume - clamped) > 0.0001f)
      {
        _volume = clamped;
        _logger.LogDebug("Local audio output volume set to {Volume:P0}", _volume);
      }
    }
  }

  /// <inheritdoc />
  public bool IsMuted
  {
    get => _isMuted;
    set
    {
      if (_isMuted != value)
      {
        _isMuted = value;
        _logger.LogDebug("Local audio output mute set to {IsMuted}", _isMuted);
      }
    }
  }

  /// <inheritdoc />
  public bool IsEnabled => _isEnabled;

  /// <inheritdoc />
  public event EventHandler<AudioOutputStateChangedEventArgs>? StateChanged;

  /// <summary>
  /// Gets the currently selected device ID.
  /// </summary>
  public string? CurrentDeviceId => _currentDeviceId;

  /// <summary>
  /// Initializes a new instance of the <see cref="LocalAudioOutput"/> class.
  /// </summary>
  /// <param name="logger">The logger instance.</param>
  /// <param name="deviceManager">The audio device manager.</param>
  /// <param name="options">The local audio output options.</param>
  public LocalAudioOutput(
    ILogger<LocalAudioOutput> logger,
    IAudioDeviceManager deviceManager,
    IOptions<AudioOutputOptions> options)
  {
    _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    _deviceManager = deviceManager ?? throw new ArgumentNullException(nameof(deviceManager));
    _options = options?.Value?.Local ?? throw new ArgumentNullException(nameof(options));

    Id = $"local-output-{Guid.NewGuid():N}";
    Name = "Local Audio Output";
    _volume = _options.DefaultVolume;
    _isEnabled = _options.Enabled;
  }

  /// <inheritdoc />
  public async Task InitializeAsync(CancellationToken cancellationToken = default)
  {
    ThrowIfDisposed();

    if (State != AudioOutputState.Created && State != AudioOutputState.Error)
    {
      throw new InvalidOperationException(
        $"Cannot initialize output in state {State}. Output must be in Created or Error state.");
    }

    State = AudioOutputState.Initializing;

    try
    {
      _logger.LogInformation("Initializing local audio output");

      // Get available output devices
      var devices = await _deviceManager.GetOutputDevicesAsync(cancellationToken);

      if (devices.Count == 0)
      {
        _logger.LogWarning("No audio output devices found");
        State = AudioOutputState.Error;
        RaiseStateChanged(AudioOutputState.Initializing, AudioOutputState.Error, "No audio output devices found");
        return;
      }

      // Select preferred device or default
      AudioDeviceInfo? selectedDevice = null;

      if (!string.IsNullOrEmpty(_options.PreferredDeviceId))
      {
        selectedDevice = devices.FirstOrDefault(d => d.Id == _options.PreferredDeviceId);
        if (selectedDevice == null)
        {
          _logger.LogWarning(
            "Preferred device '{DeviceId}' not found, using default",
            _options.PreferredDeviceId);
        }
      }

      selectedDevice ??= await _deviceManager.GetDefaultOutputDeviceAsync(cancellationToken)
        ?? devices.First();

      _currentDeviceId = selectedDevice.Id;
      Name = $"Local: {selectedDevice.Name}";

      _logger.LogInformation(
        "Local audio output initialized with device: {DeviceName} ({DeviceId})",
        selectedDevice.Name, selectedDevice.Id);

      State = AudioOutputState.Ready;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to initialize local audio output");
      State = AudioOutputState.Error;
      RaiseStateChanged(AudioOutputState.Initializing, AudioOutputState.Error, ex.Message);
      throw;
    }
  }

  /// <inheritdoc />
  public Task StartAsync(CancellationToken cancellationToken = default)
  {
    ThrowIfDisposed();

    if (State != AudioOutputState.Ready && State != AudioOutputState.Stopped)
    {
      throw new InvalidOperationException(
        $"Cannot start output in state {State}. Output must be in Ready or Stopped state.");
    }

    try
    {
      _logger.LogInformation("Starting local audio output");

      // Local output uses the SoundFlow engine's default playback
      // which is already connected to the master mixer
      _isEnabled = true;

      State = AudioOutputState.Streaming;
      _logger.LogInformation("Local audio output started");
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to start local audio output");
      State = AudioOutputState.Error;
      throw;
    }

    return Task.CompletedTask;
  }

  /// <inheritdoc />
  public Task StopAsync(CancellationToken cancellationToken = default)
  {
    ThrowIfDisposed();

    if (State != AudioOutputState.Streaming)
    {
      _logger.LogWarning("Stop requested but output is not streaming (state: {State})", State);
      return Task.CompletedTask;
    }

    try
    {
      State = AudioOutputState.Stopping;
      _logger.LogInformation("Stopping local audio output");

      _isEnabled = false;

      State = AudioOutputState.Stopped;
      _logger.LogInformation("Local audio output stopped");
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to stop local audio output");
      State = AudioOutputState.Error;
      throw;
    }

    return Task.CompletedTask;
  }

  /// <summary>
  /// Selects a different output device.
  /// </summary>
  /// <param name="deviceId">The device ID to select.</param>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>A task representing the async operation.</returns>
  public async Task SelectDeviceAsync(string deviceId, CancellationToken cancellationToken = default)
  {
    ThrowIfDisposed();
    ArgumentException.ThrowIfNullOrEmpty(deviceId);

    var devices = await _deviceManager.GetOutputDevicesAsync(cancellationToken);
    var device = devices.FirstOrDefault(d => d.Id == deviceId);

    if (device == null)
    {
      throw new ArgumentException($"Device '{deviceId}' not found", nameof(deviceId));
    }

    await _deviceManager.SetOutputDeviceAsync(deviceId, cancellationToken);
    _currentDeviceId = deviceId;
    Name = $"Local: {device.Name}";

    _logger.LogInformation(
      "Local audio output device changed to: {DeviceName} ({DeviceId})",
      device.Name, deviceId);
  }

  /// <summary>
  /// Gets the effective volume considering mute state.
  /// </summary>
  /// <returns>The effective volume (0 if muted, otherwise the volume level).</returns>
  public float GetEffectiveVolume()
  {
    return _isMuted ? 0f : _volume;
  }

  private void RaiseStateChanged(AudioOutputState previousState, AudioOutputState newState, string? errorMessage = null)
  {
    StateChanged?.Invoke(this, new AudioOutputStateChangedEventArgs
    {
      PreviousState = previousState,
      NewState = newState,
      OutputId = Id,
      ErrorMessage = errorMessage
    });
  }

  private void ThrowIfDisposed()
  {
    ObjectDisposedException.ThrowIf(_disposed, this);
  }

  /// <inheritdoc />
  public ValueTask DisposeAsync()
  {
    if (_disposed)
    {
      return ValueTask.CompletedTask;
    }

    _disposed = true;
    _isEnabled = false;
    State = AudioOutputState.Disposed;

    _logger.LogInformation("Local audio output disposed");

    return ValueTask.CompletedTask;
  }
}
