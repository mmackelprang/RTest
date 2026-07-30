namespace Radio.Core.Configuration;

/// <summary>
/// Configuration options for diagnostic audio capture output retention.
/// DiagnosticCaptureService writes multi-stage WAV captures under a base directory
/// but nothing pruned them — the live box accumulated ~1.1 GB of stale captures.
/// These options cap the number of retained capture runs and their maximum age so
/// the diagnostics directory stays bounded.
/// </summary>
public sealed class DiagnosticsOptions
{
  /// <summary>Configuration section name for binding.</summary>
  public const string SectionName = "Diagnostics";

  /// <summary>
  /// Base directory under which each capture run writes a timestamped subdirectory
  /// (yyyyMMdd-HHmmss). Relative paths resolve against the process working directory.
  /// Default: data/diagnostics.
  /// </summary>
  public string CaptureBaseDirectory { get; set; } = Path.Combine("data", "diagnostics");

  /// <summary>
  /// Enable or disable automatic pruning of old capture runs after each capture.
  /// Default: true.
  /// </summary>
  public bool RetentionEnabled { get; set; } = true;

  /// <summary>
  /// Maximum number of capture-run directories to retain. The newest runs are kept;
  /// older ones beyond this count are deleted. Set to 0 to disable the count cap
  /// (age-based pruning still applies). Default: 20.
  /// </summary>
  public int MaxRetainedRuns { get; set; } = 20;

  /// <summary>
  /// Age, in days, beyond which capture-run directories are deleted regardless of
  /// count. Set to 0 to disable age-based pruning (the count cap still applies).
  /// Default: 7 days.
  /// </summary>
  public int RetentionDays { get; set; } = 7;
}
