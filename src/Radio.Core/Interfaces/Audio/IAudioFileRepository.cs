using Radio.Core.Models.Audio;

namespace Radio.Core.Interfaces.Audio;

/// <summary>
/// Repository for persisting audio file metadata and scan state.
/// Used by FileBrowser to track which files are new vs. existing.
/// </summary>
public interface IAudioFileRepository
{
  /// <summary>
  /// Gets all tracked audio files.
  /// </summary>
  /// <param name="ct">Cancellation token.</param>
  /// <returns>A list of all tracked audio files.</returns>
  Task<IReadOnlyList<AudioFileInfo>> GetAllAsync(CancellationToken ct = default);

  /// <summary>
  /// Gets tracked files in a specific directory.
  /// </summary>
  /// <param name="directoryPath">The directory path to search.</param>
  /// <param name="recursive">Whether to include files from subdirectories.</param>
  /// <param name="ct">Cancellation token.</param>
  /// <returns>A list of audio files in the directory.</returns>
  Task<IReadOnlyList<AudioFileInfo>> GetByDirectoryAsync(
    string directoryPath,
    bool recursive = false,
    CancellationToken ct = default);

  /// <summary>
  /// Gets a single file by path.
  /// </summary>
  /// <param name="path">The file path.</param>
  /// <param name="ct">Cancellation token.</param>
  /// <returns>The audio file info, or null if not found.</returns>
  Task<AudioFileInfo?> GetByPathAsync(string path, CancellationToken ct = default);

  /// <summary>
  /// Adds or updates a file record.
  /// </summary>
  /// <param name="file">The audio file info to upsert.</param>
  /// <param name="ct">Cancellation token.</param>
  Task UpsertAsync(AudioFileInfo file, CancellationToken ct = default);

  /// <summary>
  /// Bulk adds or updates file records.
  /// </summary>
  /// <param name="files">The audio file infos to upsert.</param>
  /// <param name="ct">Cancellation token.</param>
  Task UpsertBatchAsync(IEnumerable<AudioFileInfo> files, CancellationToken ct = default);

  /// <summary>
  /// Removes a file record by path.
  /// </summary>
  /// <param name="path">The file path to remove.</param>
  /// <param name="ct">Cancellation token.</param>
  /// <returns>True if the file was removed, false if not found.</returns>
  Task<bool> RemoveAsync(string path, CancellationToken ct = default);

  /// <summary>
  /// Removes all files in a directory that are no longer present in the provided list.
  /// </summary>
  /// <param name="directoryPath">The directory path to clean up.</param>
  /// <param name="currentPaths">The list of current file paths that still exist.</param>
  /// <param name="ct">Cancellation token.</param>
  /// <returns>The number of stale files removed.</returns>
  Task<int> RemoveStaleAsync(
    string directoryPath,
    IEnumerable<string> currentPaths,
    CancellationToken ct = default);

  /// <summary>
  /// Gets the count of tracked files.
  /// </summary>
  /// <param name="ct">Cancellation token.</param>
  /// <returns>The total number of tracked files.</returns>
  Task<int> GetCountAsync(CancellationToken ct = default);

  /// <summary>
  /// Gets files that need metadata update (last modified changed).
  /// </summary>
  /// <param name="currentFiles">The current files with their last modified timestamps.</param>
  /// <param name="ct">Cancellation token.</param>
  /// <returns>A list of files that have been modified since last scan.</returns>
  Task<IReadOnlyList<AudioFileInfo>> GetStaleMetadataAsync(
    IEnumerable<(string Path, DateTimeOffset LastModified)> currentFiles,
    CancellationToken ct = default);
}
