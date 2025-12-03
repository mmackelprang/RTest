namespace Radio.API.Models;

/// <summary>
/// Represents Spotify search results with categorized items.
/// </summary>
public class SpotifySearchResultDto
{
  /// <summary>
  /// Gets or sets the list of tracks in the search results.
  /// </summary>
  public List<SpotifyTrackDto> Tracks { get; set; } = new();

  /// <summary>
  /// Gets or sets the list of albums in the search results.
  /// </summary>
  public List<SpotifyAlbumDto> Albums { get; set; } = new();

  /// <summary>
  /// Gets or sets the list of playlists in the search results.
  /// </summary>
  public List<SpotifyPlaylistDto> Playlists { get; set; } = new();

  /// <summary>
  /// Gets or sets the list of artists in the search results.
  /// </summary>
  public List<SpotifyArtistDto> Artists { get; set; } = new();

  /// <summary>
  /// Gets or sets the list of podcasts/shows in the search results.
  /// </summary>
  public List<SpotifyShowDto> Shows { get; set; } = new();

  /// <summary>
  /// Gets or sets the list of audiobooks in the search results.
  /// </summary>
  public List<SpotifyAudiobookDto> Audiobooks { get; set; } = new();
}

/// <summary>
/// Represents a Spotify track.
/// </summary>
public class SpotifyTrackDto
{
  /// <summary>
  /// Gets or sets the track ID.
  /// </summary>
  public string Id { get; set; } = "";

  /// <summary>
  /// Gets or sets the track name.
  /// </summary>
  public string Name { get; set; } = "";

  /// <summary>
  /// Gets or sets the artist names.
  /// </summary>
  public string Artist { get; set; } = "";

  /// <summary>
  /// Gets or sets the album name.
  /// </summary>
  public string Album { get; set; } = "";

  /// <summary>
  /// Gets or sets the track duration.
  /// </summary>
  public TimeSpan Duration { get; set; }

  /// <summary>
  /// Gets or sets the Spotify URI for the track.
  /// </summary>
  public string Uri { get; set; } = "";

  /// <summary>
  /// Gets or sets the album art URL.
  /// </summary>
  public string? AlbumArtUrl { get; set; }
}

/// <summary>
/// Represents a Spotify album.
/// </summary>
public class SpotifyAlbumDto
{
  /// <summary>
  /// Gets or sets the album ID.
  /// </summary>
  public string Id { get; set; } = "";

  /// <summary>
  /// Gets or sets the album name.
  /// </summary>
  public string Name { get; set; } = "";

  /// <summary>
  /// Gets or sets the artist names.
  /// </summary>
  public string Artist { get; set; } = "";

  /// <summary>
  /// Gets or sets the album image URL.
  /// </summary>
  public string? ImageUrl { get; set; }

  /// <summary>
  /// Gets or sets the Spotify URI for the album.
  /// </summary>
  public string Uri { get; set; } = "";

  /// <summary>
  /// Gets or sets the release date.
  /// </summary>
  public string? ReleaseDate { get; set; }

  /// <summary>
  /// Gets or sets the total number of tracks.
  /// </summary>
  public int TotalTracks { get; set; }
}

/// <summary>
/// Represents a Spotify playlist.
/// </summary>
public class SpotifyPlaylistDto
{
  /// <summary>
  /// Gets or sets the playlist ID.
  /// </summary>
  public string Id { get; set; } = "";

  /// <summary>
  /// Gets or sets the playlist name.
  /// </summary>
  public string Name { get; set; } = "";

  /// <summary>
  /// Gets or sets the playlist owner.
  /// </summary>
  public string Owner { get; set; } = "";

  /// <summary>
  /// Gets or sets the playlist image URL.
  /// </summary>
  public string? ImageUrl { get; set; }

  /// <summary>
  /// Gets or sets the track count.
  /// </summary>
  public int TrackCount { get; set; }

  /// <summary>
  /// Gets or sets the Spotify URI for the playlist.
  /// </summary>
  public string Uri { get; set; } = "";

  /// <summary>
  /// Gets or sets the playlist description.
  /// </summary>
  public string? Description { get; set; }
}

/// <summary>
/// Represents a Spotify artist.
/// </summary>
public class SpotifyArtistDto
{
  /// <summary>
  /// Gets or sets the artist ID.
  /// </summary>
  public string Id { get; set; } = "";

  /// <summary>
  /// Gets or sets the artist name.
  /// </summary>
  public string Name { get; set; } = "";

  /// <summary>
  /// Gets or sets the artist image URL.
  /// </summary>
  public string? ImageUrl { get; set; }

  /// <summary>
  /// Gets or sets the Spotify URI for the artist.
  /// </summary>
  public string Uri { get; set; } = "";

  /// <summary>
  /// Gets or sets the follower count.
  /// </summary>
  public int Followers { get; set; }

  /// <summary>
  /// Gets or sets the genres.
  /// </summary>
  public List<string> Genres { get; set; } = new();
}

/// <summary>
/// Represents a Spotify show (podcast).
/// </summary>
public class SpotifyShowDto
{
  /// <summary>
  /// Gets or sets the show ID.
  /// </summary>
  public string Id { get; set; } = "";

  /// <summary>
  /// Gets or sets the show name.
  /// </summary>
  public string Name { get; set; } = "";

  /// <summary>
  /// Gets or sets the publisher name.
  /// </summary>
  public string Publisher { get; set; } = "";

  /// <summary>
  /// Gets or sets the show image URL.
  /// </summary>
  public string? ImageUrl { get; set; }

  /// <summary>
  /// Gets or sets the Spotify URI for the show.
  /// </summary>
  public string Uri { get; set; } = "";

  /// <summary>
  /// Gets or sets the show description.
  /// </summary>
  public string? Description { get; set; }
}

/// <summary>
/// Represents a Spotify audiobook.
/// </summary>
public class SpotifyAudiobookDto
{
  /// <summary>
  /// Gets or sets the audiobook ID.
  /// </summary>
  public string Id { get; set; } = "";

  /// <summary>
  /// Gets or sets the audiobook name.
  /// </summary>
  public string Name { get; set; } = "";

  /// <summary>
  /// Gets or sets the author names.
  /// </summary>
  public string Author { get; set; } = "";

  /// <summary>
  /// Gets or sets the audiobook image URL.
  /// </summary>
  public string? ImageUrl { get; set; }

  /// <summary>
  /// Gets or sets the Spotify URI for the audiobook.
  /// </summary>
  public string Uri { get; set; } = "";

  /// <summary>
  /// Gets or sets the audiobook description.
  /// </summary>
  public string? Description { get; set; }
}

/// <summary>
/// Represents a Spotify browse category.
/// </summary>
public class CategoryDto
{
  /// <summary>
  /// Gets or sets the category ID.
  /// </summary>
  public string Id { get; set; } = "";

  /// <summary>
  /// Gets or sets the category name.
  /// </summary>
  public string Name { get; set; } = "";

  /// <summary>
  /// Gets or sets the category icon URLs.
  /// </summary>
  public List<IconDto> Icons { get; set; } = new();
}

/// <summary>
/// Represents a category icon.
/// </summary>
public class IconDto
{
  /// <summary>
  /// Gets or sets the icon URL.
  /// </summary>
  public string Url { get; set; } = "";

  /// <summary>
  /// Gets or sets the icon width.
  /// </summary>
  public int? Width { get; set; }

  /// <summary>
  /// Gets or sets the icon height.
  /// </summary>
  public int? Height { get; set; }
}

/// <summary>
/// Represents detailed playlist information with tracks.
/// </summary>
public class PlaylistDetailsDto
{
  /// <summary>
  /// Gets or sets the playlist ID.
  /// </summary>
  public string Id { get; set; } = "";

  /// <summary>
  /// Gets or sets the playlist name.
  /// </summary>
  public string Name { get; set; } = "";

  /// <summary>
  /// Gets or sets the playlist owner.
  /// </summary>
  public string Owner { get; set; } = "";

  /// <summary>
  /// Gets or sets the playlist image URL.
  /// </summary>
  public string? ImageUrl { get; set; }

  /// <summary>
  /// Gets or sets the playlist description.
  /// </summary>
  public string? Description { get; set; }

  /// <summary>
  /// Gets or sets the Spotify URI for the playlist.
  /// </summary>
  public string Uri { get; set; } = "";

  /// <summary>
  /// Gets or sets the tracks in the playlist.
  /// </summary>
  public List<SpotifyTrackDto> Tracks { get; set; } = new();

  /// <summary>
  /// Gets or sets the total track count.
  /// </summary>
  public int TotalTracks { get; set; }

  /// <summary>
  /// Gets or sets the follower count.
  /// </summary>
  public int Followers { get; set; }
}

/// <summary>
/// Request to play a Spotify URI.
/// </summary>
public class PlaySpotifyUriRequest
{
  /// <summary>
  /// Gets or sets the Spotify URI to play (track, album, playlist, etc.).
  /// </summary>
  public string Uri { get; set; } = "";

  /// <summary>
  /// Gets or sets the context URI (album or playlist) when playing a track.
  /// </summary>
  public string? ContextUri { get; set; }
}

/// <summary>
/// Spotify authorization URL response.
/// </summary>
public class AuthUrlDto
{
  /// <summary>
  /// Gets or sets the authorization URL to redirect the user to.
  /// </summary>
  public string Url { get; set; } = "";

  /// <summary>
  /// Gets or sets the state parameter for CSRF protection.
  /// </summary>
  public string State { get; set; } = "";

  /// <summary>
  /// Gets or sets the PKCE code verifier.
  /// Should be stored and used when handling the callback.
  /// </summary>
  public string CodeVerifier { get; set; } = "";
}

/// <summary>
/// Spotify authentication status response.
/// </summary>
public class AuthStatusDto
{
  /// <summary>
  /// Gets or sets a value indicating whether the user is authenticated.
  /// </summary>
  public bool IsAuthenticated { get; set; }

  /// <summary>
  /// Gets or sets the Spotify username (if authenticated).
  /// </summary>
  public string? Username { get; set; }

  /// <summary>
  /// Gets or sets the user's display name (if authenticated).
  /// </summary>
  public string? DisplayName { get; set; }

  /// <summary>
  /// Gets or sets when the current access token expires (if authenticated).
  /// </summary>
  public DateTimeOffset? ExpiresAt { get; set; }

  /// <summary>
  /// Gets or sets the user's Spotify user ID (if authenticated).
  /// </summary>
  public string? UserId { get; set; }
}
