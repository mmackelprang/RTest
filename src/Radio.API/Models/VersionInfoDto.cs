namespace Radio.API.Models;

/// <summary>
/// Identifies the exact build of the running API binary, used by deploy
/// verification to confirm the deployed code matches the expected commit.
/// </summary>
public class VersionInfoDto
{
  /// <summary>
  /// Full git commit SHA the API was built from, or "unknown" if not stamped.
  /// </summary>
  public string GitSha { get; set; } = string.Empty;

  /// <summary>
  /// Short (7-char) form of <see cref="GitSha"/> for convenience.
  /// </summary>
  public string GitShaShort { get; set; } = string.Empty;

  /// <summary>
  /// AssemblyInformationalVersion (e.g. "1.0.0+abc123...").
  /// </summary>
  public string InformationalVersion { get; set; } = string.Empty;

  /// <summary>
  /// AssemblyVersion (e.g. "1.0.0.0").
  /// </summary>
  public string AssemblyVersion { get; set; } = string.Empty;

  /// <summary>
  /// UTC timestamp of when the assembly file was last written (build time).
  /// </summary>
  public DateTime BuildTimestampUtc { get; set; }

  /// <summary>
  /// Assembly simple name (e.g. "Radio.API").
  /// </summary>
  public string AssemblyName { get; set; } = string.Empty;
}
