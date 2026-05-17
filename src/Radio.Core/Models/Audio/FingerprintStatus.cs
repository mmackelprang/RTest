namespace Radio.Core.Models.Audio;

/// <summary>
/// Phase of a fingerprint identification cycle.
/// </summary>
public enum FingerprintPhase
{
  Idle,
  Capturing,
  Fingerprinting,
  Querying,
  Matched,
  NoMatch,
  Error
}

/// <summary>
/// A single fingerprint identification event record.
/// Represents one audio segment being tracked through the identification pipeline.
/// </summary>
public record FingerprintEventRecord
{
  /// <summary>
  /// Stable identifier for this event record, generated once when the record is created.
  /// Persists across aggregations (Count++ on repeated matches of the same track) so the
  /// UI has an unambiguous anchor to mark a row as "the now-playing match" via
  /// <c>RadioStateDto.NowPlayingMatchId</c>. PR 2 of the Radio Controller Polish arc.
  /// </summary>
  public string MatchId { get; init; } = Guid.NewGuid().ToString("n");

  public string AudioSource { get; init; } = string.Empty;
  public string SourceType { get; set; } = string.Empty;
  public bool IsMatch { get; set; }
  public int Count { get; set; }

  /// <summary>
  /// Raw AcoustID/Shazam confidence score in the range [0, 1]. Retained on the
  /// server-side record for diagnostic logging; folded into a coarse
  /// <c>ConfidenceBucket</c> at the API boundary so the UI never surfaces a
  /// raw percentage.
  /// </summary>
  public double? LastConfidence { get; set; }
  public string? Title { get; set; }
  public string? Artist { get; set; }
  public string? Album { get; set; }
  public bool HasAlbumArt { get; set; }
  public FingerprintPhase Phase { get; set; } = FingerprintPhase.Idle;
  public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Snapshot of the current fingerprint identification state, including recent events and throughput rates.
/// </summary>
public record FingerprintStatusSnapshot
{
  public FingerprintPhase Phase { get; init; }
  public bool IsEnabled { get; init; }
  public double FingerprintsPerMinute { get; init; }
  public double MetadataCallsPerMinute { get; init; }
  public IReadOnlyList<FingerprintEventRecord> RecentEvents { get; init; } = [];
  public string? LastError { get; init; }
}
