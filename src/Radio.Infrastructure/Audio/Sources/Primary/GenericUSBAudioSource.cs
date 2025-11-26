using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Core.Exceptions;
using Radio.Core.Interfaces.Audio;

namespace Radio.Infrastructure.Audio.Sources.Primary;

/// <summary>
/// Generic USB audio source that allows users to select any USB audio device.
/// </summary>
public class GenericUSBAudioSource : USBAudioSourceBase
{
  private readonly IOptionsMonitor<GenericSourcePreferences> _preferences;
  private string? _deviceId;

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
    : base(logger, deviceManager)
  {
    _preferences = preferences;
  }

  /// <inheritdoc/>
  public override string Name => "Generic USB Audio";

  /// <inheritdoc/>
  public override AudioSourceType Type => AudioSourceType.GenericUSB;

  /// <summary>
  /// Gets the USB port path for the selected device.
  /// </summary>
  public string? USBPort => ReservedUSBPort;

  /// <summary>
  /// Gets the device ID of the selected audio device.
  /// </summary>
  public string? DeviceId => _deviceId;

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

    // Check if USB port is available before attempting to reserve
    if (DeviceManager.IsUSBPortInUse(usbPort))
    {
      Logger.LogError("USB port {USBPort} is already in use by another source", usbPort);
      throw new AudioDeviceConflictException(
        $"USB port '{usbPort}' is already reserved by another audio source. " +
        "Stop the other source before using this port.",
        usbPort,
        Id);
    }

    MetadataInternal["Source"] = "Generic USB";

    await InitializeUSBCaptureAsync(usbPort, cancellationToken);

    // Save to preferences for next session
    _preferences.CurrentValue.USBPort = usbPort;
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
    MetadataInternal["DeviceName"] = device.Name;
    MetadataInternal["DeviceId"] = device.Id;

    await InitializeWithPortAsync(device.USBPort, cancellationToken);
  }

  /// <inheritdoc/>
  protected override async Task InitializeAsync(CancellationToken cancellationToken = default)
  {
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
    if (ReservedUSBPort == null)
    {
      throw new InvalidOperationException("Generic USB audio source not initialized with a device");
    }

    return base.PlayCoreAsync(cancellationToken);
  }

  /// <inheritdoc/>
  protected override async ValueTask DisposeAsyncCore()
  {
    _deviceId = null;
    await base.DisposeAsyncCore();
  }
}
