namespace Radio.API.Models;

/// <summary>
/// DTO containing file listing results with current path context.
/// </summary>
public class FileListDto
{
  /// <summary>
  /// Gets or sets the current directory path.
  /// </summary>
  public required string CurrentPath { get; set; }

  /// <summary>
  /// Gets or sets the list of files and directories.
  /// </summary>
  public required List<FileItemDto> Items { get; set; }
}

/// <summary>
/// DTO representing a file or directory item.
/// </summary>
public class FileItemDto
{
  /// <summary>
  /// Gets or sets the item name (file or directory name).
  /// </summary>
  public required string Name { get; set; }

  /// <summary>
  /// Gets or sets the full path to the item.
  /// </summary>
  public required string Path { get; set; }

  /// <summary>
  /// Gets or sets whether this item is a directory.
  /// </summary>
  public bool IsDirectory { get; set; }

  /// <summary>
  /// Gets or sets the file size in bytes (null for directories).
  /// </summary>
  public long? Size { get; set; }

  /// <summary>
  /// Gets or sets the duration as a string (e.g., "3:45").
  /// </summary>
  public string? Duration { get; set; }

  /// <summary>
  /// Gets or sets the artist name from metadata.
  /// </summary>
  public string? Artist { get; set; }

  /// <summary>
  /// Gets or sets the album name from metadata.
  /// </summary>
  public string? Album { get; set; }
}

/// <summary>
/// DTO representing audio file information.
/// </summary>
public class AudioFileInfoDto
{
  /// <summary>
  /// Gets or sets the path to the audio file relative to the root directory.
  /// </summary>
  public required string Path { get; set; }

  /// <summary>
  /// Gets or sets the file name without path.
  /// </summary>
  public required string FileName { get; set; }

  /// <summary>
  /// Gets or sets the file extension (e.g., ".mp3", ".flac").
  /// </summary>
  public required string Extension { get; set; }

  /// <summary>
  /// Gets or sets the file size in bytes.
  /// </summary>
  public required long SizeBytes { get; set; }

  /// <summary>
  /// Gets or sets when the file was created.
  /// </summary>
  public DateTimeOffset CreatedAt { get; set; }

  /// <summary>
  /// Gets or sets when the file was last modified.
  /// </summary>
  public DateTimeOffset LastModifiedAt { get; set; }

  /// <summary>
  /// Gets or sets the track title from metadata (or filename if metadata unavailable).
  /// </summary>
  public string? Title { get; set; }

  /// <summary>
  /// Gets or sets the artist name from metadata.
  /// </summary>
  public string? Artist { get; set; }

  /// <summary>
  /// Gets or sets the album name from metadata.
  /// </summary>
  public string? Album { get; set; }

  /// <summary>
  /// Gets or sets the track duration if available from metadata.
  /// </summary>
  public TimeSpan? Duration { get; set; }

  /// <summary>
  /// Gets or sets the track number from metadata.
  /// </summary>
  public int? TrackNumber { get; set; }

  /// <summary>
  /// Gets or sets the genre from metadata.
  /// </summary>
  public string? Genre { get; set; }

  /// <summary>
  /// Gets or sets the year from metadata.
  /// </summary>
  public int? Year { get; set; }
}

/// <summary>
/// Request DTO for playing a specific audio file.
/// </summary>
public class PlayFileRequestDto
{
  /// <summary>
  /// Gets or sets the path to the audio file to play, relative to the root directory.
  /// </summary>
  public required string Path { get; set; }
}

/// <summary>
/// Response DTO for play file operation.
/// </summary>
public class PlayFileResponseDto
{
  /// <summary>
  /// Gets or sets whether the operation was successful.
  /// </summary>
  public bool Success { get; set; }

  /// <summary>
  /// Gets or sets the response message.
  /// </summary>
  public required string Message { get; set; }

  /// <summary>
  /// Gets or sets the file path that is now playing.
  /// </summary>
  public required string FilePath { get; set; }

  /// <summary>
  /// Gets or sets the file name.
  /// </summary>
  public required string FileName { get; set; }

  /// <summary>
  /// Gets or sets the track title.
  /// </summary>
  public string? Title { get; set; }

  /// <summary>
  /// Gets or sets the artist name.
  /// </summary>
  public string? Artist { get; set; }

  /// <summary>
  /// Gets or sets the album name.
  /// </summary>
  public string? Album { get; set; }

  /// <summary>
  /// Gets or sets the track duration.
  /// </summary>
  public TimeSpan? Duration { get; set; }
}

/// <summary>
/// Request DTO for adding files to the playback queue.
/// </summary>
public class QueueFilesRequestDto
{
  /// <summary>
  /// Gets or sets the list of file paths to add to the queue.
  /// </summary>
  public required List<string> Paths { get; set; }
}

/// <summary>
/// Response DTO for queue files operation.
/// </summary>
public class QueueFilesResponseDto
{
  /// <summary>
  /// Gets or sets whether the operation was successful.
  /// </summary>
  public bool Success { get; set; }

  /// <summary>
  /// Gets or sets the response message.
  /// </summary>
  public required string Message { get; set; }

  /// <summary>
  /// Gets or sets the number of files successfully added to the queue.
  /// </summary>
  public int AddedCount { get; set; }

  /// <summary>
  /// Gets or sets the number of files that failed to be added.
  /// </summary>
  public int FailedCount { get; set; }

  /// <summary>
  /// Gets or sets the list of file paths that failed to be added.
  /// </summary>
  public List<string> FailedPaths { get; set; } = new();
}

/// <summary>
/// DTO representing drive information.
/// </summary>
public class DriveInfoDto
{
  /// <summary>
  /// Gets or sets the drive name (e.g., "C:\\" or "/").
  /// </summary>
  public required string Name { get; set; }

  /// <summary>
  /// Gets or sets the drive label for display (e.g., "Data (D:\\)").
  /// </summary>
  public required string Label { get; set; }

  /// <summary>
  /// Gets or sets the drive type (Fixed, Removable, Network, etc.).
  /// </summary>
  public required string DriveType { get; set; }

  /// <summary>
  /// Gets or sets whether the drive is ready for access.
  /// </summary>
  public bool IsReady { get; set; }

  /// <summary>
  /// Gets or sets the total size of the drive in bytes.
  /// </summary>
  public long TotalSize { get; set; }

  /// <summary>
  /// Gets or sets the available free space in bytes.
  /// </summary>
  public long AvailableSpace { get; set; }

  /// <summary>
  /// Gets or sets the drive format (e.g., "NTFS", "ext4").
  /// </summary>
  public string? DriveFormat { get; set; }
}

/// <summary>
/// DTO representing a bookmarked directory for quick access in the file browser.
/// </summary>
public class BookmarkDto
{
  /// <summary>
  /// Gets or sets the absolute filesystem path.
  /// </summary>
  public required string Path { get; set; }

  /// <summary>
  /// Gets or sets the user-friendly label.
  /// </summary>
  public required string Label { get; set; }

  /// <summary>
  /// Gets or sets the purpose tag (e.g., "music", "sounds").
  /// </summary>
  public required string Tag { get; set; }

  /// <summary>
  /// Gets or sets whether the path is currently accessible on the filesystem.
  /// </summary>
  public bool IsAccessible { get; set; }
}
