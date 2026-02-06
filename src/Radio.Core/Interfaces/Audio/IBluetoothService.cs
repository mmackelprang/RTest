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
/// Metadata for the currently playing track.
/// </summary>
public class BluetoothPlaybackMetadata
{
  public string Title { get; init; } = string.Empty;
  public string Artist { get; init; } = string.Empty;
  public string Album { get; init; } = string.Empty;
  public TimeSpan Duration { get; init; } = TimeSpan.Zero;
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

  /// <summary>Disconnect current device.</summary>
  Task DisconnectAsync(CancellationToken cancellationToken = default);

  /// <summary>
  /// Gets the audio capture device representing the Bluetooth input stream.
  /// Should be SoundFlow-compatible for downstream pipeline consumption.
  /// </summary>
  Task<object?> GetAudioCaptureDeviceAsync(CancellationToken cancellationToken = default);

  /// <summary>Event raised when adapter state changes.</summary>
  event EventHandler<BluetoothAdapterStateChangedEventArgs>? StateChanged;

  /// <summary>Event raised when device connects.</summary>
  event EventHandler<BluetoothDeviceConnectedEventArgs>? DeviceConnected;

  /// <summary>Event raised when device disconnects.</summary>
  event EventHandler<BluetoothDeviceDisconnectedEventArgs>? DeviceDisconnected;

  /// <summary>Event raised when new device discovered.</summary>
  event EventHandler<BluetoothDeviceDiscoveredEventArgs>? DeviceDiscovered;

  /// <summary>Event raised when playback metadata changes (Track, Artist, etc.).</summary>
  event EventHandler<BluetoothPlaybackMetadata>? MetadataChanged;

  /// <summary>Event raised when playback status changes (Playing, Paused, etc.).</summary>
  event EventHandler<BluetoothPlaybackStatus>? PlaybackStatusChanged;

  /// <summary>Event raised when playback position changes.</summary>
  event EventHandler<TimeSpan>? PositionChanged;
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
}

/// <summary>Device discovered args.</summary>
public class BluetoothDeviceDiscoveredEventArgs : EventArgs
{
  public required BluetoothDeviceInfo Device { get; init; }
}
