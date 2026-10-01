namespace Radio.Core.Configuration;

/// <summary>
/// Options for the band map sweeps (AUD-76; every mappable band since AUD-91), bound from the
/// <c>BandMap</c> section.
/// </summary>
public sealed class BandMapOptions
{
  /// <summary>Configuration section name.</summary>
  public const string SectionName = "BandMap";

  /// <summary>When false, neither the timer nor explicit requests sweep. Default true.</summary>
  public bool Enabled { get; set; } = true;

  /// <summary>Delay from service start to the first timer evaluation, in seconds. Default 120.</summary>
  public int InitialDelaySeconds { get; set; } = 120;

  /// <summary>
  /// Interval between timer evaluations, in minutes; a map of the radio's current band older than
  /// this is due for a rescan.
  /// Default 60.
  /// </summary>
  public int RescanIntervalMinutes { get; set; } = 60;

  /// <summary>Fixed manual tuner gain used by every sweep, in dB. Default 28.0.</summary>
  public float SweepGainDb { get; set; } = 28.0f;

  /// <summary>
  /// IQ samples per settling read and per measurement read. Default 16384. Must be at least
  /// 2048 (one FFT frame) and a multiple of 256; an invalid value is replaced by the default,
  /// with a warning.
  /// </summary>
  public int SamplesPerMeasurement { get; set; } = 16384;
}
