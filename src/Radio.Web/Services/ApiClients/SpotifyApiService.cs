using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Radio.Web.Models;

namespace Radio.Web.Services.ApiClients;

/// <summary>
/// API client service for Spotify integration endpoints (10 endpoints)
/// </summary>
public class SpotifyApiService
{
  private readonly HttpClient _httpClient;
  private readonly ILogger<SpotifyApiService> _logger;

  public SpotifyApiService(HttpClient httpClient, ILogger<SpotifyApiService> logger)
  {
    _httpClient = httpClient;
    _logger = logger;
  }

  public async Task<SpotifyAuthStatusDto?> GetAuthStatusAsync(CancellationToken cancellationToken = default)
  {
    try
    {
      return await _httpClient.GetFromJsonAsync<SpotifyAuthStatusDto>("/api/spotify/auth", cancellationToken);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to get Spotify auth status");
      return null;
    }
  }

  public async Task<SpotifyAuthUrlDto?> GetAuthUrlAsync(string redirectUri, CancellationToken cancellationToken = default)
  {
    try
    {
      return await _httpClient.GetFromJsonAsync<SpotifyAuthUrlDto>($"/api/spotify/auth/url?redirectUri={Uri.EscapeDataString(redirectUri)}", cancellationToken);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to get Spotify auth URL");
      return null;
    }
  }
  
  public async Task LogoutAsync(CancellationToken cancellationToken = default)
  {
    try
    {
      await _httpClient.PostAsync("/api/spotify/auth/logout", null, cancellationToken);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to logout from Spotify");
    }
  }

  public async Task<SpotifySearchResultsDto?> SearchAsync(string query, string? type = null, CancellationToken cancellationToken = default)
  {
    try
    {
      var typeParam = !string.IsNullOrEmpty(type) ? $"&type={type}" : "";
      return await _httpClient.GetFromJsonAsync<SpotifySearchResultsDto>($"/api/spotify/search?q={Uri.EscapeDataString(query)}{typeParam}", cancellationToken);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to search Spotify");
      return null;
    }
  }
  
  public async Task<SpotifySearchResultsDto?> SearchAsync(string query, List<string> types, CancellationToken cancellationToken = default)
  {
    try
    {
      var typeParam = types.Any() ? $"&type={string.Join(",", types)}" : "";
      return await _httpClient.GetFromJsonAsync<SpotifySearchResultsDto>($"/api/spotify/search?q={Uri.EscapeDataString(query)}{typeParam}", cancellationToken);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to search Spotify");
      return null;
    }
  }

  public async Task<List<SpotifyCategoryDto>?> GetCategoriesAsync(CancellationToken cancellationToken = default)
  {
    try
    {
      return await _httpClient.GetFromJsonAsync<List<SpotifyCategoryDto>>("/api/spotify/categories", cancellationToken);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to get Spotify categories");
      return null;
    }
  }

  public async Task<List<SpotifyPlaylistDto>?> GetCategoryPlaylistsAsync(string categoryId, CancellationToken cancellationToken = default)
  {
    try
    {
      return await _httpClient.GetFromJsonAsync<List<SpotifyPlaylistDto>>($"/api/spotify/categories/{categoryId}/playlists", cancellationToken);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to get category playlists");
      return null;
    }
  }

  public async Task<SpotifyPlaylistDto?> GetPlaylistAsync(string playlistId, CancellationToken cancellationToken = default)
  {
    try
    {
      return await _httpClient.GetFromJsonAsync<SpotifyPlaylistDto>($"/api/spotify/playlists/{playlistId}", cancellationToken);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to get playlist");
      return null;
    }
  }

  public async Task<bool> PlayTrackAsync(string trackId, CancellationToken cancellationToken = default)
  {
    try
    {
      var response = await _httpClient.PostAsync($"/api/spotify/play/{trackId}", null, cancellationToken);
      return response.IsSuccessStatusCode;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to play Spotify track");
      return false;
    }
  }

  public async Task<bool> PlayPlaylistAsync(string playlistId, CancellationToken cancellationToken = default)
  {
    try
    {
      var response = await _httpClient.PostAsync($"/api/spotify/playlists/{playlistId}/play", null, cancellationToken);
      return response.IsSuccessStatusCode;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to play Spotify playlist");
      return false;
    }
  }

  public async Task<bool> AddTrackToQueueAsync(string trackId, CancellationToken cancellationToken = default)
  {
    try
    {
      var response = await _httpClient.PostAsync($"/api/spotify/queue/{trackId}", null, cancellationToken);
      return response.IsSuccessStatusCode;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to add Spotify track to queue");
      return false;
    }
  }
  
  public async Task<bool> PlayUriAsync(string uri, string? contextUri = null, CancellationToken cancellationToken = default)
  {
    try
    {
      var body = new { uri, contextUri };
      var response = await _httpClient.PostAsJsonAsync("/api/spotify/play", body, cancellationToken);
      return response.IsSuccessStatusCode;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to play Spotify URI");
      return false;
    }
  }

  public async Task<SpotifyUserDto?> GetCurrentUserAsync(CancellationToken cancellationToken = default)
  {
    try
    {
      return await _httpClient.GetFromJsonAsync<SpotifyUserDto>("/api/spotify/me", cancellationToken);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to get Spotify user");
      return null;
    }
  }
}
