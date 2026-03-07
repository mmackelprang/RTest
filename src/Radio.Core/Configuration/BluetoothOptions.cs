namespace Radio.Core.Configuration;

/// <summary>
/// Configuration options for Bluetooth audio input (A2DP sink).
/// Loaded from the 'Bluetooth' configuration section.
/// </summary>
public class BluetoothOptions
{
  /// <summary>Configuration section name.</summary>
  public const string SectionName = "Bluetooth";

  /// <summary>Device name advertised to Bluetooth clients (e.g., "Grandpa's Radio").</summary>
  public string DeviceName { get; set; } = "Radio Console";

  /// <summary>Automatically accept incoming connection requests.</summary>
  public bool AutoAcceptConnections { get; set; } = true;

  /// <summary>Require pairing before connecting.</summary>
  public bool RequirePairing { get; set; } = false;

  /// <summary>Master enable/disable switch.</summary>
  public bool Enabled { get; set; } = true;

  /// <summary>Enable Bluetooth on startup.</summary>
  public bool EnableOnStartup { get; set; } = true;

  /// <summary>Automatically switch to Bluetooth source when device connects.</summary>
  public bool AutoSwitchOnConnect { get; set; } = true;

  /// <summary>Audio quality settings.</summary>
  public BluetoothAudioQuality AudioQuality { get; set; } = BluetoothAudioQuality.High;

  /// <summary>
  /// Enable Windows AudioPlaybackConnection for A2DP sink (receive audio from phone).
  /// Requires Windows 10 2004+ (build 19041) and MSIX sparse package identity.
  /// </summary>
  public bool EnableA2dpSink { get; set; } = true;

  /// <summary>
  /// Enable Windows SMTC (System Media Transport Controls) monitoring for AVRCP-equivalent metadata.
  /// Provides track title, artist, album, duration, and album art from connected devices.
  /// </summary>
  public bool EnableMediaSessionMonitoring { get; set; } = true;

  /// <summary>
  /// Enable WASAPI loopback capture to route Bluetooth audio through SoundFlow pipeline.
  /// Windows only. When enabled, BT audio goes through SoundFlow for Cast, visualization,
  /// modifiers, and output device selection. When false, platform manages audio directly
  /// (no Cast/viz for BT audio).
  /// </summary>
  public bool EnableLoopbackCapture { get; set; } = true;

  /// <summary>
  /// Preferred Bluetooth adapter address (e.g., "78:20:51:F5:FB:A7").
  /// When set, the service will prefer this adapter over others.
  /// Useful when multiple adapters are present (e.g., built-in + USB dongle).
  /// </summary>
  public string? PreferredAdapterAddress { get; set; }

  /// <summary>Automatically reconnect to known device after unexpected disconnect.</summary>
  public bool AutoReconnect { get; set; } = true;

  /// <summary>Base delay in ms for exponential backoff reconnection (first attempt).</summary>
  public int ReconnectBaseDelayMs { get; set; } = 3000;

  /// <summary>Maximum delay in ms between reconnection attempts (backoff cap).</summary>
  public int ReconnectMaxDelayMs { get; set; } = 60000;

  /// <summary>Maximum number of reconnection attempts before giving up.</summary>
  public int MaxReconnectAttempts { get; set; } = 20;
}

/// <summary>
/// Bluetooth audio quality options.
/// </summary>
public enum BluetoothAudioQuality
{
  /// <summary>Standard quality (44.1kHz).</summary>
  Standard,

  /// <summary>High quality (48kHz).</summary>
  High
}
