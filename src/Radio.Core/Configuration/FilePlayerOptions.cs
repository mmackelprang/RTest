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

  /// <summary>
  /// Gets or sets bookmarked directories that appear as quick-access entries in
  /// the file browser. These paths are also implicitly allowed for browsing.
  /// </summary>
  public BookmarkedPath[] BookmarkedPaths { get; set; } = [];
}

/// <summary>
/// A bookmarked directory for quick access in the file browser.
/// </summary>
public class BookmarkedPath
{
  /// <summary>
  /// The absolute filesystem path to the directory.
  /// </summary>
  public string Path { get; set; } = "";

  /// <summary>
  /// A user-friendly label for the bookmark (e.g., "NAS Music", "Alert Sounds").
  /// </summary>
  public string Label { get; set; } = "";

  /// <summary>
  /// A tag indicating the bookmark's purpose. Used to select context-appropriate
  /// defaults (e.g., "music" for queue browsing, "sounds" for event file selection).
  /// </summary>
  public string Tag { get; set; } = "";
}
