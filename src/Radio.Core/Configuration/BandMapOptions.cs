namespace Radio.Core.Configuration;

/// <summary>
/// Options for the FM band map sweep (AUD-76), bound from the <c>BandMap</c> section.
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
  /// Interval between timer evaluations, in minutes; a map older than this is due for a rescan.
  /// Default 60.
  /// </summary>
  public int RescanIntervalMinutes { get; set; } = 60;

  /// <summary>Fixed manual tuner gain used by every sweep, in dB. Default 28.0.</summary>
  public float SweepGainDb { get; set; } = 28.0f;

  /// <summary>IQ samples per settling read and per measurement read. Default 16384.</summary>
  public int SamplesPerMeasurement { get; set; } = 16384;
}
