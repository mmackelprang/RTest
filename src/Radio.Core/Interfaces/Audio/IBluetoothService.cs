using Microsoft.Extensions.Logging;

namespace Radio.Core.Interfaces.Audio;

/// <summary>
/// Playback status of the Bluetooth media player.
/// </summary>
public enum BluetoothPlaybackStatus
{
  Stopped,
  Playing,
  Paused,
  ForwardSeek,
  ReverseSeek,
  Error
}

/// <summary>
/// Metadata for the currently playing track from AVRCP or platform media APIs.
/// </summary>
public class BluetoothPlaybackMetadata
{
  public string Title { get; init; } = string.Empty;
  public string Artist { get; init; } = string.Empty;
  public string Album { get; init; } = string.Empty;
  public TimeSpan Duration { get; init; } = TimeSpan.Zero;
  public string? AlbumArtUrl { get; init; }
}

public interface IBluetoothService : IAsyncDisposable
{
  /// <summary>Gets whether Bluetooth is available on this platform.</summary>
  bool IsAvailable { get; }

  /// <summary>Gets the current Bluetooth adapter state.</summary>
  BluetoothAdapterState State { get; }

  /// <summary>Gets the list of paired devices.</summary>
  IReadOnlyList<BluetoothDeviceInfo> PairedDevices { get; }

  /// <summary>Gets the list of discovered devices (during discovery).</summary>
  IReadOnlyList<BluetoothDeviceInfo> DiscoveredDevices { get; }

  /// <summary>Indicates whether discovery is active.</summary>
  bool IsDiscovering { get; }

  /// <summary>Gets the currently connected device.</summary>
  BluetoothDeviceInfo? ConnectedDevice { get; }

  /// <summary>
  /// Gets whether the platform manages audio routing directly (e.g., Windows AudioPlaybackConnection
  /// routes to system speakers). When true, no SoundFlow capture device is needed.
  /// </summary>
  bool IsAudioManagedByPlatform { get; }

  /// <summary>Start Bluetooth adapter and make device discoverable.</summary>
  Task<bool> StartAsync(string deviceName, CancellationToken cancellationToken = default);

  /// <summary>Stop Bluetooth adapter.</summary>
  Task StopAsync(CancellationToken cancellationToken = default);

  /// <summary>Start device discovery.</summary>
  Task StartDiscoveryAsync(CancellationToken cancellationToken = default);

  /// <summary>Stop device discovery.</summary>
  Task StopDiscoveryAsync();

  /// <summary>Pair with a discovered device.</summary>
  Task<bool> PairDeviceAsync(string deviceAddress, CancellationToken cancellationToken = default);

  /// <summary>Unpair a device.</summary>
  Task<bool> UnpairDeviceAsync(string deviceAddress, CancellationToken cancellationToken = default);

  /// <summary>Accept incoming connection.</summary>
  Task<bool> AcceptConnectionAsync(string deviceAddress, CancellationToken cancellationToken = default);

  /// <summary>Initiate outbound connection to a paired device.</summary>
  Task<bool> ConnectAsync(string deviceAddress, CancellationToken cancellationToken = default);

  /// <summary>Disconnect current device.</summary>
  Task DisconnectAsync(CancellationToken cancellationToken = default);

  /// <summary>Disconnect a specific device by address.</summary>
  Task DisconnectAsync(string deviceAddress, CancellationToken cancellationToken = default);

  /// <summary>
  /// Gets the audio capture device representing the Bluetooth input stream.
  /// Should be SoundFlow-compatible for downstream pipeline consumption.
  /// </summary>
  Task<object?> GetAudioCaptureDeviceAsync(CancellationToken cancellationToken = default);

  /// <summary>
  /// Stops the audio capture subprocess and clears the cached generator.
  /// Does NOT stop the Bluetooth adapter — the adapter stays powered and discoverable.
  /// Call this when the BT audio source is stopped so a fresh capture can be created on re-play.
  /// </summary>
  void StopAudioCapture();

  /// <summary>Event raised when adapter state changes.</summary>
  event EventHandler<BluetoothAdapterStateChangedEventArgs>? StateChanged;

  /// <summary>Event raised when device connects.</summary>
  event EventHandler<BluetoothDeviceConnectedEventArgs>? DeviceConnected;

  /// <summary>Event raised when device disconnects.</summary>
  event EventHandler<BluetoothDeviceDisconnectedEventArgs>? DeviceDisconnected;

  /// <summary>Event raised when new device discovered.</summary>
  event EventHandler<BluetoothDeviceDiscoveredEventArgs>? DeviceDiscovered;

  /// <summary>
  /// Raised when the pipeline monitor successfully recovers a lost capture stream.
  /// Subscribers should re-attach the capture generator to their audio mixer.
  /// </summary>
  event EventHandler? CaptureStreamRecovered;

  /// <summary>Event raised when playback metadata changes (Track, Artist, etc.).</summary>
  event EventHandler<BluetoothPlaybackMetadata>? MetadataChanged;

  /// <summary>Event raised when playback status changes (Playing, Paused, etc.).</summary>
  event EventHandler<BluetoothPlaybackStatus>? PlaybackStatusChanged;

  /// <summary>Event raised when playback position changes.</summary>
  event EventHandler<TimeSpan>? PositionChanged;

  /// <summary>
  /// Event raised when the Bluetooth device's AVRCP volume changes.
  /// Volume is normalized to 0.0-1.0 range.
  /// </summary>
  event EventHandler<BluetoothVolumeChangedEventArgs>? VolumeChanged;

  /// <summary>
  /// Gets the current Bluetooth device volume (0.0-1.0), or null if not available.
  /// </summary>
  float? DeviceVolume { get; }

  /// <summary>
  /// Sets the Bluetooth device volume via AVRCP absolute volume.
  /// </summary>
  /// <param name="volume">Volume level (0.0 to 1.0).</param>
  Task SetDeviceVolumeAsync(float volume);

  /// <summary>Skip to next track via AVRCP.</summary>
  Task NextTrackAsync(CancellationToken cancellationToken = default);

  /// <summary>Go to previous track via AVRCP.</summary>
  Task PreviousTrackAsync(CancellationToken cancellationToken = default);

  /// <summary>Whether a reconnection loop is currently active.</summary>
  bool IsReconnecting { get; }

  /// <summary>Cancel any active reconnection loop.</summary>
  void CancelReconnection();

  /// <summary>Last disconnect reason for UI display. Null if no disconnect has occurred.</summary>
  BluetoothDisconnectReason? LastDisconnectReason { get; }

  /// <summary>
  /// Gets the current health status of the Bluetooth audio pipeline.
  /// </summary>
  BluetoothPipelineStatus PipelineStatus { get; }
}

/// <summary>
/// Describes the health of the Bluetooth audio capture pipeline.
/// </summary>
public enum BluetoothPipelineStatus
{
  /// <summary>BT is disabled or not started.</summary>
  Inactive,

  /// <summary>No device connected — waiting for connection.</summary>
  Degraded,

  /// <summary>Device connected and capture stream active.</summary>
  Healthy,

  /// <summary>Device connected but capture stream is missing or broken.</summary>
  Broken
}

/// <summary>Bluetooth adapter states.</summary>
public enum BluetoothAdapterState
{
  Unknown,
  Off,
  TurningOn,
  On,
  TurningOff,
  Error
}

/// <summary>Bluetooth device information.</summary>
public class BluetoothDeviceInfo
{
  public required string Address { get; init; }
  public required string Name { get; init; }
  public bool IsPaired { get; init; }
  public bool IsConnected { get; init; }
  public DateTime? LastConnected { get; init; }
}

/// <summary>Adapter state changed args.</summary>
public class BluetoothAdapterStateChangedEventArgs : EventArgs
{
  public BluetoothAdapterState PreviousState { get; init; }
  public BluetoothAdapterState NewState { get; init; }
}

/// <summary>Device connected args.</summary>
public class BluetoothDeviceConnectedEventArgs : EventArgs
{
  public required BluetoothDeviceInfo Device { get; init; }
}

/// <summary>Device disconnected args.</summary>
public class BluetoothDeviceDisconnectedEventArgs : EventArgs
{
  public required BluetoothDeviceInfo Device { get; init; }
  /// <summary>True if disconnect was user-initiated (via DisconnectAsync).</summary>
  public bool UserInitiated { get; init; }
  /// <summary>Disconnect reason from BlueZ management protocol.</summary>
  public BluetoothDisconnectReason Reason { get; init; } = BluetoothDisconnectReason.Unknown;
}

/// <summary>Device discovered args.</summary>
public class BluetoothDeviceDiscoveredEventArgs : EventArgs
{
  public required BluetoothDeviceInfo Device { get; init; }
}

/// <summary>Bluetooth device volume changed args.</summary>
public class BluetoothVolumeChangedEventArgs : EventArgs
{
  /// <summary>Normalized volume (0.0 to 1.0).</summary>
  public required float Volume { get; init; }
}
