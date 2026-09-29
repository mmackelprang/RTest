using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Radio.Web.Models;

namespace Radio.Web.Services.ApiClients;

/// <summary>
/// API client service for playlist management endpoints.
/// </summary>
public class PlaylistApiService
{
  private readonly HttpClient _httpClient;
  private readonly ILogger<PlaylistApiService> _logger;

  public PlaylistApiService(HttpClient httpClient, ILogger<PlaylistApiService> logger)
  {
    _httpClient = httpClient;
    _logger = logger;
  }

  public async Task<List<PlaylistSummaryDto>?> GetAllAsync(CancellationToken ct = default)
  {
    try
    {
      return await _httpClient.GetFromJsonAsync<List<PlaylistSummaryDto>>("/api/playlists", ct);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to get playlists");
      return null;
    }
  }

  public async Task<PlaylistDetailDto?> GetByIdAsync(string id, CancellationToken ct = default)
  {
    try
    {
      return await _httpClient.GetFromJsonAsync<PlaylistDetailDto>($"/api/playlists/{id}", ct);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to get playlist {Id}", id);
      return null;
    }
  }

  public async Task<PlaylistSummaryDto?> CreateAsync(string name, string? description, CancellationToken ct = default)
  {
    try
    {
      var response = await _httpClient.PostAsJsonAsync("/api/playlists",
        new { name, description }, ct);
      if (response.IsSuccessStatusCode)
      {
        return await response.Content.ReadFromJsonAsync<PlaylistSummaryDto>(ct);
      }
      var error = await response.Content.ReadAsStringAsync(ct);
      _logger.LogWarning("Failed to create playlist: {Status} {Error}", response.StatusCode, error);
      return null;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to create playlist");
      return null;
    }
  }

  public async Task<bool> DeleteAsync(string id, CancellationToken ct = default)
  {
    try
    {
      var response = await _httpClient.DeleteAsync($"/api/playlists/{id}", ct);
      return response.IsSuccessStatusCode;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to delete playlist {Id}", id);
      return false;
    }
  }

  /// <summary>
  /// Loads a playlist into the queue. Returns the server's loaded/skipped counts, or null when the request failed.
  /// </summary>
  /// <remarks>
  /// The counts are the point: a playlist can be "loaded" with every file missing, and the caller must report what
  /// reached the queue, not the playlist's saved size. <see cref="OperationCanceledException"/> propagates so the
  /// caller can tell a timeout from a failure.
  /// </remarks>
  public async Task<PlaylistLoadResultDto?> LoadAsync(string id, CancellationToken ct = default)
  {
    try
    {
      var response = await _httpClient.PostAsync($"/api/playlists/{id}/load", null, ct);
      if (!response.IsSuccessStatusCode)
      {
        return null;
      }

      return await response.Content.ReadFromJsonAsync<PlaylistLoadResultDto>(ct) ?? new PlaylistLoadResultDto();
    }
    catch (OperationCanceledException)
    {
      throw;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to load playlist {Id}", id);
      return null;
    }
  }
}
