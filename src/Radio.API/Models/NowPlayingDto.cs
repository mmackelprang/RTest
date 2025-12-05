namespace Radio.API.Models;

/// <summary>
/// Represents the current "Now Playing" information with structured track data.
/// This DTO always returns valid content with defaults when no track is available.
/// </summary>
public class NowPlayingDto
{
  /// <summary>
  /// Gets or sets the type of the audio source (e.g., "Spotify", "Radio", "FilePlayer").
  /// </summary>
  public string SourceType { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets the display name of the audio source.
  /// </summary>
  public string SourceName { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets whether audio is currently playing.
  /// </summary>
  public bool IsPlaying { get; set; }

  /// <summary>
  /// Gets or sets whether playback is paused.
  /// </summary>
  public bool IsPaused { get; set; }

  /// <summary>
  /// Gets or sets the track title. Never null; defaults to "No Track" when empty.
  /// </summary>
  public string Title { get; set; } = "No Track";

  /// <summary>
  /// Gets or sets the artist name. Never null; defaults to "--" when empty.
  /// </summary>
  public string Artist { get; set; } = "--";

  /// <summary>
  /// Gets or sets the album name. Never null; defaults to "--" when empty.
  /// </summary>
  public string Album { get; set; } = "--";

  /// <summary>
  /// Gets or sets the URL to the album art. Never null; defaults to generic icon URL when empty.
  /// </summary>
  public string AlbumArtUrl { get; set; } = "/images/default-album-art.png";

  /// <summary>
  /// Gets or sets the current playback position, if available.
  /// </summary>
  public TimeSpan? Position { get; set; }

  /// <summary>
  /// Gets or sets the track duration, if available.
  /// </summary>
  public TimeSpan? Duration { get; set; }

  /// <summary>
  /// Gets or sets the playback progress as a percentage (0-100), if duration is available.
  /// </summary>
  public double? ProgressPercentage { get; set; }

  /// <summary>
  /// Gets or sets additional metadata specific to the source or track.
  /// May include genre, year, bitrate, or other source-specific information.
  /// </summary>
  public Dictionary<string, object>? ExtendedMetadata { get; set; }
}
