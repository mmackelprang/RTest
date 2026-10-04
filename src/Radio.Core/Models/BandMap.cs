namespace Radio.Core.Models;

/// <summary>One measured channel in a <see cref="BandMap"/>.</summary>
/// <param name="FrequencyHz">Channel centre, in Hz.</param>
/// <param name="LevelDbfs">Relative in-channel level, in dB. Comparable only with other levels measured the same way.</param>
public sealed record BandMapChannel(long FrequencyHz, float LevelDbfs);

/// <summary>
/// A station a live seek stopped on (AUD-100): proof that something was there, at that time. Kept
/// apart from a <see cref="BandMap"/>'s swept channels, never mixed into them.
/// </summary>
/// <param name="FrequencyHz">Where the seek stopped, in Hz.</param>
/// <param name="SeekStrength">
/// The seek's own signal reading when it stopped: the RMS magnitude of the receiver's wideband
/// capture, 0 to about 1.2. Not a dB level and not comparable with <see cref="BandMapChannel.LevelDbfs"/>.
/// </param>
/// <param name="ObservedAtUtc">When the seek stopped there, UTC.</param>
public sealed record BandMapSeekStation(long FrequencyHz, float SeekStrength, DateTimeOffset ObservedAtUtc);

/// <summary>The seek-observed stations of one band, as stored (AUD-100).</summary>
public sealed record BandMapSeekStations
{
  /// <summary>Band code, e.g. <c>"FM"</c>.</summary>
  public string Band { get; init; } = "FM";

  /// <summary>Stations in ascending frequency order, at most one per frequency.</summary>
  public IReadOnlyList<BandMapSeekStation> Stations { get; init; } = Array.Empty<BandMapSeekStation>();
}

/// <summary>
/// Which swept channels of a <see cref="BandMap"/> are stations: the peak rule the BAND view's tap
/// has used since AUD-76, shared since AUD-100 so Scan stops where a tap would snap.
/// </summary>
public static class BandMapStations
{
  /// <summary>
  /// How far above the map's median level (its noise estimate) a channel must stand to be a peak,
  /// in dB. The levels are relative, not calibrated, so only differences within one map mean
  /// anything; 6 dB is a factor of four in power, well clear of channel-to-channel noise.
  /// </summary>
  public const double PeakProminenceDb = 6.0;

  /// <summary>
  /// The frequencies of <paramref name="channels"/>' peaks, ascending. A channel is a peak when its
  /// level is at least that of both neighbours in frequency order (a missing neighbour at the band
  /// edge counts as lower) and at least <see cref="PeakProminenceDb"/> above the median level of all
  /// of <paramref name="channels"/>. The neighbour rule drops a strong station's adjacent-channel
  /// shadow, which is lower than the station.
  /// </summary>
  public static IReadOnlyList<long> Peaks(IReadOnlyList<BandMapChannel> channels)
  {
    ArgumentNullException.ThrowIfNull(channels);
    if (channels.Count == 0)
    {
      return Array.Empty<long>();
    }

    BandMapChannel[] sorted = channels.OrderBy(c => c.FrequencyHz).ToArray();
    double threshold = Median(sorted.Select(c => (double)c.LevelDbfs)) + PeakProminenceDb;
    List<long> peaks = new();
    for (int i = 0; i < sorted.Length; i++)
    {
      double level = sorted[i].LevelDbfs;
      double left = i > 0 ? sorted[i - 1].LevelDbfs : double.NegativeInfinity;
      double right = i < sorted.Length - 1 ? sorted[i + 1].LevelDbfs : double.NegativeInfinity;
      if (level >= left && level >= right && level >= threshold)
      {
        peaks.Add(sorted[i].FrequencyHz);
      }
    }

    return peaks;
  }

  /// <summary>Median of <paramref name="values"/> (mean of the middle two for an even count); 0 when empty.</summary>
  public static double Median(IEnumerable<double> values)
  {
    double[] sorted = values.OrderBy(v => v).ToArray();
    if (sorted.Length == 0)
    {
      return 0;
    }

    int mid = sorted.Length / 2;
    return sorted.Length % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2.0;
  }
}

/// <summary>
/// A stored per-channel signal map of one radio band, produced by a band sweep (AUD-76; per band
/// since AUD-91).
/// </summary>
public sealed record BandMap
{
  /// <summary>Band code, e.g. <c>"FM"</c>, <c>"WB"</c>, <c>"AIR"</c>, <c>"VHF"</c>.</summary>
  public string Band { get; init; } = "FM";

  /// <summary>
  /// Lower edge of the frequency range the sweep covered, as drawn on the band's axis, in Hz.
  /// 0 when not recorded (a map written before AUD-91).
  /// </summary>
  public long RangeMinHz { get; init; }

  /// <summary>
  /// Upper edge of the frequency range the sweep covered, as drawn on the band's axis, in Hz.
  /// 0 when not recorded (a map written before AUD-91).
  /// </summary>
  public long RangeMaxHz { get; init; }

  /// <summary>Spacing of the swept channel grid, in Hz. 0 when not recorded (a map written before AUD-91).</summary>
  public long ChannelSpacingHz { get; init; }

  /// <summary>When the sweep that produced this map finished, UTC.</summary>
  public DateTimeOffset ScannedAtUtc { get; init; }

  /// <summary>Measured channels in ascending frequency order. Channels that could not be measured are absent.</summary>
  public IReadOnlyList<BandMapChannel> Channels { get; init; } = Array.Empty<BandMapChannel>();

  /// <summary>What started the sweep: see <see cref="BandSweepTriggers"/>.</summary>
  public string Trigger { get; init; } = string.Empty;

  /// <summary>Which device path measured it: see <see cref="BandSweepPaths"/>.</summary>
  public string Path { get; init; } = string.Empty;
}

/// <summary>Values of <see cref="BandMap.Trigger"/> and <see cref="BandSweepOutcome.Trigger"/>.</summary>
public static class BandSweepTriggers
{
  /// <summary>The periodic timer, with the dongle idle.</summary>
  public const string Timer = "timer";

  /// <summary>The periodic timer, while the console was asleep with the radio source holding the dongle.</summary>
  public const string Sleep = "sleep";

  /// <summary>An explicit request (the owner's Scan tap).</summary>
  public const string Request = "request";
}

/// <summary>Values of <see cref="BandMap.Path"/> and <see cref="BandSweepOutcome.Path"/>.</summary>
public static class BandSweepPaths
{
  /// <summary>The sweep opened the idle dongle itself.</summary>
  public const string Idle = "idle";

  /// <summary>The sweep used the running radio receiver's stream.</summary>
  public const string Live = "live";
}

/// <summary>Values of <see cref="BandSweepOutcome.Result"/>.</summary>
public static class BandSweepResults
{
  /// <summary>The sweep finished and its map was stored.</summary>
  public const string Completed = "completed";

  /// <summary>The sweep was cancelled; the previous map was kept.</summary>
  public const string Cancelled = "cancelled";

  /// <summary>The sweep failed; the previous map was kept.</summary>
  public const string Failed = "failed";

  /// <summary>No sweep ran.</summary>
  public const string Skipped = "skipped";
}

/// <summary>The result of the most recent sweep attempt.</summary>
public sealed record BandSweepOutcome
{
  /// <summary>Band code the attempt was for, or null when not recorded.</summary>
  public string? Band { get; init; }

  /// <summary>What started (or would have started) the sweep.</summary>
  public string Trigger { get; init; } = string.Empty;

  /// <summary>Device path used, or null when no sweep ran.</summary>
  public string? Path { get; init; }

  /// <summary>When the attempt started, UTC.</summary>
  public DateTimeOffset StartedAtUtc { get; init; }

  /// <summary>Wall time of the attempt, in milliseconds.</summary>
  public long DurationMs { get; init; }

  /// <summary>One of <see cref="BandSweepResults"/>.</summary>
  public string Result { get; init; } = string.Empty;

  /// <summary>Why the attempt was cancelled, failed or skipped; null on completion.</summary>
  public string? Reason { get; init; }

  /// <summary>Channels measured, for a sweep that ran.</summary>
  public int ChannelsMeasured { get; init; }
}

/// <summary>Sweep state as reported to clients.</summary>
public sealed record BandSweepStatus
{
  /// <summary>True while a sweep is running.</summary>
  public bool IsSweeping { get; init; }

  /// <summary>Band code of the running sweep, or null when idle.</summary>
  public string? Band { get; init; }

  /// <summary>Trigger of the running sweep, or null.</summary>
  public string? Trigger { get; init; }

  /// <summary>Path of the running sweep, or null.</summary>
  public string? Path { get; init; }

  /// <summary>Fraction of channels processed by the running sweep, 0 to 1; 0 when idle.</summary>
  public double Progress { get; init; }

  /// <summary>When the running sweep started, UTC, or null.</summary>
  public DateTimeOffset? StartedAtUtc { get; init; }

  /// <summary>
  /// Remaining time extrapolated from the running sweep's progress so far, or null when
  /// idle or before the first channel has been processed.
  /// </summary>
  public double? EstimatedSecondsRemaining { get; init; }

  /// <summary>The most recent finished or skipped attempt, or null when there has been none.</summary>
  public BandSweepOutcome? Last { get; init; }
}
