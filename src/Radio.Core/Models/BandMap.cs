namespace Radio.Core.Models;

/// <summary>One measured channel in a <see cref="BandMap"/>.</summary>
/// <param name="FrequencyHz">Channel centre, in Hz.</param>
/// <param name="LevelDbfs">Relative in-channel level, in dB. Comparable only with other levels measured the same way.</param>
public sealed record BandMapChannel(long FrequencyHz, float LevelDbfs);

/// <summary>
/// A stored per-channel signal map of one radio band, produced by a band sweep (AUD-76).
/// </summary>
public sealed record BandMap
{
  /// <summary>Band name, e.g. <c>"FM"</c>.</summary>
  public string Band { get; init; } = "FM";

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
