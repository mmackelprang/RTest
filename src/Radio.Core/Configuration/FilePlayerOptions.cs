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

  /// <summary>
  /// Gets or sets the most tracks one "Add folder" queues (UI-32). The folder walk stops at the first track past
  /// it and the first <c>MaxFolderTracks</c>, in queue order, are offered. Default 500: measured 2026-10-02 on the
  /// box, an artist folder on the NAS holds 12–68 tracks and the whole music library 12,649, so the cap admits
  /// any artist or album and stops a tap on the library root from queueing the lot.
  /// </summary>
  public int MaxFolderTracks { get; set; } = 500;

  /// <summary>
  /// Gets or sets how long, in seconds, the "Add folder" walk may take before the request gives up (UI-32).
  /// Default 20. A cold walk of the whole NAS library measured ~15 s, and the cap normally ends a walk far sooner.
  /// </summary>
  public int FolderScanTimeoutSeconds { get; set; } = 20;
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
