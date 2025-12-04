using Microsoft.Extensions.Logging;
using Radio.Core.Events;
using Radio.Core.Exceptions;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Models.Audio;
using Radio.Infrastructure.Audio.Fingerprinting;
using SoundFlow.Abstracts.Devices;
using SoundFlow.Backends.MiniAudio;
using SoundFlow.Enums;
using SoundFlow.Structs;

namespace Radio.Infrastructure.Audio.Sources.Primary;

/// <summary>
/// Base class for USB audio sources that capture audio from USB audio input devices.
/// Provides common functionality for USB port reservation, sound component management,
/// live stream handling, and fingerprinting integration.
/// </summary>
public abstract class USBAudioSourceBase : PrimaryAudioSourceBase
{
  private readonly IAudioDeviceManager _deviceManager;
  private readonly Dictionary<string, object> _metadata = new();
  private readonly BackgroundIdentificationService? _identificationService;
  private string? _reservedPort;
  private object? _soundComponent;
  private AudioCaptureDevice? _captureDevice;
  private MiniAudioEngine? _audioEngine;

  /// <summary>
  /// Initializes a new instance of the <see cref="USBAudioSourceBase"/> class.
  /// </summary>
  /// <param name="logger">The logger instance.</param>
  /// <param name="deviceManager">The audio device manager.</param>
  /// <param name="identificationService">Optional fingerprinting service for track identification.</param>
  protected USBAudioSourceBase(
    ILogger logger, 
    IAudioDeviceManager deviceManager,
    BackgroundIdentificationService? identificationService = null)
    : base(logger)
  {
    _deviceManager = deviceManager;
    _identificationService = identificationService;

    // Subscribe to track identification events if service is available
    if (_identificationService != null)
    {
      _identificationService.TrackIdentified += OnTrackIdentified;
    }
  }

  /// <inheritdoc/>
  public override TimeSpan? Duration => null; // Live stream has no duration

  /// <inheritdoc/>
  public override TimeSpan Position => TimeSpan.Zero; // Live stream has no position

  /// <inheritdoc/>
  public override bool IsSeekable => false; // Live input cannot be seeked

  /// <inheritdoc/>
  public override IReadOnlyDictionary<string, object> Metadata => _metadata;

  /// <summary>
  /// Gets the reserved USB port path, or null if not reserved.
  /// </summary>
  public string? ReservedUSBPort => _reservedPort;

  /// <summary>
  /// Gets the audio device manager.
  /// </summary>
  protected IAudioDeviceManager DeviceManager => _deviceManager;

  /// <summary>
  /// Gets the metadata dictionary for modification by derived classes.
  /// </summary>
  protected Dictionary<string, object> MetadataInternal => _metadata;

  /// <summary>
  /// Gets or sets the sound component. Can be accessed by derived classes.
  /// </summary>
  protected object? SoundComponent
  {
    get => _soundComponent;
    set => _soundComponent = value;
  }

  /// <inheritdoc/>
  public override object GetSoundComponent()
  {
    return _soundComponent ?? throw new InvalidOperationException("Audio source not initialized");
  }

  /// <summary>
  /// Reserves a USB port for this audio source.
  /// </summary>
  /// <param name="usbPort">The USB port path to reserve.</param>
  /// <exception cref="AudioDeviceConflictException">Thrown if the port is already in use.</exception>
  protected void ReserveUSBPort(string usbPort)
  {
    if (_deviceManager.IsUSBPortInUse(usbPort))
    {
      Logger.LogError("USB port {USBPort} is already in use", usbPort);
      State = AudioSourceState.Error;
      throw new AudioDeviceConflictException(
        $"USB port '{usbPort}' is already in use by another source",
        usbPort,
        Id);
    }

    _deviceManager.ReserveUSBPort(usbPort, Id);
    _reservedPort = usbPort;
    _metadata["USBPort"] = usbPort;
  }

  /// <summary>
  /// Releases the reserved USB port if one is held.
  /// </summary>
  protected void ReleaseUSBPort()
  {
    if (_reservedPort != null)
    {
      _deviceManager.ReleaseUSBPort(_reservedPort);
      Logger.LogDebug("Released USB port {USBPort}", _reservedPort);
      _reservedPort = null;
    }
  }

  /// <summary>
  /// Initializes the USB audio capture on the specified port.
  /// </summary>
  /// <param name="usbPort">The USB port to use.</param>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>A task representing the async operation.</returns>
  protected virtual async Task InitializeUSBCaptureAsync(string usbPort, CancellationToken cancellationToken = default)
  {
    await base.InitializeAsync(cancellationToken);

    ReserveUSBPort(usbPort);

    try
    {
      // Create SoundFlow MiniAudioEngine for audio capture
      _audioEngine = new MiniAudioEngine();

      // Find the USB capture device matching the port
      var captureDevices = _audioEngine.CaptureDevices;
      DeviceInfo? targetDevice = null;

      foreach (var device in captureDevices)
      {
        // Match by device name containing the USB port identifier
        // USB devices on Linux typically include card/device info in the name
        if (device.Name.Contains(usbPort, StringComparison.OrdinalIgnoreCase))
        {
          targetDevice = device;
          break;
        }
      }

      // If no specific device found, use the default capture device if available
      if (targetDevice == null && captureDevices.Length > 0)
      {
        Logger.LogWarning(
          "Could not find USB capture device for port {USBPort}, using first available capture device",
          usbPort);
        targetDevice = captureDevices[0];
      }

      // Initialize the capture device with standard CD quality audio format (44.1kHz stereo)
      _captureDevice = _audioEngine.InitializeCaptureDevice(targetDevice, AudioFormat.Cd);

      // Subscribe to audio capture events
      _captureDevice.OnAudioProcessed += OnAudioCaptured;

      // Store the capture device as the sound component
      _soundComponent = _captureDevice;

      Logger.LogInformation(
        "{SourceName} initialized on USB port {USBPort} using device: {DeviceName}",
        Name, usbPort, targetDevice?.Name ?? "default");
      State = AudioSourceState.Ready;
    }
    catch (Exception ex)
    {
      Logger.LogError(ex, "Failed to initialize {SourceName} audio capture on {USBPort}", Name, usbPort);
      CleanupCaptureDevice();
      ReleaseUSBPort();
      State = AudioSourceState.Error;
      throw;
    }
  }

  /// <summary>
  /// Called when audio samples are captured from the USB device.
  /// Override in derived classes to process the captured audio.
  /// </summary>
  /// <param name="samples">The captured audio samples.</param>
  /// <param name="capability">The device capability (should be Record for capture).</param>
  protected virtual void OnAudioCaptured(Span<float> samples, Capability capability)
  {
    // Default implementation does nothing
    // Derived classes can override to process audio samples
  }

  /// <summary>
  /// Cleans up the capture device and audio engine.
  /// </summary>
  private void CleanupCaptureDevice()
  {
    if (_captureDevice != null)
    {
      _captureDevice.OnAudioProcessed -= OnAudioCaptured;
      _captureDevice.Dispose();
      _captureDevice = null;
    }

    if (_audioEngine != null)
    {
      _audioEngine.Dispose();
      _audioEngine = null;
    }
  }

  /// <inheritdoc/>
  protected override Task PlayCoreAsync(CancellationToken cancellationToken)
  {
    if (_soundComponent == null)
    {
      throw new InvalidOperationException($"{Name} audio source not initialized");
    }

    Logger.LogInformation("Starting {SourceName} audio capture", Name);
    return Task.CompletedTask;
  }

  /// <inheritdoc/>
  protected override Task PauseCoreAsync(CancellationToken cancellationToken)
  {
    Logger.LogInformation("Pausing {SourceName} audio (muting)", Name);
    return Task.CompletedTask;
  }

  /// <inheritdoc/>
  protected override Task ResumeCoreAsync(CancellationToken cancellationToken)
  {
    Logger.LogInformation("Resuming {SourceName} audio", Name);
    return Task.CompletedTask;
  }

  /// <inheritdoc/>
  protected override Task StopCoreAsync(CancellationToken cancellationToken)
  {
    Logger.LogInformation("Stopping {SourceName} audio capture", Name);
    return Task.CompletedTask;
  }

  /// <summary>
  /// Handles the TrackIdentified event from the fingerprinting service.
  /// Updates metadata with identified track information.
  /// </summary>
  /// <param name="sender">The event sender.</param>
  /// <param name="e">The event arguments containing track metadata.</param>
  private void OnTrackIdentified(object? sender, TrackIdentifiedEventArgs e)
  {
    // Only update metadata if this is the active source
    if (State != AudioSourceState.Playing && State != AudioSourceState.Paused)
    {
      return;
    }

    var track = e.Track;
    Logger.LogInformation(
      "Updating {SourceName} metadata from fingerprinting: {Title} by {Artist} (confidence: {Confidence:P0})",
      Name, track.Title, track.Artist, e.Confidence);

    // Update metadata with fingerprinted track information using StandardMetadataKeys
    UpdateMetadataFromFingerprint(track, e.Confidence, e.IdentifiedAt);
  }

  /// <summary>
  /// Updates metadata from fingerprinted track information.
  /// Can be overridden by derived classes to customize behavior.
  /// </summary>
  /// <param name="track">The identified track metadata.</param>
  /// <param name="confidence">The confidence level of the identification.</param>
  /// <param name="identifiedAt">When the track was identified.</param>
  protected virtual void UpdateMetadataFromFingerprint(TrackMetadata track, double confidence, DateTime identifiedAt)
  {
    // Store current source/device info to restore later
    var sourceInfo = _metadata.TryGetValue("Source", out var source) ? source : null;
    var deviceInfo = _metadata.TryGetValue("Device", out var device) ? device : null;

    // Update standard metadata fields
    _metadata[StandardMetadataKeys.Title] = track.Title;
    _metadata[StandardMetadataKeys.Artist] = track.Artist;
    _metadata[StandardMetadataKeys.Album] = track.Album ?? StandardMetadataKeys.DefaultAlbum;
    
    // Use CoverArtUrl from fingerprinting if available, otherwise use default
    _metadata[StandardMetadataKeys.AlbumArtUrl] = !string.IsNullOrEmpty(track.CoverArtUrl)
      ? track.CoverArtUrl
      : StandardMetadataKeys.DefaultAlbumArtUrl;

    // Add optional metadata if available
    if (track.Genre != null)
    {
      _metadata[StandardMetadataKeys.Genre] = track.Genre;
    }

    if (track.ReleaseYear.HasValue)
    {
      _metadata[StandardMetadataKeys.Year] = track.ReleaseYear.Value;
    }

    if (track.TrackNumber.HasValue)
    {
      _metadata[StandardMetadataKeys.TrackNumber] = track.TrackNumber.Value;
    }

    // Restore source/device information
    if (sourceInfo != null)
    {
      _metadata["Source"] = sourceInfo;
    }
    if (deviceInfo != null)
    {
      _metadata["Device"] = deviceInfo;
    }

    // Add fingerprinting metadata
    _metadata["IdentificationConfidence"] = confidence;
    _metadata["IdentifiedAt"] = identifiedAt;
  }

  /// <inheritdoc/>
  protected override async ValueTask DisposeAsyncCore()
  {
    // Unsubscribe from fingerprinting events
    if (_identificationService != null)
    {
      _identificationService.TrackIdentified -= OnTrackIdentified;
    }

    CleanupCaptureDevice();
    ReleaseUSBPort();
    _soundComponent = null;
    await base.DisposeAsyncCore();
  }
}
