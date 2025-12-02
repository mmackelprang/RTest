namespace Radio.Core.Models.Audio;

/// <summary>
/// Represents play statistics for the play history.
/// </summary>
public sealed record PlayStatistics
{
  /// <summary>Gets the total number of plays.</summary>
  public required int TotalPlays { get; init; }

  /// <summary>Gets the number of identified plays.</summary>
  public required int IdentifiedPlays { get; init; }

  /// <summary>Gets the number of unidentified plays.</summary>
  public required int UnidentifiedPlays { get; init; }

  /// <summary>Gets the count of plays by source type.</summary>
  public required IReadOnlyDictionary<PlaySource, int> PlaysBySource { get; init; }

  /// <summary>Gets the top artists by play count.</summary>
  public required IReadOnlyList<ArtistPlayCount> TopArtists { get; init; }

  /// <summary>Gets the top tracks by play count.</summary>
  public required IReadOnlyList<TrackPlayCount> TopTracks { get; init; }
}

/// <summary>
/// Represents an artist and their play count.
/// </summary>
public sealed record ArtistPlayCount
{
  /// <summary>Gets the artist name.</summary>
  public required string Artist { get; init; }

  /// <summary>Gets the number of plays.</summary>
  public required int PlayCount { get; init; }
}

/// <summary>
/// Represents a track and its play count.
/// </summary>
public sealed record TrackPlayCount
{
  /// <summary>Gets the track title.</summary>
  public required string Title { get; init; }

  /// <summary>Gets the artist name.</summary>
  public required string Artist { get; init; }

  /// <summary>Gets the number of plays.</summary>
  public required int PlayCount { get; init; }
}
