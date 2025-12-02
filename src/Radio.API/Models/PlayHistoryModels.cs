namespace Radio.API.Models;

/// <summary>
/// Represents a play history entry.
/// </summary>
public class PlayHistoryEntryDto
{
  /// <summary>Gets or sets the unique identifier.</summary>
  public string Id { get; set; } = string.Empty;

  /// <summary>Gets or sets when the track was played.</summary>
  public DateTime PlayedAt { get; set; }

  /// <summary>Gets or sets the audio source type (Vinyl, Radio, File, Spotify).</summary>
  public string Source { get; set; } = string.Empty;

  /// <summary>Gets or sets the metadata source (Spotify, FileTag, Fingerprinting).</summary>
  public string? MetadataSource { get; set; }

  /// <summary>Gets or sets additional source details.</summary>
  public string? SourceDetails { get; set; }

  /// <summary>Gets or sets the track duration in seconds.</summary>
  public int? DurationSeconds { get; set; }

  /// <summary>Gets or sets the identification confidence (0.0 to 1.0).</summary>
  public double? IdentificationConfidence { get; set; }

  /// <summary>Gets or sets whether the track was identified.</summary>
  public bool WasIdentified { get; set; }

  /// <summary>Gets or sets the track metadata.</summary>
  public TrackMetadataDto? Track { get; set; }
}

/// <summary>
/// Represents track metadata.
/// </summary>
public class TrackMetadataDto
{
  /// <summary>Gets or sets the track title.</summary>
  public string Title { get; set; } = string.Empty;

  /// <summary>Gets or sets the artist name.</summary>
  public string Artist { get; set; } = string.Empty;

  /// <summary>Gets or sets the album name.</summary>
  public string? Album { get; set; }

  /// <summary>Gets or sets the album artist.</summary>
  public string? AlbumArtist { get; set; }

  /// <summary>Gets or sets the cover art URL.</summary>
  public string? CoverArtUrl { get; set; }
}

/// <summary>
/// Represents play statistics.
/// </summary>
public class PlayStatisticsDto
{
  /// <summary>Gets or sets the total number of plays.</summary>
  public int TotalPlays { get; set; }

  /// <summary>Gets or sets the number of identified plays.</summary>
  public int IdentifiedPlays { get; set; }

  /// <summary>Gets or sets the number of unidentified plays.</summary>
  public int UnidentifiedPlays { get; set; }

  /// <summary>Gets or sets plays by source type.</summary>
  public Dictionary<string, int> PlaysBySource { get; set; } = new();

  /// <summary>Gets or sets top artists by play count.</summary>
  public List<ArtistPlayCountDto> TopArtists { get; set; } = [];

  /// <summary>Gets or sets top tracks by play count.</summary>
  public List<TrackPlayCountDto> TopTracks { get; set; } = [];
}

/// <summary>
/// Represents an artist play count.
/// </summary>
public class ArtistPlayCountDto
{
  /// <summary>Gets or sets the artist name.</summary>
  public string Artist { get; set; } = string.Empty;

  /// <summary>Gets or sets the play count.</summary>
  public int PlayCount { get; set; }
}

/// <summary>
/// Represents a track play count.
/// </summary>
public class TrackPlayCountDto
{
  /// <summary>Gets or sets the track title.</summary>
  public string Title { get; set; } = string.Empty;

  /// <summary>Gets or sets the artist name.</summary>
  public string Artist { get; set; } = string.Empty;

  /// <summary>Gets or sets the play count.</summary>
  public int PlayCount { get; set; }
}

/// <summary>
/// Request to record a play history entry.
/// </summary>
public class RecordPlayRequest
{
  /// <summary>Gets or sets the audio source type.</summary>
  public string Source { get; set; } = string.Empty;

  /// <summary>Gets or sets the metadata source (optional).</summary>
  public string? MetadataSource { get; set; }

  /// <summary>Gets or sets additional source details.</summary>
  public string? SourceDetails { get; set; }

  /// <summary>Gets or sets the track title (optional).</summary>
  public string? Title { get; set; }

  /// <summary>Gets or sets the artist name (optional).</summary>
  public string? Artist { get; set; }

  /// <summary>Gets or sets the album name (optional).</summary>
  public string? Album { get; set; }

  /// <summary>Gets or sets the duration in seconds (optional).</summary>
  public int? DurationSeconds { get; set; }
}
