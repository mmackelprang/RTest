using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Core.Events;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Models.Audio;
using Radio.Infrastructure.Audio.Fingerprinting;

namespace Radio.Infrastructure.Audio.Sources.Primary;

/// <summary>
/// Raddy RF320 USB Radio audio source.
/// Captures audio from a USB audio input device.
/// Supports automatic track identification via fingerprinting.
/// </summary>
public class RadioAudioSource : USBAudioSourceBase
{
  private readonly IOptionsMonitor<DeviceOptions> _deviceOptions;
  private readonly BackgroundIdentificationService? _identificationService;

  /// <summary>
  /// Initializes a new instance of the <see cref="RadioAudioSource"/> class.
  /// </summary>
  /// <param name="logger">The logger instance.</param>
  /// <param name="deviceOptions">The device options configuration.</param>
  /// <param name="deviceManager">The audio device manager.</param>
  /// <param name="identificationService">Optional fingerprinting service for track identification.</param>
  public RadioAudioSource(
    ILogger<RadioAudioSource> logger,
    IOptionsMonitor<DeviceOptions> deviceOptions,
    IAudioDeviceManager deviceManager,
    BackgroundIdentificationService? identificationService = null)
    : base(logger, deviceManager)
  {
    _deviceOptions = deviceOptions;
    _identificationService = identificationService;

    // Subscribe to track identification events if service is available
    if (_identificationService != null)
    {
      _identificationService.TrackIdentified += OnTrackIdentified;
    }
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
    SetDefaultMetadata();

    await InitializeUSBCaptureAsync(usbPort, cancellationToken);
  }

  /// <summary>
  /// Sets default metadata for the Radio source when no track is identified.
  /// </summary>
  private void SetDefaultMetadata()
  {
    MetadataInternal[StandardMetadataKeys.Title] = "Radio";
    MetadataInternal[StandardMetadataKeys.Artist] = StandardMetadataKeys.DefaultArtist;
    MetadataInternal[StandardMetadataKeys.Album] = StandardMetadataKeys.DefaultAlbum;
    MetadataInternal[StandardMetadataKeys.AlbumArtUrl] = StandardMetadataKeys.DefaultAlbumArtUrl;
    MetadataInternal["Source"] = "Radio";
    MetadataInternal["Device"] = "Raddy RF320";
  }

  /// <summary>
  /// Handles the TrackIdentified event from the fingerprinting service.
  /// Updates metadata with identified track information.
  /// </summary>
  private void OnTrackIdentified(object? sender, TrackIdentifiedEventArgs e)
  {
    // Only update metadata if this is the active source
    if (State != AudioSourceState.Playing && State != AudioSourceState.Paused)
    {
      return;
    }

    var track = e.Track;
    Logger.LogInformation(
      "Updating Radio metadata from fingerprinting: {Title} by {Artist} (confidence: {Confidence:P0})",
      track.Title, track.Artist, e.Confidence);

    // Update metadata with fingerprinted track information using StandardMetadataKeys
    MetadataInternal[StandardMetadataKeys.Title] = track.Title;
    MetadataInternal[StandardMetadataKeys.Artist] = track.Artist;
    MetadataInternal[StandardMetadataKeys.Album] = track.Album ?? StandardMetadataKeys.DefaultAlbum;
    
    // Use CoverArtUrl from fingerprinting if available, otherwise use default
    MetadataInternal[StandardMetadataKeys.AlbumArtUrl] = !string.IsNullOrEmpty(track.CoverArtUrl)
      ? track.CoverArtUrl
      : StandardMetadataKeys.DefaultAlbumArtUrl;

    // Add optional metadata if available
    if (track.Genre != null)
    {
      MetadataInternal[StandardMetadataKeys.Genre] = track.Genre;
    }

    if (track.ReleaseYear.HasValue)
    {
      MetadataInternal[StandardMetadataKeys.Year] = track.ReleaseYear.Value;
    }

    if (track.TrackNumber.HasValue)
    {
      MetadataInternal[StandardMetadataKeys.TrackNumber] = track.TrackNumber.Value;
    }

    // Keep source information
    MetadataInternal["Source"] = "Radio";
    MetadataInternal["Device"] = "Raddy RF320";
    MetadataInternal["IdentificationConfidence"] = e.Confidence;
    MetadataInternal["IdentifiedAt"] = e.IdentifiedAt;
  }

  /// <inheritdoc/>
  protected override async ValueTask DisposeAsyncCore()
  {
    // Unsubscribe from events
    if (_identificationService != null)
    {
      _identificationService.TrackIdentified -= OnTrackIdentified;
    }

    await base.DisposeAsyncCore();
  }
}
