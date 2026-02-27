namespace Radio.Core.Configuration;

/// <summary>
/// Configuration for the audio diagnostic validation pipeline.
/// When enabled, taps at multiple pipeline stages analyze audio for
/// silence, wrong frequencies, channel leakage, and level anomalies.
/// </summary>
public class AudioValidationOptions
{
  /// <summary>
  /// Configuration section name in appsettings.json.
  /// </summary>
  public const string SectionName = "Diagnostics:AudioValidation";

  /// <summary>
  /// Whether audio validation is enabled. Default false (zero overhead via NullAudioValidator).
  /// </summary>
  public bool Enabled { get; set; }

  /// <summary>
  /// FFT/analysis buffer size in samples. Default 4096.
  /// </summary>
  public int FftSize { get; set; } = 4096;

  /// <summary>
  /// Log an OK summary every N batches. Default 10.
  /// </summary>
  public int LogIntervalBatches { get; set; } = 10;

  /// <summary>
  /// RMS level (dBFS) below which audio is considered silent. Default -50.
  /// </summary>
  public float SilenceThresholdDb { get; set; } = -50f;

  /// <summary>
  /// Goertzel magnitude threshold (normalized) for frequency detection. Default 0.1.
  /// </summary>
  public float FrequencyDetectionThreshold { get; set; } = 0.1f;
}
