using Radio.Core.Models.Audio;

namespace Radio.Core.Interfaces.Audio;

/// <summary>
/// Service for browsing and listing audio files in the file system.
/// Used by FilePlayerAudioSource to discover and manage audio files.
/// </summary>
public interface IFileBrowser
{
  /// <summary>
  /// Lists audio files in the specified directory.
  /// </summary>
  /// <param name="path">
  /// The path relative to the configured root directory. 
  /// Empty or null returns files from the root directory.
  /// </param>
  /// <param name="recursive">
  /// If true, searches subdirectories recursively. Default is false.
  /// </param>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>A list of audio file information.</returns>
  Task<IReadOnlyList<AudioFileInfo>> ListFilesAsync(
    string? path = null, 
    bool recursive = false, 
    CancellationToken cancellationToken = default);

  /// <summary>
  /// Gets information about a specific audio file.
  /// </summary>
  /// <param name="path">The path to the audio file relative to the root directory.</param>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>Audio file information, or null if file not found or not a supported audio format.</returns>
  Task<AudioFileInfo?> GetFileInfoAsync(
    string path, 
    CancellationToken cancellationToken = default);

  /// <summary>
  /// Checks if a file is a supported audio format.
  /// </summary>
  /// <param name="filePath">The file path to check.</param>
  /// <returns>True if the file is a supported audio format, false otherwise.</returns>
  bool IsSupportedAudioFile(string filePath);

  /// <summary>
  /// Gets a list of supported audio file extensions.
  /// </summary>
  /// <returns>Array of supported extensions (e.g., ".mp3", ".flac", ".wav").</returns>
  string[] GetSupportedExtensions();

  /// <summary>
  /// Gets the count of tracked audio files from the database.
  /// </summary>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>The total number of tracked files, or 0 if repository is not available.</returns>
  Task<int> GetFileCountAsync(CancellationToken cancellationToken = default);

  /// <summary>
  /// Scans for changes (new, modified, removed files) compared to last scan.
  /// </summary>
  /// <param name="path">The path relative to the configured root directory.</param>
  /// <param name="recursive">If true, searches subdirectories recursively.</param>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>A scan result containing lists of new, modified, and removed files.</returns>
  Task<FileScanResult> ScanForChangesAsync(
    string? path = null,
    bool recursive = false,
    CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of a file scan operation, containing lists of changed files.
/// </summary>
public record FileScanResult
{
  /// <summary>
  /// Files that are new (not previously scanned).
  /// </summary>
  public IReadOnlyList<AudioFileInfo> NewFiles { get; init; } = Array.Empty<AudioFileInfo>();

  /// <summary>
  /// Files that have been modified since last scan.
  /// </summary>
  public IReadOnlyList<AudioFileInfo> ModifiedFiles { get; init; } = Array.Empty<AudioFileInfo>();

  /// <summary>
  /// Paths of files that have been removed from disk.
  /// </summary>
  public IReadOnlyList<string> RemovedPaths { get; init; } = Array.Empty<string>();

  /// <summary>
  /// Total number of files currently on disk.
  /// </summary>
  public int TotalFiles { get; init; }
}
