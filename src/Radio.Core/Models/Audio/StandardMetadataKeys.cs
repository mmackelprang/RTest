namespace Radio.Core.Models.Audio;

/// <summary>
/// Standard metadata keys used across all audio sources.
/// Using these constants ensures consistency in metadata handling.
/// </summary>
public static class StandardMetadataKeys
{
  /// <summary>
  /// Title of the track, song, or content.
  /// Default: "No Track"
  /// </summary>
  public const string Title = "Title";

  /// <summary>
  /// Artist or performer of the track.
  /// Default: "--"
  /// </summary>
  public const string Artist = "Artist";

  /// <summary>
  /// Album name containing the track.
  /// Default: "--"
  /// </summary>
  public const string Album = "Album";

  /// <summary>
  /// URL to album art image.
  /// Default: "/images/default-album-art.png"
  /// </summary>
  public const string AlbumArtUrl = "AlbumArtUrl";

  /// <summary>
  /// Duration of the track as TimeSpan.
  /// Type: TimeSpan?
  /// </summary>
  public const string Duration = "Duration";

  /// <summary>
  /// Track number within the album.
  /// Type: int?
  /// </summary>
  public const string TrackNumber = "TrackNumber";

  /// <summary>
  /// Genre or style of music.
  /// </summary>
  public const string Genre = "Genre";

  /// <summary>
  /// Year of release.
  /// Type: int?
  /// </summary>
  public const string Year = "Year";

  /// <summary>
  /// Default album art URL when no album art is available.
  /// </summary>
  public const string DefaultAlbumArtUrl = "/images/default-album-art.png";

  /// <summary>
  /// Default title when no track is playing.
  /// </summary>
  public const string DefaultTitle = "No Track";

  /// <summary>
  /// Default artist when no artist information is available.
  /// </summary>
  public const string DefaultArtist = "--";

  /// <summary>
  /// Default album when no album information is available.
  /// </summary>
  public const string DefaultAlbum = "--";
}
