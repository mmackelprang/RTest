namespace Radio.API.Models;

/// <summary>
/// DTO for the current fingerprint identification status.
/// </summary>
public class FingerprintStatusDto
{
  public string Phase { get; set; } = "Idle";
  public bool IsEnabled { get; set; }
  public double FingerprintsPerMinute { get; set; }
  public double MetadataCallsPerMinute { get; set; }
  public List<FingerprintEventDto> RecentEvents { get; set; } = [];
  public string? LastError { get; set; }
}

/// <summary>
/// DTO for a single fingerprint identification event.
/// </summary>
public class FingerprintEventDto
{
  public string AudioSource { get; set; } = string.Empty;
  public DateTime? FirstMatchAt { get; set; }
  public int NoMatchCount { get; set; }
  public int MatchCount { get; set; }
  public double? LastConfidence { get; set; }
  public string? Title { get; set; }
  public string? Artist { get; set; }
  public string? Album { get; set; }
  public string Phase { get; set; } = "Idle";
  public DateTime Timestamp { get; set; }
}
