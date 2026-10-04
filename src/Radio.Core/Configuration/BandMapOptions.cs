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

  /// <summary>
  /// Oldest a band's swept map may be, in minutes, for Scan Up/Down to hop between its stations
  /// instead of seeking live (AUD-100). Default 1440 (one day). 0 or less: Scan always seeks live.
  /// </summary>
  /// <remarks>
  /// A day, because the timer re-sweeps the radio's band every <see cref="RescanIntervalMinutes"/>
  /// whenever the dongle is idle or the console is asleep, so on a box in normal use the map is
  /// rarely more than a few hours old, and broadcast transmitters do not move within a day. What a
  /// stale map costs is a station that came on air since the sweep being hopped over, and a
  /// station gone off air costing one scan pause. A map older than a day means the sweeps have
  /// stopped happening, and live seek is the safer answer.
  /// </remarks>
  public int ScanMapMaxAgeMinutes { get; set; } = 1440;
}
