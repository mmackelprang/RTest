using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Core.Exceptions;
using Radio.Core.Interfaces.Audio;

namespace Radio.Infrastructure.Audio.Sources.Primary;

/// <summary>
/// Raddy RF320 USB Radio audio source.
/// Captures audio from a USB audio input device.
/// </summary>
public class RadioAudioSource : PrimaryAudioSourceBase
{
  private readonly IOptionsMonitor<DeviceOptions> _deviceOptions;
  private readonly IAudioDeviceManager _deviceManager;
  private readonly Dictionary<string, string> _metadata = new();
  private string? _reservedPort;
  private object? _soundComponent;

  /// <summary>
  /// Initializes a new instance of the <see cref="RadioAudioSource"/> class.
  /// </summary>
  /// <param name="logger">The logger instance.</param>
  /// <param name="deviceOptions">The device options configuration.</param>
  /// <param name="deviceManager">The audio device manager.</param>
  public RadioAudioSource(
    ILogger<RadioAudioSource> logger,
    IOptionsMonitor<DeviceOptions> deviceOptions,
    IAudioDeviceManager deviceManager)
    : base(logger)
  {
    _deviceOptions = deviceOptions;
    _deviceManager = deviceManager;
  }

  /// <inheritdoc/>
  public override string Name => "Radio (RF320)";

  /// <inheritdoc/>
  public override AudioSourceType Type => AudioSourceType.Radio;

  /// <inheritdoc/>
  public override TimeSpan? Duration => null; // Live stream has no duration

  /// <inheritdoc/>
  public override TimeSpan Position => TimeSpan.Zero; // Live stream has no position

  /// <inheritdoc/>
  public override bool IsSeekable => false; // Live input cannot be seeked

  /// <inheritdoc/>
  public override IReadOnlyDictionary<string, string> Metadata => _metadata;

  /// <summary>
  /// Gets the USB port path for the radio device.
  /// </summary>
  public string USBPort => _deviceOptions.CurrentValue.Radio.USBPort;

  /// <inheritdoc/>
  public override object GetSoundComponent()
  {
    return _soundComponent ?? throw new InvalidOperationException("Audio source not initialized");
  }

  /// <inheritdoc/>
  protected override async Task InitializeAsync(CancellationToken cancellationToken = default)
  {
    await base.InitializeAsync(cancellationToken);

    var usbPort = _deviceOptions.CurrentValue.Radio.USBPort;

    // Check if USB port is available
    if (_deviceManager.IsUSBPortInUse(usbPort))
    {
      Logger.LogError("USB port {USBPort} is already in use", usbPort);
      State = AudioSourceState.Error;
      throw new AudioDeviceConflictException(
        $"USB port '{usbPort}' is already in use by another source",
        usbPort,
        Id);
    }

    // Reserve the USB port
    _deviceManager.ReserveUSBPort(usbPort, Id);
    _reservedPort = usbPort;

    try
    {
      // Create SoundFlow audio capture for USB input
      // In a real implementation, this would create a SoundFlow capture device
      _soundComponent = new object(); // Placeholder for actual SoundFlow component

      _metadata["Source"] = "Radio";
      _metadata["Device"] = "Raddy RF320";
      _metadata["USBPort"] = usbPort;

      Logger.LogInformation("Radio audio source initialized on USB port {USBPort}", usbPort);
      State = AudioSourceState.Ready;
    }
    catch (Exception ex)
    {
      Logger.LogError(ex, "Failed to initialize radio audio capture on {USBPort}", usbPort);
      _deviceManager.ReleaseUSBPort(usbPort);
      _reservedPort = null;
      State = AudioSourceState.Error;
      throw;
    }
  }

  /// <inheritdoc/>
  protected override Task PlayCoreAsync(CancellationToken cancellationToken)
  {
    if (_soundComponent == null)
    {
      throw new InvalidOperationException("Radio audio source not initialized");
    }

    // Start capturing audio from the USB device
    Logger.LogInformation("Starting radio audio capture");

    // In a real implementation, this would start the SoundFlow capture
    return Task.CompletedTask;
  }

  /// <inheritdoc/>
  protected override Task PauseCoreAsync(CancellationToken cancellationToken)
  {
    // For live input, pause is effectively muting
    Logger.LogInformation("Pausing radio audio (muting)");
    return Task.CompletedTask;
  }

  /// <inheritdoc/>
  protected override Task ResumeCoreAsync(CancellationToken cancellationToken)
  {
    Logger.LogInformation("Resuming radio audio");
    return Task.CompletedTask;
  }

  /// <inheritdoc/>
  protected override Task StopCoreAsync(CancellationToken cancellationToken)
  {
    Logger.LogInformation("Stopping radio audio capture");
    return Task.CompletedTask;
  }

  /// <inheritdoc/>
  protected override async ValueTask DisposeAsyncCore()
  {
    // Release the USB port reservation
    if (_reservedPort != null)
    {
      _deviceManager.ReleaseUSBPort(_reservedPort);
      Logger.LogDebug("Released USB port {USBPort}", _reservedPort);
      _reservedPort = null;
    }

    _soundComponent = null;
    await base.DisposeAsyncCore();
  }
}
