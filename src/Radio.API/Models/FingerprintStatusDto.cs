namespace Radio.API.Models;

/// <summary>
/// Coarse confidence band for a fingerprint match. PR 2 of the Radio Controller
/// Polish arc replaces the raw <c>double?</c> confidence on the API surface so the
/// UI renders a word + pip count instead of a percentage that varies misleadingly
/// over a narrow dynamic range. Greenfield rename — no back-compat shim.
/// </summary>
/// <remarks>
/// Server-side threshold mapping (folded at the API projection boundary):
/// <list type="bullet">
///   <item><description><c>Strong</c>   — raw score ≥ 0.90</description></item>
///   <item><description><c>Likely</c>   — 0.80 ≤ raw score &lt; 0.90</description></item>
///   <item><description><c>Possible</c> — 0.60 ≤ raw score &lt; 0.80</description></item>
///   <item><description><c>None</c>     — no match returned OR raw score &lt; 0.60</description></item>
/// </list>
/// The raw score remains in <c>FingerprintEventRecord.LastConfidence</c> for
/// server-side diagnostic logging; it is intentionally NOT exposed on the DTO.
/// </remarks>
public enum ConfidenceBucket
{
  None,
  Possible,
  Likely,
  Strong
}

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
  /// <summary>
  /// Stable identifier for this event record, propagated from
  /// <see cref="Radio.Core.Models.Audio.FingerprintEventRecord.MatchId"/>.
  /// Used by the UI to anchor the currently-playing match row when
  /// <c>RadioStateDto.NowPlayingMatchId</c> matches.
  /// </summary>
  public string MatchId { get; set; } = string.Empty;

  public string AudioSource { get; set; } = string.Empty;
  public string SourceType { get; set; } = string.Empty;
  public bool IsMatch { get; set; }
  public int Count { get; set; }

  /// <summary>
  /// Coarse confidence band for this match. Replaces the prior raw
  /// <c>double? LastConfidence</c> field on the API surface (PR 2 of the
  /// Radio Controller Polish arc). The raw score lives on the server-side
  /// record only; the DTO carries only the bucket.
  /// </summary>
  public ConfidenceBucket Confidence { get; set; } = ConfidenceBucket.None;

  public string? Title { get; set; }
  public string? Artist { get; set; }
  public string? Album { get; set; }
  public bool HasAlbumArt { get; set; }
  public string Phase { get; set; } = "Idle";
  public DateTime Timestamp { get; set; }
}
