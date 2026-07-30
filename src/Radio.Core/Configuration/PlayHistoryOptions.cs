namespace Radio.Core.Configuration;

/// <summary>
/// Configuration options for play-history storage growth control.
/// The PlayHistory table only ever grows (one row per track played, ~32k rows and
/// counting on the live box). These options drive a scheduled retention prune that
/// deletes entries older than a configurable window so the SQLite file — and the
/// transient memory of any wide query over it — stays bounded.
/// </summary>
public sealed class PlayHistoryOptions
{
  /// <summary>Configuration section name for binding.</summary>
  public const string SectionName = "PlayHistory";

  /// <summary>
  /// Enable or disable the scheduled age-based retention prune.
  /// Default: true.
  /// </summary>
  public bool RetentionEnabled { get; set; } = true;

  /// <summary>
  /// Age, in days, beyond which play-history entries are pruned. Entries whose
  /// PlayedAt is older than (now - RetentionDays) are deleted on each prune pass.
  /// Default: 180 days (~6 months).
  /// </summary>
  public int RetentionDays { get; set; } = 180;

  /// <summary>
  /// Interval, in hours, between retention prune passes.
  /// Default: 24 hours (once per day).
  /// </summary>
  public int PruneIntervalHours { get; set; } = 24;
}
