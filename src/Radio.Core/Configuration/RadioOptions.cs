namespace Radio.Core.Configuration;

/// <summary>
/// Configuration options for radio functionality.
/// Loaded from the 'Radio' configuration section.
/// </summary>
public class RadioOptions
{
  /// <summary>
  /// The configuration section name.
  /// </summary>
  public const string SectionName = "Radio";

  /// <summary>
  /// Gets or sets the default radio device type.
  /// The only supported value is "RTLSDRCore". Any other value is unavailable, so
  /// <c>RadioFactory.GetDefaultDeviceType</c> falls back to the first available device.
  /// </summary>
  public string DefaultDevice { get; set; } = "RTLSDRCore";

  /// <summary>
  /// Gets or sets the default FM frequency in MHz.
  /// </summary>
  public double DefaultFMFrequencyMHz { get; set; } = 101.5;

  /// <summary>
  /// Gets or sets the default AM frequency in kHz.
  /// </summary>
  public double DefaultAMFrequencyKHz { get; set; } = 1000.0;

  /// <summary>
  /// Gets or sets the default FM frequency step in MHz.
  /// Typical values: 0.1 (100 kHz) or 0.2 (200 kHz)
  /// </summary>
  public double DefaultFMStepMHz { get; set; } = 0.1;

  /// <summary>
  /// Gets or sets the minimum FM frequency in MHz.
  /// </summary>
  public double MinFMFrequencyMHz { get; set; } = 87.5;

  /// <summary>
  /// Gets or sets the maximum FM frequency in MHz.
  /// </summary>
  public double MaxFMFrequencyMHz { get; set; } = 108.0;

  /// <summary>
  /// Gets or sets the signal strength (0-100) at which a live seek pauses on FM, Weather, VHF and
  /// Aircraft, the bands that use the tuner.
  /// </summary>
  /// <remarks>
  /// On these bands the signal strength is linear wideband RMS, so 14 means 14% of full scale. It
  /// follows the tuner's gain: 14 is set for the default <see cref="TunerGainDb"/>, where a station
  /// can read well under the 50 this used to be. Automatic gain or a higher manual gain lifts the
  /// noise too, and an empty channel can then read over 14. A fresh band map bypasses this value,
  /// because Scan then hops the map instead of seeking live. AM and Shortwave use
  /// <see cref="HfScanStopThreshold"/>.
  /// </remarks>
  public int ScanStopThreshold { get; set; } = 14;

  /// <summary>
  /// Gets or sets the signal strength (0-100) at which a live seek pauses on AM and Shortwave.
  /// </summary>
  /// <remarks>
  /// On these bands the signal strength is the in-channel level in dB (AUD-101),
  /// <c>−60 + 0.6 × threshold</c> dBFS, so 50 means −30 dBFS. The bands bypass the tuner, so
  /// <see cref="TunerGainDb"/> does not move it. It cannot share <see cref="ScanStopThreshold"/>:
  /// the AM floor measured on the appliance is about −49 to −51.5 dBFS, so 14 (−51.6 dBFS) would
  /// pause on nearly every empty channel.
  /// </remarks>
  public int HfScanStopThreshold { get; set; } = 50;

  /// <summary>
  /// Gets or sets the scan step delay in milliseconds.
  /// Time to wait between frequency steps during scanning.
  /// </summary>
  public int ScanStepDelayMs { get; set; } = 100;

  /// <summary>
  /// Gets or sets the default device volume (0-100).
  /// </summary>
  public int DefaultDeviceVolume { get; set; } = 50;

  /// <summary>
  /// Gets or sets whether the SDR tuner starts on automatic gain.
  /// </summary>
  /// <remarks>
  /// False, because automatic gain overloaded the device's converter on strong FM stations on the
  /// appliance. A switch on the panel changes the gain until the next restart. Applies to every
  /// band that uses the tuner; AM and Shortwave use direct sampling and bypass it.
  /// </remarks>
  public bool AutoGain { get; set; }

  /// <summary>
  /// Gets or sets the SDR tuner's gain in dB, used when <see cref="AutoGain"/> is false.
  /// </summary>
  /// <remarks>
  /// 30 was measured on the appliance: the overload clears on 97.7 MHz, and stereo and RDS hold. It
  /// suits that antenna, and it was only measured on that station. Signal readings on the tuner
  /// bands scale with it, so a change here moves their stations against
  /// <see cref="ScanStopThreshold"/> and <see cref="SquelchThreshold"/>.
  /// </remarks>
  public float TunerGainDb { get; set; } = 30f;

  /// <summary>
  /// Gets or sets the squelch threshold (0-1), on the tuner bands' linear signal strength scale. AM
  /// and Shortwave never apply a squelch. 0 leaves it open.
  /// </summary>
  /// <remarks>
  /// Open, because a closed squelch is exact digital silence with nothing on the panel to say why,
  /// and at a fixed gain weak stations read below the receiver's own 0.1 default.
  /// </remarks>
  public float SquelchThreshold { get; set; }
}
