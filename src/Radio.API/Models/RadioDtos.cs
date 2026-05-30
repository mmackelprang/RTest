namespace Radio.API.Models;

/// <summary>
/// Represents the current state of a radio device.
/// </summary>
public class RadioStateDto
{
  /// <summary>
  /// Gets or sets the current frequency in Hertz (Hz).
  /// </summary>
  public double Frequency { get; set; }

  /// <summary>
  /// Gets or sets the current band (AM, FM, etc.).
  /// </summary>
  public string Band { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets the frequency step size in Hertz (Hz).
  /// </summary>
  public double Step { get; set; }

  /// <summary>
  /// Gets or sets the signal strength as a percentage (0-100), clamped at the
  /// API boundary. Values are guaranteed never to exceed 100% even if the raw
  /// front-end power reading is overdriven — see <see cref="Clip"/>.
  /// </summary>
  public int? SignalStrength { get; set; }

  /// <summary>
  /// Gets or sets a value indicating whether the front-end is overdriving
  /// (raw power exceeded the calibrated full-scale reference). Drives the
  /// CLIP pill in the signal meter UI; independent of the clamped
  /// <see cref="SignalStrength"/> percentage.
  /// </summary>
  public bool Clip { get; set; }

  /// <summary>
  /// Gets or sets the received-signal level in dBu, mapped linearly from the
  /// raw front-end reading into the range [-60, 0]. Surfaced as a separate
  /// field so the UI can render a calibrated readout alongside the clamped
  /// percent bar.
  /// </summary>
  public double RssiDbu { get; set; }

  /// <summary>
  /// Gets or sets the gain (in dB) currently applied to the front-end.
  /// When AGC is on this is the device-chosen value; when AGC is off this
  /// mirrors the user-set <see cref="Gain"/>. Either way the UI can bind one
  /// field to display the "live" gain in either mode.
  /// </summary>
  public double AppliedGain { get; set; }

  /// <summary>
  /// Gets or sets the current equalizer mode.
  /// </summary>
  public string? Equalizer { get; set; }

  /// <summary>
  /// Gets or sets the device-specific volume (0-100).
  /// </summary>
  public int? DeviceVolume { get; set; }

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
  public bool AutoGain { get; set; }

  /// <summary>
  /// Gets or sets the manual gain value in dB (only effective when AutoGain is false).
  /// </summary>
  public int? Gain { get; set; }

  /// <summary>
  /// Gets or sets whether the radio is receiving a stereo FM signal
  /// (19 kHz pilot tone detected).
  /// </summary>
  public bool IsStereo { get; set; }

  /// <summary>
  /// Gets or sets the RDS Program Service station name (up to 8 characters),
  /// or null if RDS is not available. This is the <em>live</em> value — for
  /// rolling-PS stations it cycles every few seconds. Use
  /// <see cref="RdsStationNameStable"/> for save-seed scenarios where a
  /// transient rolling frame would be the wrong value to capture.
  /// </summary>
  public string? RdsStationName { get; set; }

  /// <summary>
  /// Gets or sets the last <em>stable</em> RDS Program Service station name —
  /// the consensus value after several consecutive identical PS frames.
  /// Used by the save-preset dialog and history records so rolling-PS
  /// stations (e.g. "Rock 92" cycling 8 chars at a time) don't end up
  /// seeded with mid-roll fragments like "anoidRoc". PR D #40.
  /// </summary>
  public string? RdsStationNameStable { get; set; }

  /// <summary>
  /// Gets or sets the RDS Program Type name (e.g., "Rock", "News"),
  /// or null if not available.
  /// </summary>
  public string? RdsProgramType { get; set; }

  /// <summary>
  /// Gets or sets the RDS RadioText (RT) string broadcast by the station,
  /// or null if not available. Often contains an artist/title pair or a
  /// station slogan. Rendered as a one-line ellipsised string below the
  /// frequency display. PR 3 of the Radio Controller Polish arc.
  /// </summary>
  public string? RdsRadioText { get; set; }

  /// <summary>
  /// Gets or sets the RDS PI (Program Identification) code — the 16-bit
  /// station identifier broadcast on every RDS frame. Null until first RDS
  /// lock or for non-RDS sources. Task #80 v4 — exposed for diagnostics and
  /// so the call-sign decode that drives <see cref="RdsStationNameStable"/>
  /// is debuggable from the wire without log scraping. Surfaced as a
  /// nullable ushort so JSON consumers see a number, not a hex string.
  /// </summary>
  public ushort? RdsPi { get; set; }

  /// <summary>
  /// Gets or sets the dBu threshold at which scan pauses on a signal.
  /// Re-typed from <c>int</c> (percent) to <c>double</c> (dBu) in PR 1 of the
  /// Radio Controller Polish arc so the threshold matches the dBu semantics
  /// of <see cref="RssiDbu"/>. Greenfield project — no back-compat shim.
  /// </summary>
  public double ScanStopThreshold { get; set; }

  /// <summary>
  /// Gets or sets the <see cref="FingerprintEventDto.MatchId"/> of the
  /// fingerprint event that is currently playing, when one has been
  /// identified. The recognition stream in <c>NowPlayingPanel</c> anchors the
  /// NOW header + amber-left-border on the row whose <c>MatchId</c> equals
  /// this value. Null when no match is currently anchored (e.g. dead air,
  /// no-match window, non-radio source). PR 2 of the Radio Controller
  /// Polish arc.
  /// </summary>
  public string? NowPlayingMatchId { get; set; }

  /// <summary>
  /// Per-broadcast discriminator (NOT persisted device state). True when this
  /// broadcast carries a change to an RDS- or tuning-relevant field
  /// (frequency, band, step, any RDS PS/PTY/RT/PI/stable field, or
  /// NowPlayingMatchId); false when only volatile signal telemetry
  /// (RSSI/signal-strength/gain/stereo/scan) changed. The Web RDS marquee
  /// path reads this to avoid re-running the RDS accumulator + restarting the
  /// CSS ticker ~twice a second on pure-telemetry ticks. Telemetry consumers
  /// (signal meter, gain readout, recognition NOW-row) ignore it and read the
  /// full DTO every broadcast. Defaults true so any non-broadcast construction
  /// (REST /api/radio/state) is treated as a full refresh.
  /// </summary>
  public bool RdsRelevantChanged { get; set; } = true;
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
