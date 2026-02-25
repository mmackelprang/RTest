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
  public string AudioSource { get; init; } = string.Empty;
  public DateTime? FirstMatchAt { get; set; }
  public int NoMatchCount { get; set; }
  public int MatchCount { get; set; }
  public double? LastConfidence { get; set; }
  public string? Title { get; set; }
  public string? Artist { get; set; }
  public string? Album { get; set; }
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
