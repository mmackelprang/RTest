using Microsoft.AspNetCore.Mvc;
using Radio.API.Models;
using Radio.Core.Interfaces.Audio;
using Radio.Infrastructure.Audio.Sources.Primary;
using SpotifyAPI.Web;

namespace Radio.API.Controllers;

/// <summary>
/// API controller for Spotify search, browse, and playback operations.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class SpotifyController : ControllerBase
{
  private readonly ILogger<SpotifyController> _logger;
  private readonly IAudioEngine _audioEngine;

  /// <summary>
  /// Initializes a new instance of the SpotifyController.
  /// </summary>
  public SpotifyController(
    ILogger<SpotifyController> logger,
    IAudioEngine audioEngine)
  {
    _logger = logger;
    _audioEngine = audioEngine;
  }

  /// <summary>
  /// Searches Spotify for tracks, albums, playlists, artists, or podcasts.
  /// </summary>
  /// <param name="query">The search query string.</param>
  /// <param name="types">Comma-separated list of types to search for (track,album,playlist,artist,show). Default is all types.</param>
  /// <returns>Categorized search results.</returns>
  [HttpGet("search")]
  [ProducesResponseType(typeof(SpotifySearchResultDto), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  [ProducesResponseType(StatusCodes.Status500InternalServerError)]
  public async Task<ActionResult<SpotifySearchResultDto>> Search(
    [FromQuery] string query,
    [FromQuery] string? types = null)
  {
    if (string.IsNullOrWhiteSpace(query))
    {
      return BadRequest(new { error = "Query parameter is required" });
    }

    try
    {
      var spotifySource = GetSpotifySource();
      if (spotifySource == null)
      {
        return BadRequest(new { error = "Spotify source not available" });
      }

      var client = GetSpotifyClient(spotifySource);
      if (client == null)
      {
        return BadRequest(new { error = "Spotify client not authenticated" });
      }

      // Parse search types or default to all
      var searchTypes = ParseSearchTypes(types);
      
      var searchRequest = new SearchRequest(searchTypes, query)
      {
        Limit = 20 // Limit results per category
      };

      var searchResult = await client.Search.Item(searchRequest);
      var result = new SpotifySearchResultDto();

      // Map tracks
      if (searchResult.Tracks?.Items != null)
      {
        result.Tracks = searchResult.Tracks.Items.Select(MapTrack).ToList();
      }

      // Map albums
      if (searchResult.Albums?.Items != null)
      {
        result.Albums = searchResult.Albums.Items.Select(MapAlbum).ToList();
      }

      // Map playlists
      if (searchResult.Playlists?.Items != null)
      {
        result.Playlists = searchResult.Playlists.Items.Select(MapPlaylist).ToList();
      }

      // Map artists
      if (searchResult.Artists?.Items != null)
      {
        result.Artists = searchResult.Artists.Items.Select(MapArtist).ToList();
      }

      // Map shows (podcasts)
      if (searchResult.Shows?.Items != null)
      {
        result.Shows = searchResult.Shows.Items.Select(MapShow).ToList();
      }

      // Note: Audiobooks are not supported in SpotifyAPI.Web v7

      _logger.LogInformation("Spotify search for '{Query}' returned {TrackCount} tracks, {AlbumCount} albums, {PlaylistCount} playlists, {ArtistCount} artists",
        query, result.Tracks.Count, result.Albums.Count, result.Playlists.Count, result.Artists.Count);

      return Ok(result);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error searching Spotify for query '{Query}'", query);
      return StatusCode(500, new { error = "Failed to search Spotify", details = ex.Message });
    }
  }

  /// <summary>
  /// Gets browse categories from Spotify.
  /// </summary>
  /// <returns>List of browse categories.</returns>
  [HttpGet("browse/categories")]
  [ProducesResponseType(typeof(List<CategoryDto>), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  [ProducesResponseType(StatusCodes.Status500InternalServerError)]
  public async Task<ActionResult<List<CategoryDto>>> GetBrowseCategories()
  {
    try
    {
      var spotifySource = GetSpotifySource();
      if (spotifySource == null)
      {
        return BadRequest(new { error = "Spotify source not available" });
      }

      var client = GetSpotifyClient(spotifySource);
      if (client == null)
      {
        return BadRequest(new { error = "Spotify client not authenticated" });
      }

      var categoriesResponse = await client.Browse.GetCategories();
      var categories = categoriesResponse.Categories?.Items?
        .Select(MapCategory).ToList() ?? new List<CategoryDto>();

      _logger.LogInformation("Retrieved {CategoryCount} browse categories from Spotify", categories.Count);

      return Ok(categories);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error getting browse categories from Spotify");
      return StatusCode(500, new { error = "Failed to get browse categories", details = ex.Message });
    }
  }

  /// <summary>
  /// Gets playlists in a specific browse category.
  /// </summary>
  /// <param name="id">The category ID.</param>
  /// <returns>List of playlists in the category.</returns>
  [HttpGet("browse/category/{id}/playlists")]
  [ProducesResponseType(typeof(List<SpotifyPlaylistDto>), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  [ProducesResponseType(StatusCodes.Status404NotFound)]
  [ProducesResponseType(StatusCodes.Status500InternalServerError)]
  public async Task<ActionResult<List<SpotifyPlaylistDto>>> GetCategoryPlaylists(string id)
  {
    if (string.IsNullOrWhiteSpace(id))
    {
      return BadRequest(new { error = "Category ID is required" });
    }

    try
    {
      var spotifySource = GetSpotifySource();
      if (spotifySource == null)
      {
        return BadRequest(new { error = "Spotify source not available" });
      }

      var client = GetSpotifyClient(spotifySource);
      if (client == null)
      {
        return BadRequest(new { error = "Spotify client not authenticated" });
      }

      var playlistsResponse = await client.Browse.GetCategoryPlaylists(id);
      var playlists = playlistsResponse.Playlists?.Items?
        .Select(MapPlaylist).ToList() ?? new List<SpotifyPlaylistDto>();

      _logger.LogInformation("Retrieved {PlaylistCount} playlists from category {CategoryId}", playlists.Count, id);

      return Ok(playlists);
    }
    catch (APIException ex) when (ex.Response?.StatusCode == System.Net.HttpStatusCode.NotFound)
    {
      _logger.LogWarning("Category {CategoryId} not found", id);
      return NotFound(new { error = $"Category '{id}' not found" });
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error getting playlists for category {CategoryId}", id);
      return StatusCode(500, new { error = "Failed to get category playlists", details = ex.Message });
    }
  }

  /// <summary>
  /// Gets the current user's playlists.
  /// </summary>
  /// <returns>List of user playlists.</returns>
  [HttpGet("playlists/user")]
  [ProducesResponseType(typeof(List<SpotifyPlaylistDto>), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  [ProducesResponseType(StatusCodes.Status500InternalServerError)]
  public async Task<ActionResult<List<SpotifyPlaylistDto>>> GetUserPlaylists()
  {
    try
    {
      var spotifySource = GetSpotifySource();
      if (spotifySource == null)
      {
        return BadRequest(new { error = "Spotify source not available" });
      }

      var client = GetSpotifyClient(spotifySource);
      if (client == null)
      {
        return BadRequest(new { error = "Spotify client not authenticated" });
      }

      var playlistsResponse = await client.Playlists.CurrentUsers();
      var playlists = playlistsResponse.Items?
        .Select(MapPlaylist).ToList() ?? new List<SpotifyPlaylistDto>();

      _logger.LogInformation("Retrieved {PlaylistCount} user playlists", playlists.Count);

      return Ok(playlists);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error getting user playlists");
      return StatusCode(500, new { error = "Failed to get user playlists", details = ex.Message });
    }
  }

  /// <summary>
  /// Gets detailed information about a specific playlist including tracks.
  /// </summary>
  /// <param name="id">The playlist ID.</param>
  /// <returns>Detailed playlist information.</returns>
  [HttpGet("playlists/{id}")]
  [ProducesResponseType(typeof(PlaylistDetailsDto), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  [ProducesResponseType(StatusCodes.Status404NotFound)]
  [ProducesResponseType(StatusCodes.Status500InternalServerError)]
  public async Task<ActionResult<PlaylistDetailsDto>> GetPlaylistDetails(string id)
  {
    if (string.IsNullOrWhiteSpace(id))
    {
      return BadRequest(new { error = "Playlist ID is required" });
    }

    try
    {
      var spotifySource = GetSpotifySource();
      if (spotifySource == null)
      {
        return BadRequest(new { error = "Spotify source not available" });
      }

      var client = GetSpotifyClient(spotifySource);
      if (client == null)
      {
        return BadRequest(new { error = "Spotify client not authenticated" });
      }

      var playlist = await client.Playlists.Get(id);
      var details = new PlaylistDetailsDto
      {
        Id = playlist?.Id ?? "",
        Name = playlist?.Name ?? "",
        Owner = playlist?.Owner?.DisplayName ?? playlist?.Owner?.Id ?? "Unknown",
        ImageUrl = playlist?.Images?.FirstOrDefault()?.Url,
        Description = playlist?.Description,
        Uri = playlist?.Uri ?? "",
        TotalTracks = playlist?.Tracks?.Total ?? 0,
        Followers = playlist?.Followers?.Total ?? 0,
        Tracks = playlist?.Tracks?.Items?
          .Where(item => item.Track is FullTrack)
          .Select(item => MapTrack((FullTrack)item.Track))
          .ToList() ?? new List<SpotifyTrackDto>()
      };

      _logger.LogInformation("Retrieved playlist '{PlaylistName}' with {TrackCount} tracks", playlist?.Name ?? id, details.Tracks.Count);

      return Ok(details);
    }
    catch (APIException ex) when (ex.Response?.StatusCode == System.Net.HttpStatusCode.NotFound)
    {
      _logger.LogWarning("Playlist {PlaylistId} not found", id);
      return NotFound(new { error = $"Playlist '{id}' not found" });
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error getting playlist details for {PlaylistId}", id);
      return StatusCode(500, new { error = "Failed to get playlist details", details = ex.Message });
    }
  }

  /// <summary>
  /// Plays a track, album, or playlist by Spotify URI.
  /// </summary>
  /// <param name="request">The playback request.</param>
  /// <returns>Success or error response.</returns>
  [HttpPost("play")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  [ProducesResponseType(StatusCodes.Status500InternalServerError)]
  public async Task<ActionResult> Play([FromBody] PlaySpotifyUriRequest request)
  {
    if (string.IsNullOrWhiteSpace(request.Uri))
    {
      return BadRequest(new { error = "URI is required" });
    }

    try
    {
      var spotifySource = GetSpotifySource();
      if (spotifySource == null)
      {
        return BadRequest(new { error = "Spotify source not available" });
      }

      // Make sure Spotify is the active source
      var mixer = _audioEngine.GetMasterMixer();
      var activeSources = mixer.GetActiveSources();
      var activeSpotify = activeSources.FirstOrDefault(s => s is SpotifyAudioSource);

      if (activeSpotify == null)
      {
        // Spotify source needs to be activated - this should be done via SourcesController
        return BadRequest(new { error = "Spotify source is not active. Activate it first via /api/sources/activate endpoint." });
      }

      // Use the context URI if provided, otherwise use the main URI
      var uriToPlay = !string.IsNullOrWhiteSpace(request.ContextUri) ? request.ContextUri : request.Uri;
      
      await spotifySource.PlayUriAsync(uriToPlay);

      _logger.LogInformation("Started playback of Spotify URI: {Uri}", uriToPlay);

      return Ok(new { message = "Playback started", uri = uriToPlay });
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error playing Spotify URI {Uri}", request.Uri);
      return StatusCode(500, new { error = "Failed to play Spotify URI", details = ex.Message });
    }
  }

  #region Helper Methods

  private SpotifyAudioSource? GetSpotifySource()
  {
    var mixer = _audioEngine.GetMasterMixer();
    var sources = mixer.GetActiveSources();
    return sources.OfType<SpotifyAudioSource>().FirstOrDefault();
  }

  private SpotifyClient? GetSpotifyClient(SpotifyAudioSource source)
  {
    // Use reflection to access the private _client field
    var clientField = typeof(SpotifyAudioSource).GetField("_client",
      System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
    return clientField?.GetValue(source) as SpotifyClient;
  }

  private SearchRequest.Types ParseSearchTypes(string? types)
  {
    if (string.IsNullOrWhiteSpace(types))
    {
      // Default to all types
      return SearchRequest.Types.Track | SearchRequest.Types.Album | 
             SearchRequest.Types.Playlist | SearchRequest.Types.Artist;
    }

    var typesList = types.ToLowerInvariant().Split(',', StringSplitOptions.RemoveEmptyEntries);
    var searchTypes = SearchRequest.Types.All;
    var hasAny = false;

    foreach (var type in typesList)
    {
      switch (type.Trim())
      {
        case "track":
        case "music":
          searchTypes = hasAny ? searchTypes | SearchRequest.Types.Track : SearchRequest.Types.Track;
          hasAny = true;
          break;
        case "album":
        case "albums":
          searchTypes = hasAny ? searchTypes | SearchRequest.Types.Album : SearchRequest.Types.Album;
          hasAny = true;
          break;
        case "playlist":
        case "playlists":
          searchTypes = hasAny ? searchTypes | SearchRequest.Types.Playlist : SearchRequest.Types.Playlist;
          hasAny = true;
          break;
        case "artist":
        case "artists":
          searchTypes = hasAny ? searchTypes | SearchRequest.Types.Artist : SearchRequest.Types.Artist;
          hasAny = true;
          break;
        case "show":
        case "podcast":
        case "podcasts":
          searchTypes = hasAny ? searchTypes | SearchRequest.Types.Show : SearchRequest.Types.Show;
          hasAny = true;
          break;
        case "all":
          return SearchRequest.Types.Track | SearchRequest.Types.Album | 
                 SearchRequest.Types.Playlist | SearchRequest.Types.Artist;
      }
    }

    return hasAny ? searchTypes : (SearchRequest.Types.Track | SearchRequest.Types.Album | 
                                    SearchRequest.Types.Playlist | SearchRequest.Types.Artist);
  }

  private SpotifyTrackDto MapTrack(FullTrack track)
  {
    return new SpotifyTrackDto
    {
      Id = track.Id,
      Name = track.Name,
      Artist = string.Join(", ", track.Artists.Select(a => a.Name)),
      Album = track.Album.Name,
      Duration = TimeSpan.FromMilliseconds(track.DurationMs),
      Uri = track.Uri,
      AlbumArtUrl = track.Album.Images?.FirstOrDefault()?.Url
    };
  }

  private SpotifyAlbumDto MapAlbum(SimpleAlbum album)
  {
    return new SpotifyAlbumDto
    {
      Id = album.Id,
      Name = album.Name,
      Artist = string.Join(", ", album.Artists.Select(a => a.Name)),
      ImageUrl = album.Images?.FirstOrDefault()?.Url,
      Uri = album.Uri,
      ReleaseDate = album.ReleaseDate,
      TotalTracks = album.TotalTracks
    };
  }

  private SpotifyPlaylistDto MapPlaylist(dynamic playlist)
  {
    return new SpotifyPlaylistDto
    {
      Id = playlist.Id,
      Name = playlist.Name,
      Owner = playlist.Owner?.DisplayName ?? playlist.Owner?.Id ?? "Unknown",
      ImageUrl = playlist.Images?.FirstOrDefault()?.Url,
      TrackCount = playlist.Tracks?.Total ?? 0,
      Uri = playlist.Uri,
      Description = playlist.Description
    };
  }

  private SpotifyArtistDto MapArtist(FullArtist artist)
  {
    return new SpotifyArtistDto
    {
      Id = artist.Id,
      Name = artist.Name,
      ImageUrl = artist.Images?.FirstOrDefault()?.Url,
      Uri = artist.Uri,
      Followers = artist.Followers?.Total ?? 0,
      Genres = artist.Genres?.ToList() ?? new List<string>()
    };
  }

  private SpotifyShowDto MapShow(SimpleShow show)
  {
    return new SpotifyShowDto
    {
      Id = show.Id,
      Name = show.Name,
      Publisher = show.Publisher,
      ImageUrl = show.Images?.FirstOrDefault()?.Url,
      Uri = show.Uri,
      Description = show.Description
    };
  }

  private SpotifyAudiobookDto MapAudiobook(SimpleAudiobook audiobook)
  {
    return new SpotifyAudiobookDto
    {
      Id = audiobook.Id,
      Name = audiobook.Name,
      Author = string.Join(", ", audiobook.Authors.Select(a => a.Name)),
      ImageUrl = audiobook.Images?.FirstOrDefault()?.Url,
      Uri = audiobook.Uri,
      Description = audiobook.Description
    };
  }

  private CategoryDto MapCategory(Category category)
  {
    return new CategoryDto
    {
      Id = category.Id,
      Name = category.Name,
      Icons = category.Icons?.Select(icon => new IconDto
      {
        Url = icon.Url,
        Width = icon.Width,
        Height = icon.Height
      }).ToList() ?? new List<IconDto>()
    };
  }

  #endregion
}
