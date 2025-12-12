using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Radio.Web.Models;

namespace Radio.Web.Services.ApiClients;

/// <summary>
/// API client service for play history endpoints (8 endpoints)
/// </summary>
public class PlayHistoryApiService
{
  private readonly HttpClient _httpClient;
  private readonly ILogger<PlayHistoryApiService> _logger;

  public PlayHistoryApiService(HttpClient httpClient, ILogger<PlayHistoryApiService> logger)
  {
    _httpClient = httpClient;
    _logger = logger;
  }

  public async Task<PlayHistoryListDto?> GetHistoryAsync(int? limit = null, int? offset = null, CancellationToken cancellationToken = default)
  {
    try
    {
      var query = new List<string>();
      if (limit.HasValue) query.Add($"limit={limit}");
      if (offset.HasValue) query.Add($"offset={offset}");
      var queryString = query.Any() ? "?" + string.Join("&", query) : "";
      
      return await _httpClient.GetFromJsonAsync<PlayHistoryListDto>($"/api/history{queryString}", cancellationToken);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to get play history");
      return null;
    }
  }

  public async Task<PlayHistoryItemDto?> GetHistoryItemAsync(string id, CancellationToken cancellationToken = default)
  {
    try
    {
      return await _httpClient.GetFromJsonAsync<PlayHistoryItemDto>($"/api/history/{id}", cancellationToken);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to get history item {Id}", id);
      return null;
    }
  }

  public async Task<PlayHistoryListDto?> GetHistoryBySourceAsync(string source, int? limit = null, CancellationToken cancellationToken = default)
  {
    try
    {
      var queryString = limit.HasValue ? $"?limit={limit}" : "";
      return await _httpClient.GetFromJsonAsync<PlayHistoryListDto>($"/api/history/source/{source}{queryString}", cancellationToken);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to get history by source {Source}", source);
      return null;
    }
  }

  public async Task<PlayHistoryListDto?> GetHistoryByDateAsync(DateTime date, CancellationToken cancellationToken = default)
  {
    try
    {
      var dateStr = date.ToString("yyyy-MM-dd");
      return await _httpClient.GetFromJsonAsync<PlayHistoryListDto>($"/api/history/date/{dateStr}", cancellationToken);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to get history by date {Date}", date);
      return null;
    }
  }

  public async Task<PlayHistoryListDto?> SearchHistoryAsync(string query, CancellationToken cancellationToken = default)
  {
    try
    {
      return await _httpClient.GetFromJsonAsync<PlayHistoryListDto>($"/api/history/search?q={Uri.EscapeDataString(query)}", cancellationToken);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to search history");
      return null;
    }
  }

  public async Task<PlayHistoryStatsDto?> GetStatsAsync(CancellationToken cancellationToken = default)
  {
    try
    {
      return await _httpClient.GetFromJsonAsync<PlayHistoryStatsDto>("/api/history/stats", cancellationToken);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to get history stats");
      return null;
    }
  }

  public async Task<bool> ClearHistoryAsync(CancellationToken cancellationToken = default)
  {
    try
    {
      var response = await _httpClient.DeleteAsync("/api/history", cancellationToken);
      return response.IsSuccessStatusCode;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to clear history");
      return false;
    }
  }

  public async Task<bool> DeleteHistoryItemAsync(string id, CancellationToken cancellationToken = default)
  {
    try
    {
      var response = await _httpClient.DeleteAsync($"/api/history/{id}", cancellationToken);
      return response.IsSuccessStatusCode;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to delete history item {Id}", id);
      return false;
    }
  }
}
