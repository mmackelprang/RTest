namespace Radio.AudioAnalysis;

/// <summary>
/// Threshold configuration for waveform comparison.
/// </summary>
public class ComparisonOptions
{
  /// <summary>
  /// Maximum allowable RMS error between reference and captured signals.
  /// Default: 0.01 (-40 dB).
  /// </summary>
  public float MaxRmsError { get; init; } = 0.01f;

  /// <summary>
  /// Maximum allowable peak sample difference.
  /// Default: 0.05.
  /// </summary>
  public float MaxPeakError { get; init; } = 0.05f;

  /// <summary>
  /// Maximum allowable gain deviation ratio (e.g., 0.05 = 5% gain error).
  /// Default: 0.05.
  /// </summary>
  public float MaxGainDeviation { get; init; } = 0.05f;

  /// <summary>
  /// Minimum consecutive zero samples to flag as silence insertion.
  /// Default: 8 (stereo frames).
  /// </summary>
  public int MinSilenceRunLength { get; init; } = 8;

  /// <summary>
  /// Minimum consecutive repeated samples to flag as repeated sample event.
  /// Default: 8.
  /// </summary>
  public int MinRepeatedRunLength { get; init; } = 8;

  /// <summary>
  /// Clipping threshold — samples at or above this absolute value are flagged.
  /// Default: 0.999.
  /// </summary>
  public float ClippingThreshold { get; init; } = 0.999f;

  /// <summary>
  /// Minimum consecutive clipped samples to flag as clipping event.
  /// Default: 4.
  /// </summary>
  public int MinClippingRunLength { get; init; } = 4;

  /// <summary>
  /// Maximum THD (Total Harmonic Distortion) percentage considered clean.
  /// Default: 1.0 (1%).
  /// </summary>
  public float MaxThdPercent { get; init; } = 1.0f;

  /// <summary>
  /// Number of audio channels (for frame alignment).
  /// Default: 2 (stereo).
  /// </summary>
  public int Channels { get; init; } = 2;
}
