using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Models.Audio;
using Radio.Infrastructure.Audio.Fingerprinting;

namespace Radio.Infrastructure.Audio.Sources.Primary;

/// <summary>
/// Vinyl turntable USB audio source.
/// Captures audio from a USB audio input device connected to a turntable.
/// Supports automatic track identification via fingerprinting.
/// </summary>
public class VinylAudioSource : USBAudioSourceBase
{
  private readonly IOptionsMonitor<DeviceOptions> _deviceOptions;

  /// <summary>
  /// Initializes a new instance of the <see cref="VinylAudioSource"/> class.
  /// </summary>
  /// <param name="logger">The logger instance.</param>
  /// <param name="deviceOptions">The device options configuration.</param>
  /// <param name="deviceManager">The audio device manager.</param>
  /// <param name="identificationService">Optional fingerprinting service for track identification.</param>
  public VinylAudioSource(
    ILogger<VinylAudioSource> logger,
    IOptionsMonitor<DeviceOptions> deviceOptions,
    IAudioDeviceManager deviceManager,
    BackgroundIdentificationService? identificationService = null)
    : base(logger, deviceManager, identificationService)
  {
    _deviceOptions = deviceOptions;
  }

  /// <inheritdoc/>
  public override string Name => "Vinyl Turntable";

  /// <inheritdoc/>
  public override AudioSourceType Type => AudioSourceType.Vinyl;

  /// <summary>
  /// Gets the USB port path for the vinyl device.
  /// </summary>
  public string USBPort => _deviceOptions.CurrentValue.Vinyl.USBPort;

  /// <inheritdoc/>
  protected override async Task InitializeAsync(CancellationToken cancellationToken = default)
  {
    var usbPort = _deviceOptions.CurrentValue.Vinyl.USBPort;

    // Set standard metadata with defaults for Vinyl source
    SetDefaultMetadata("Vinyl", "Vinyl", "Turntable");

    await InitializeUSBCaptureAsync(usbPort, cancellationToken);
  }
}
