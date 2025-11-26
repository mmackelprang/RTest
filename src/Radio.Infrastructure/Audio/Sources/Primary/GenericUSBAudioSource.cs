using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Core.Exceptions;
using Radio.Core.Interfaces.Audio;

namespace Radio.Infrastructure.Audio.Sources.Primary;

/// <summary>
/// Generic USB audio source that allows users to select any USB audio device.
/// </summary>
public class GenericUSBAudioSource : PrimaryAudioSourceBase
{
  private readonly IOptionsMonitor<GenericSourcePreferences> _preferences;
  private readonly IAudioDeviceManager _deviceManager;
  private readonly Dictionary<string, string> _metadata = new();
  private string? _reservedPort;
  private string? _deviceId;
  private object? _soundComponent;

  /// <summary>
  /// Initializes a new instance of the <see cref="GenericUSBAudioSource"/> class.
  /// </summary>
  /// <param name="logger">The logger instance.</param>
  /// <param name="preferences">The generic source preferences.</param>
  /// <param name="deviceManager">The audio device manager.</param>
  public GenericUSBAudioSource(
    ILogger<GenericUSBAudioSource> logger,
    IOptionsMonitor<GenericSourcePreferences> preferences,
    IAudioDeviceManager deviceManager)
    : base(logger)
  {
    _preferences = preferences;
    _deviceManager = deviceManager;
  }

  /// <inheritdoc/>
  public override string Name => "Generic USB Audio";

  /// <inheritdoc/>
  public override AudioSourceType Type => AudioSourceType.GenericUSB;

  /// <inheritdoc/>
  public override TimeSpan? Duration => null; // Live stream has no duration

  /// <inheritdoc/>
  public override TimeSpan Position => TimeSpan.Zero; // Live stream has no position

  /// <inheritdoc/>
  public override bool IsSeekable => false; // Live input cannot be seeked

  /// <inheritdoc/>
  public override IReadOnlyDictionary<string, string> Metadata => _metadata;

  /// <summary>
  /// Gets the USB port path for the selected device.
  /// </summary>
  public string? USBPort => _reservedPort;

  /// <summary>
  /// Gets the device ID of the selected audio device.
  /// </summary>
  public string? DeviceId => _deviceId;

  /// <inheritdoc/>
  public override object GetSoundComponent()
  {
    return _soundComponent ?? throw new InvalidOperationException("Audio source not initialized");
  }

  /// <summary>
  /// Initializes the audio source with a specific USB port.
  /// </summary>
  /// <param name="usbPort">The USB port path to use.</param>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>A task representing the async operation.</returns>
  /// <exception cref="AudioDeviceConflictException">Thrown if the port is already in use.</exception>
  public async Task InitializeWithPortAsync(string usbPort, CancellationToken cancellationToken = default)
  {
    ThrowIfDisposed();

    // Check if USB port is available
    if (_deviceManager.IsUSBPortInUse(usbPort))
    {
      Logger.LogError("USB port {USBPort} is already in use", usbPort);
      throw new AudioDeviceConflictException(
        $"USB port '{usbPort}' is already in use by another source. " +
        "Please select a different device or stop the conflicting source.",
        usbPort,
        Id);
    }

    // Reserve and connect
    _deviceManager.ReserveUSBPort(usbPort, Id);
    _reservedPort = usbPort;

    try
    {
      // Create SoundFlow audio capture for USB input
      _soundComponent = new object(); // Placeholder for actual SoundFlow component

      _metadata["Source"] = "Generic USB";
      _metadata["USBPort"] = usbPort;

      // Save to preferences for next session
      _preferences.CurrentValue.USBPort = usbPort;

      Logger.LogInformation("Generic USB audio source initialized on port {USBPort}", usbPort);
      State = AudioSourceState.Ready;
    }
    catch (Exception ex)
    {
      Logger.LogError(ex, "Failed to initialize generic USB audio capture on {USBPort}", usbPort);
      _deviceManager.ReleaseUSBPort(usbPort);
      _reservedPort = null;
      State = AudioSourceState.Error;
      throw;
    }

    await Task.CompletedTask;
  }

  /// <summary>
  /// Initializes the audio source with a specific device.
  /// </summary>
  /// <param name="device">The audio device info to use.</param>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>A task representing the async operation.</returns>
  public async Task InitializeWithDeviceAsync(AudioDeviceInfo device, CancellationToken cancellationToken = default)
  {
    ThrowIfDisposed();

    if (!device.IsUSBDevice)
    {
      throw new ArgumentException("Device must be a USB device", nameof(device));
    }

    if (string.IsNullOrEmpty(device.USBPort))
    {
      throw new ArgumentException("Device does not have a USB port", nameof(device));
    }

    _deviceId = device.Id;
    _metadata["DeviceName"] = device.Name;
    _metadata["DeviceId"] = device.Id;

    await InitializeWithPortAsync(device.USBPort, cancellationToken);
  }

  /// <inheritdoc/>
  protected override async Task InitializeAsync(CancellationToken cancellationToken = default)
  {
    await base.InitializeAsync(cancellationToken);

    // Try to use saved USB port from preferences
    var savedPort = _preferences.CurrentValue.USBPort;
    if (!string.IsNullOrEmpty(savedPort))
    {
      Logger.LogDebug("Restoring saved USB port: {USBPort}", savedPort);
      await InitializeWithPortAsync(savedPort, cancellationToken);
    }
    else
    {
      Logger.LogWarning("No USB port configured for generic USB source. Call InitializeWithPortAsync or InitializeWithDeviceAsync first.");
      State = AudioSourceState.Ready;
    }
  }

  /// <inheritdoc/>
  protected override Task PlayCoreAsync(CancellationToken cancellationToken)
  {
    if (_soundComponent == null || _reservedPort == null)
    {
      throw new InvalidOperationException("Generic USB audio source not initialized with a device");
    }

    Logger.LogInformation("Starting generic USB audio capture on {USBPort}", _reservedPort);
    return Task.CompletedTask;
  }

  /// <inheritdoc/>
  protected override Task PauseCoreAsync(CancellationToken cancellationToken)
  {
    Logger.LogInformation("Pausing generic USB audio (muting)");
    return Task.CompletedTask;
  }

  /// <inheritdoc/>
  protected override Task ResumeCoreAsync(CancellationToken cancellationToken)
  {
    Logger.LogInformation("Resuming generic USB audio");
    return Task.CompletedTask;
  }

  /// <inheritdoc/>
  protected override Task StopCoreAsync(CancellationToken cancellationToken)
  {
    Logger.LogInformation("Stopping generic USB audio capture");
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
    _deviceId = null;
    await base.DisposeAsyncCore();
  }
}
