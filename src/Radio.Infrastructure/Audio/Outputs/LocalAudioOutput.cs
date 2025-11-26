using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;

namespace Radio.Infrastructure.Audio.Outputs;

/// <summary>
/// Local audio output implementation using SoundFlow's default output device.
/// Routes mixed audio to the system's local speakers or selected audio device.
/// </summary>
public class LocalAudioOutput : AudioOutputBase
{
  private readonly ILogger<LocalAudioOutput> _logger;
  private readonly IAudioDeviceManager _deviceManager;
  private readonly LocalAudioOutputOptions _options;
  private string? _currentDeviceId;

  /// <inheritdoc />
  protected override ILogger Logger => _logger;

  /// <inheritdoc />
  public override AudioOutputType Type => AudioOutputType.Local;

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
    : base("local-output", "Local Audio Output",
        options?.Value?.Local?.DefaultVolume ?? 0.8f,
        options?.Value?.Local?.Enabled ?? true)
  {
    _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    _deviceManager = deviceManager ?? throw new ArgumentNullException(nameof(deviceManager));
    _options = options?.Value?.Local ?? throw new ArgumentNullException(nameof(options));
  }

  /// <inheritdoc />
  public override async Task InitializeAsync(CancellationToken cancellationToken = default)
  {
    ValidateCanInitialize();

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
        OnStateChanged(AudioOutputState.Initializing, AudioOutputState.Error, "No audio output devices found");
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
      OnStateChanged(AudioOutputState.Initializing, AudioOutputState.Error, ex.Message);
      throw;
    }
  }

  /// <inheritdoc />
  public override Task StartAsync(CancellationToken cancellationToken = default)
  {
    ValidateCanStart();

    try
    {
      _logger.LogInformation("Starting local audio output");

      // Local output uses the SoundFlow engine's default playback
      // which is already connected to the master mixer
      IsEnabledInternal = true;

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
  public override Task StopAsync(CancellationToken cancellationToken = default)
  {
    if (!ValidateCanStop())
    {
      return Task.CompletedTask;
    }

    try
    {
      State = AudioOutputState.Stopping;
      _logger.LogInformation("Stopping local audio output");

      IsEnabledInternal = false;

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

  /// <inheritdoc />
  public override ValueTask DisposeAsync()
  {
    if (IsDisposed)
    {
      return ValueTask.CompletedTask;
    }

    DisposeBase();

    return ValueTask.CompletedTask;
  }
}
