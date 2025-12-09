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
  /// Supported values: "RTLSDRCore", "RF320"
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
  /// Gets or sets the default AM frequency step in kHz.
  /// Typical values: 9 or 10 kHz
  /// </summary>
  public double DefaultAMStepKHz { get; set; } = 10.0;

  /// <summary>
  /// Gets or sets the minimum FM frequency in MHz.
  /// </summary>
  public double MinFMFrequencyMHz { get; set; } = 87.5;

  /// <summary>
  /// Gets or sets the maximum FM frequency in MHz.
  /// </summary>
  public double MaxFMFrequencyMHz { get; set; } = 108.0;

  /// <summary>
  /// Gets or sets the minimum AM frequency in kHz.
  /// </summary>
  public double MinAMFrequencyKHz { get; set; } = 520.0;

  /// <summary>
  /// Gets or sets the maximum AM frequency in kHz.
  /// </summary>
  public double MaxAMFrequencyKHz { get; set; } = 1710.0;

  /// <summary>
  /// Gets or sets the signal strength threshold for scan stop (0-100).
  /// When scanning, the scan will stop when signal strength exceeds this threshold.
  /// </summary>
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
}
