using Microsoft.Extensions.Logging;
using Radio.Core.Exceptions;
using Radio.Core.Interfaces.Audio;
using SoundFlow.Abstracts.Devices;
using SoundFlow.Backends.MiniAudio;
using SoundFlow.Enums;
using SoundFlow.Structs;

namespace Radio.Infrastructure.Audio.Sources.Primary;

/// <summary>
/// Base class for USB audio sources that capture audio from USB audio input devices.
/// Provides common functionality for USB port reservation, sound component management,
/// and live stream handling.
/// </summary>
public abstract class USBAudioSourceBase : PrimaryAudioSourceBase
{
  private readonly IAudioDeviceManager _deviceManager;
  private readonly Dictionary<string, object> _metadata = new();
  private string? _reservedPort;
  private object? _soundComponent;
  private AudioCaptureDevice? _captureDevice;
  private MiniAudioEngine? _audioEngine;

  /// <summary>
  /// Initializes a new instance of the <see cref="USBAudioSourceBase"/> class.
  /// </summary>
  /// <param name="logger">The logger instance.</param>
  /// <param name="deviceManager">The audio device manager.</param>
  protected USBAudioSourceBase(ILogger logger, IAudioDeviceManager deviceManager)
    : base(logger)
  {
    _deviceManager = deviceManager;
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

  /// <inheritdoc/>
  protected override async ValueTask DisposeAsyncCore()
  {
    CleanupCaptureDevice();
    ReleaseUSBPort();
    _soundComponent = null;
    await base.DisposeAsyncCore();
  }
}
