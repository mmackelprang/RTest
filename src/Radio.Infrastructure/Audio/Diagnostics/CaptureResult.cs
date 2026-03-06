namespace Radio.Infrastructure.Audio.Diagnostics;

/// <summary>
/// Result of a diagnostic capture session.
/// </summary>
public class CaptureResult
{
  /// <summary>When the capture started.</summary>
  public DateTime StartTime { get; init; }

  /// <summary>Actual capture duration.</summary>
  public TimeSpan Duration { get; init; }

  /// <summary>Output directory containing WAV files.</summary>
  public string OutputDirectory { get; init; } = "";

  /// <summary>
  /// Map of stage name to WAV file path.
  /// Keys: "generator-input", "generator-output", "pre-modifiers", "post-modifiers".
  /// </summary>
  public Dictionary<string, string> StageFiles { get; init; } = new();

  /// <summary>Total samples captured per stage.</summary>
  public Dictionary<string, int> StageSampleCounts { get; init; } = new();

  /// <summary>Whether the capture completed successfully.</summary>
  public bool Success { get; init; }

  /// <summary>Error message if capture failed.</summary>
  public string? ErrorMessage { get; init; }
}
