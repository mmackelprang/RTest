using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Models.Audio;

namespace Radio.Infrastructure.Audio.Sources.Primary;

/// <summary>
/// Raddy RF320 USB Radio audio source.
/// Captures audio from a USB audio input device.
/// </summary>
public class RadioAudioSource : USBAudioSourceBase
{
  private readonly IOptionsMonitor<DeviceOptions> _deviceOptions;

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
    : base(logger, deviceManager)
  {
    _deviceOptions = deviceOptions;
  }

  /// <inheritdoc/>
  public override string Name => "Radio (RF320)";

  /// <inheritdoc/>
  public override AudioSourceType Type => AudioSourceType.Radio;

  /// <summary>
  /// Gets the USB port path for the radio device.
  /// </summary>
  public string USBPort => _deviceOptions.CurrentValue.Radio.USBPort;

  /// <inheritdoc/>
  protected override async Task InitializeAsync(CancellationToken cancellationToken = default)
  {
    var usbPort = _deviceOptions.CurrentValue.Radio.USBPort;

    // Set standard metadata with defaults for Radio source
    MetadataInternal[StandardMetadataKeys.Title] = "Radio";
    MetadataInternal[StandardMetadataKeys.Artist] = StandardMetadataKeys.DefaultArtist;
    MetadataInternal[StandardMetadataKeys.Album] = StandardMetadataKeys.DefaultAlbum;
    MetadataInternal[StandardMetadataKeys.AlbumArtUrl] = StandardMetadataKeys.DefaultAlbumArtUrl;
    MetadataInternal["Source"] = "Radio";
    MetadataInternal["Device"] = "Raddy RF320";

    await InitializeUSBCaptureAsync(usbPort, cancellationToken);
  }
}
