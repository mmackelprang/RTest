namespace Radio.API.Models;

/// <summary>
/// Represents the current state of a radio device.
/// </summary>
public class RadioStateDto
{
  /// <summary>
  /// Gets or sets the current frequency in Hertz (Hz).
  /// </summary>
  public long Frequency { get; set; }

  /// <summary>
  /// Gets or sets the current band (AM, FM, etc.).
  /// </summary>
  public string Band { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets the frequency step size in Hertz (Hz).
  /// </summary>
  public long FrequencyStep { get; set; }

  /// <summary>
  /// Gets or sets the signal strength as a percentage (0-100).
  /// </summary>
  public int SignalStrength { get; set; }

  /// <summary>
  /// Gets or sets a value indicating whether the radio is receiving a stereo signal.
  /// </summary>
  public bool IsStereo { get; set; }

  /// <summary>
  /// Gets or sets the current equalizer mode.
  /// </summary>
  public string EqualizerMode { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets the device-specific volume (0-100).
  /// </summary>
  public int DeviceVolume { get; set; }

  /// <summary>
  /// Gets or sets a value indicating whether the radio is currently scanning.
  /// </summary>
  public bool IsScanning { get; set; }

  /// <summary>
  /// Gets or sets the scan direction if scanning; otherwise, null.
  /// </summary>
  public string? ScanDirection { get; set; }

  /// <summary>
  /// Gets or sets a value indicating whether automatic gain control is enabled.
  /// </summary>
  public bool AutoGainEnabled { get; set; }

  /// <summary>
  /// Gets or sets the manual gain value in dB (only effective when AutoGainEnabled is false).
  /// </summary>
  public float Gain { get; set; }

  /// <summary>
  /// Gets or sets a value indicating whether the radio receiver is running.
  /// </summary>
  public bool IsRunning { get; set; }
}

/// <summary>
/// Request to set radio frequency.
/// </summary>
public class SetFrequencyRequest
{
  /// <summary>
  /// Gets or sets the frequency to tune to in Hertz (Hz).
  /// </summary>
  public long Frequency { get; set; }
}

/// <summary>
/// Request to set radio band.
/// </summary>
public class SetBandRequest
{
  /// <summary>
  /// Gets or sets the band to switch to (AM, FM, etc.).
  /// </summary>
  public string Band { get; set; } = string.Empty;
}

/// <summary>
/// Request to set frequency step size.
/// </summary>
public class SetFrequencyStepRequest
{
  /// <summary>
  /// Gets or sets the step size in Hertz (Hz).
  /// </summary>
  public long Step { get; set; }
}

/// <summary>
/// Request to start scanning.
/// </summary>
public class StartScanRequest
{
  /// <summary>
  /// Gets or sets the scan direction (Up or Down).
  /// </summary>
  public string Direction { get; set; } = string.Empty;
}

/// <summary>
/// Request to set equalizer mode.
/// </summary>
public class SetEqualizerModeRequest
{
  /// <summary>
  /// Gets or sets the equalizer mode.
  /// </summary>
  public string Mode { get; set; } = string.Empty;
}

/// <summary>
/// Request to set device volume.
/// </summary>
public class SetDeviceVolumeRequest
{
  /// <summary>
  /// Gets or sets the volume level (0-100).
  /// </summary>
  public int Volume { get; set; }
}

/// <summary>
/// Request to set manual gain value.
/// </summary>
public class SetGainRequest
{
  /// <summary>
  /// Gets or sets the gain value in dB.
  /// </summary>
  public float Gain { get; set; }
}

/// <summary>
/// Request to toggle automatic gain control.
/// </summary>
public class SetAutoGainRequest
{
  /// <summary>
  /// Gets or sets whether automatic gain control should be enabled.
  /// </summary>
  public bool Enabled { get; set; }
}

/// <summary>
/// Request to select a radio device type.
/// </summary>
public class SelectRadioDeviceRequest
{
  /// <summary>
  /// Gets or sets the device type to select (e.g., "RTLSDRCore", "RF320").
  /// </summary>
  public string DeviceType { get; set; } = string.Empty;
}

/// <summary>
/// Information about a radio device type.
/// </summary>
public class RadioDeviceInfoDto
{
  /// <summary>
  /// Gets or sets the device type identifier.
  /// </summary>
  public string DeviceType { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets whether the device is currently available.
  /// </summary>
  public bool IsAvailable { get; set; }

  /// <summary>
  /// Gets or sets whether the device is currently active.
  /// </summary>
  public bool IsActive { get; set; }

  /// <summary>
  /// Gets or sets the device capabilities.
  /// </summary>
  public RadioDeviceCapabilitiesDto Capabilities { get; set; } = new();
}

/// <summary>
/// List of available radio devices.
/// </summary>
public class RadioDeviceListDto
{
  /// <summary>
  /// Gets or sets the list of radio devices.
  /// </summary>
  public List<RadioDeviceInfoDto> Devices { get; set; } = new();

  /// <summary>
  /// Gets or sets the total count of available devices.
  /// </summary>
  public int Count { get; set; }
}

/// <summary>
/// Capabilities of a radio device type.
/// </summary>
public class RadioDeviceCapabilitiesDto
{
  /// <summary>
  /// Gets or sets whether the device supports software control.
  /// </summary>
  public bool SupportsSoftwareControl { get; set; }

  /// <summary>
  /// Gets or sets whether the device supports frequency control.
  /// </summary>
  public bool SupportsFrequencyControl { get; set; }

  /// <summary>
  /// Gets or sets whether the device supports band switching.
  /// </summary>
  public bool SupportsBandSwitching { get; set; }

  /// <summary>
  /// Gets or sets whether the device supports automatic scanning.
  /// </summary>
  public bool SupportsScanning { get; set; }

  /// <summary>
  /// Gets or sets whether the device supports gain control.
  /// </summary>
  public bool SupportsGainControl { get; set; }

  /// <summary>
  /// Gets or sets whether the device supports equalizer.
  /// </summary>
  public bool SupportsEqualizer { get; set; }

  /// <summary>
  /// Gets or sets whether the device supports volume control.
  /// </summary>
  public bool SupportsDeviceVolume { get; set; }

  /// <summary>
  /// Gets or sets a human-readable description of the device.
  /// </summary>
  public string Description { get; set; } = string.Empty;
}
