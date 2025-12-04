namespace Radio.Core.Models.Audio;

/// <summary>
/// Information about an audio file discovered by the file browser.
/// </summary>
public sealed record AudioFileInfo
{
  /// <summary>
  /// The path to the audio file relative to the root directory.
  /// </summary>
  public required string Path { get; init; }

  /// <summary>
  /// The file name without path.
  /// </summary>
  public required string FileName { get; init; }

  /// <summary>
  /// The file extension (e.g., ".mp3", ".flac").
  /// </summary>
  public required string Extension { get; init; }

  /// <summary>
  /// File size in bytes.
  /// </summary>
  public required long SizeBytes { get; init; }

  /// <summary>
  /// When the file was created.
  /// </summary>
  public DateTimeOffset CreatedAt { get; init; }

  /// <summary>
  /// When the file was last modified.
  /// </summary>
  public DateTimeOffset LastModifiedAt { get; init; }

  /// <summary>
  /// Track title from metadata (or filename if metadata unavailable).
  /// </summary>
  public string? Title { get; init; }

  /// <summary>
  /// Artist name from metadata.
  /// </summary>
  public string? Artist { get; init; }

  /// <summary>
  /// Album name from metadata.
  /// </summary>
  public string? Album { get; init; }

  /// <summary>
  /// Track duration if available from metadata.
  /// </summary>
  public TimeSpan? Duration { get; init; }

  /// <summary>
  /// Track number from metadata.
  /// </summary>
  public int? TrackNumber { get; init; }

  /// <summary>
  /// Genre from metadata.
  /// </summary>
  public string? Genre { get; init; }

  /// <summary>
  /// Year from metadata.
  /// </summary>
  public int? Year { get; init; }
}
