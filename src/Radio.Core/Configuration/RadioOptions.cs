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
  /// Gets or sets the signal strength threshold for scan stop (0-100).
  /// When scanning, the scan will stop when signal strength exceeds this threshold.
  /// </summary>
  /// <remarks>
  /// The signal strength has two scales (AUD-101). On FM, Weather, VHF and Aircraft it is linear
  /// wideband RMS, so 50 means half of full scale. On AM and Shortwave it is the in-channel level
  /// in dB, <c>−60 + 0.6 × threshold</c> dBFS, so 50 means −30 dBFS in the channel.
  /// </remarks>
  public int ScanStopThreshold { get; set; } = 50;

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
  /// suits that antenna and those stations. Signal readings scale with it, so a change here moves
  /// every station against <see cref="ScanStopThreshold"/>.
  /// </remarks>
  public float TunerGainDb { get; set; } = 30f;
}
