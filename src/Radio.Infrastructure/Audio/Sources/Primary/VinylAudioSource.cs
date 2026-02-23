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
  private readonly string? _resolvedUSBPort;

  /// <summary>
  /// Initializes a new instance of the <see cref="VinylAudioSource"/> class.
  /// </summary>
  /// <param name="logger">The logger instance.</param>
  /// <param name="deviceOptions">The device options configuration.</param>
  /// <param name="deviceManager">The audio device manager.</param>
  /// <param name="identificationService">Optional fingerprinting service for track identification.</param>
  /// <param name="resolvedUSBPort">USB port resolved from config store, overrides IOptionsMonitor value.</param>
  public VinylAudioSource(
    ILogger<VinylAudioSource> logger,
    IOptionsMonitor<DeviceOptions> deviceOptions,
    IAudioDeviceManager deviceManager,
    BackgroundIdentificationService? identificationService = null,
    string? resolvedUSBPort = null)
    : base(logger, deviceManager, identificationService)
  {
    _deviceOptions = deviceOptions;
    _resolvedUSBPort = resolvedUSBPort;
  }

  /// <inheritdoc/>
  public override string Name => "Vinyl Turntable";

  /// <inheritdoc/>
  public override AudioSourceType Type => AudioSourceType.Vinyl;

  /// <summary>
  /// Gets the USB port path for the vinyl device.
  /// Prefers the resolved config store value over IOptionsMonitor.
  /// </summary>
  public string USBPort => !string.IsNullOrWhiteSpace(_resolvedUSBPort)
    ? _resolvedUSBPort
    : _deviceOptions.CurrentValue.Vinyl.USBPort;

  /// <inheritdoc/>
  public override async Task InitializeAsync(CancellationToken cancellationToken = default)
  {
    var usbPort = USBPort;

    // Set standard metadata with defaults for Vinyl source
    SetDefaultMetadata("Vinyl", "Vinyl", "Turntable");

    await InitializeUSBCaptureAsync(usbPort, cancellationToken);
  }
}
