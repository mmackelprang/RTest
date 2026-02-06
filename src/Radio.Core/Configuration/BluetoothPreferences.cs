using System.Collections.Generic;

namespace Radio.Core.Configuration;

/// <summary>
/// User and device preference data for Bluetooth audio.
/// Loaded from the 'BluetoothPreferences' configuration section.
/// </summary>
public class BluetoothPreferences
{
  /// <summary>Configuration section name.</summary>
  public const string SectionName = "BluetoothPreferences";

  /// <summary>Last connected device address.</summary>
  public string? LastConnectedDevice { get; set; }

  /// <summary>List of paired device addresses.</summary>
  public List<string> PairedDevices { get; set; } = new();

  /// <summary>Device trust list (auto-connect without prompt).</summary>
  public List<string> TrustedDevices { get; set; } = new();
}
