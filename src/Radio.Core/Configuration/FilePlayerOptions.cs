namespace Radio.Core.Configuration;

/// <summary>
/// Configuration options for the file player audio source.
/// Loaded from the 'FilePlayer' configuration section.
/// </summary>
public class FilePlayerOptions
{
  /// <summary>
  /// The configuration section name.
  /// </summary>
  public const string SectionName = "FilePlayer";

  /// <summary>
  /// Gets or sets the root directory for audio files (relative to RootDir).
  /// </summary>
  public string RootDirectory { get; set; } = "media/audio";

  /// <summary>
  /// Gets or sets the supported audio file extensions.
  /// </summary>
  public string[] SupportedExtensions { get; set; } = [".mp3", ".flac", ".wav", ".ogg", ".aac", ".m4a", ".wma"];

  /// <summary>
  /// Gets or sets additional directories that may be browsed via absolute path
  /// (e.g., NAS mounts, USB drives). Paths outside RootDirectory and these
  /// directories will be rejected. Empty array means only RootDirectory is allowed.
  /// </summary>
  public string[] AllowedBrowseDirectories { get; set; } = [];
}
