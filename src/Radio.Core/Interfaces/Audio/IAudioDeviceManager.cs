namespace Radio.Core.Interfaces.Audio;

/// <summary>
/// Interface for enumerating and managing audio devices.
/// Handles system ALSA/PulseAudio/USB device discovery and selection.
/// </summary>
public interface IAudioDeviceManager
{
  /// <summary>
  /// Gets all available audio output devices.
  /// </summary>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>A list of available output devices.</returns>
  Task<IReadOnlyList<AudioDeviceInfo>> GetOutputDevicesAsync(CancellationToken cancellationToken = default);

  /// <summary>
  /// Gets all available audio input devices (USB audio, microphones, etc.).
  /// </summary>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>A list of available input devices.</returns>
  Task<IReadOnlyList<AudioDeviceInfo>> GetInputDevicesAsync(CancellationToken cancellationToken = default);

  /// <summary>
  /// Gets the current default output device.
  /// </summary>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>The default output device, or null if none available.</returns>
  Task<AudioDeviceInfo?> GetDefaultOutputDeviceAsync(CancellationToken cancellationToken = default);

  /// <summary>
  /// Sets the preferred output device.
  /// </summary>
  /// <param name="deviceId">The device ID to set as output.</param>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>A task representing the async operation.</returns>
  Task SetOutputDeviceAsync(string deviceId, CancellationToken cancellationToken = default);

  /// <summary>
  /// Checks if a specific USB port is already in use by another source.
  /// </summary>
  /// <param name="usbPort">The USB port path (e.g., /dev/ttyUSB0).</param>
  /// <returns>True if the port is in use.</returns>
  bool IsUSBPortInUse(string usbPort);

  /// <summary>
  /// Reserves a USB port for exclusive use by a source.
  /// </summary>
  /// <param name="usbPort">The USB port path to reserve.</param>
  /// <param name="sourceId">The ID of the source reserving the port.</param>
  /// <exception cref="Exceptions.AudioDeviceConflictException">Thrown if the port is already in use.</exception>
  void ReserveUSBPort(string usbPort, string sourceId);

  /// <summary>
  /// Releases a USB port reservation.
  /// </summary>
  /// <param name="usbPort">The USB port path to release.</param>
  void ReleaseUSBPort(string usbPort);

  /// <summary>
  /// Refreshes the list of available devices (manual hot-plug check).
  /// </summary>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>A task representing the async operation.</returns>
  Task RefreshDevicesAsync(CancellationToken cancellationToken = default);

  /// <summary>
  /// Event raised when devices are added or removed.
  /// </summary>
  event EventHandler<AudioDeviceChangedEventArgs>? DevicesChanged;
}

/// <summary>
/// Information about an audio device.
/// </summary>
public record AudioDeviceInfo
{
  /// <summary>
  /// Gets the unique identifier for this device.
  /// </summary>
  public required string Id { get; init; }

  /// <summary>
  /// Gets the human-readable name of the device.
  /// </summary>
  public required string Name { get; init; }

  /// <summary>
  /// Gets the type of audio device (input, output, or duplex).
  /// </summary>
  public required AudioDeviceType Type { get; init; }

  /// <summary>
  /// Gets whether this is the system default device.
  /// </summary>
  public bool IsDefault { get; init; }

  /// <summary>
  /// Gets the maximum number of audio channels supported.
  /// </summary>
  public int MaxChannels { get; init; }

  /// <summary>
  /// Gets the sample rates supported by this device.
  /// </summary>
  public int[] SupportedSampleRates { get; init; } = [];

  /// <summary>
  /// Gets the ALSA device ID (Linux-specific).
  /// </summary>
  public string? AlsaDeviceId { get; init; }

  /// <summary>
  /// Gets the USB port path if this is a USB device.
  /// </summary>
  public string? USBPort { get; init; }

  /// <summary>
  /// Gets whether this is a USB audio device.
  /// </summary>
  public bool IsUSBDevice { get; init; }
}

/// <summary>
/// Types of audio devices.
/// </summary>
public enum AudioDeviceType
{
  /// <summary>Audio output device (speakers, headphones).</summary>
  Output,

  /// <summary>Audio input device (microphone, line-in).</summary>
  Input,

  /// <summary>Duplex device supporting both input and output.</summary>
  Duplex
}
