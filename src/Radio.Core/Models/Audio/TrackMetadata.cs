namespace Radio.Core.Models.Audio;

/// <summary>
/// Represents metadata about a track identified via fingerprinting or manual entry.
/// </summary>
public sealed record TrackMetadata
{
  /// <summary>Gets the unique identifier for this metadata record.</summary>
  public required string Id { get; init; }

  /// <summary>Gets the associated fingerprint ID (optional).</summary>
  public string? FingerprintId { get; init; }

  /// <summary>Gets the track title.</summary>
  public required string Title { get; init; }

  /// <summary>Gets the artist name.</summary>
  public required string Artist { get; init; }

  /// <summary>Gets the album name (optional).</summary>
  public string? Album { get; init; }

  /// <summary>Gets the album artist (optional).</summary>
  public string? AlbumArtist { get; init; }

  /// <summary>Gets the track number on the album (optional).</summary>
  public int? TrackNumber { get; init; }

  /// <summary>Gets the disc number (optional).</summary>
  public int? DiscNumber { get; init; }

  /// <summary>Gets the release year (optional).</summary>
  public int? ReleaseYear { get; init; }

  /// <summary>Gets the genre (optional).</summary>
  public string? Genre { get; init; }

  /// <summary>Gets the MusicBrainz artist ID (optional).</summary>
  public string? MusicBrainzArtistId { get; init; }

  /// <summary>Gets the MusicBrainz release/album ID (optional).</summary>
  public string? MusicBrainzReleaseId { get; init; }

  /// <summary>Gets the MusicBrainz recording ID (optional).</summary>
  public string? MusicBrainzRecordingId { get; init; }

  /// <summary>Gets the cover art URL (optional).</summary>
  public string? CoverArtUrl { get; init; }

  /// <summary>Gets the source of this metadata.</summary>
  public required MetadataSource Source { get; init; }

  /// <summary>Gets when this metadata was created.</summary>
  public required DateTime CreatedAt { get; init; }

  /// <summary>Gets when this metadata was last updated.</summary>
  public required DateTime UpdatedAt { get; init; }
}

/// <summary>
/// Specifies the source of track metadata.
/// </summary>
public enum MetadataSource
{
  /// <summary>Metadata obtained from AcoustID/MusicBrainz lookup.</summary>
  AcoustID,

  /// <summary>Metadata manually entered by user.</summary>
  Manual,

  /// <summary>Metadata extracted from audio file tags.</summary>
  FileTag,

  /// <summary>Metadata obtained from Spotify.</summary>
  Spotify,

  /// <summary>Metadata obtained from fingerprinting service.</summary>
  Fingerprinting
}
