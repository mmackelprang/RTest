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
