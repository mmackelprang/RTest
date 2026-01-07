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
      
      return await _httpClient.GetFromJsonAsync<PlayHistoryListDto>($"/api/playhistory{queryString}", cancellationToken);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to get play history");
      return null;
    }
  }

  public async Task<PlayHistoryEntryDto?> GetHistoryItemAsync(string id, CancellationToken cancellationToken = default)
  {
    try
    {
      return await _httpClient.GetFromJsonAsync<PlayHistoryEntryDto>($"/api/playhistory/{id}", cancellationToken);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to get history item {Id}", id);
      return null;
    }
  }

  public async Task<PlayHistoryListDto?> GetHistoryBySourceAsync(string source, int? limit = null, int? offset = null, CancellationToken cancellationToken = default)
  {
    try
    {
      // For "All" source, use the base endpoint directly (more reliable)
      if (string.Equals(source?.Trim(), "All", StringComparison.OrdinalIgnoreCase))
      {
        return await GetHistoryAsync(limit, offset, cancellationToken);
      }

      var query = new List<string>();
      if (limit.HasValue) query.Add($"limit={limit}");
      if (offset.HasValue) query.Add($"offset={offset}");
      var queryString = query.Any() ? "?" + string.Join("&", query) : "";

      var response = await _httpClient.GetAsync($"/api/playhistory/source/{Uri.EscapeDataString(source ?? "")}{queryString}", cancellationToken);

      // Handle 400 for invalid source types gracefully
      if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
      {
        _logger.LogWarning("Invalid source type: {Source}", source);
        return null;
      }

      response.EnsureSuccessStatusCode();
      return await response.Content.ReadFromJsonAsync<PlayHistoryListDto>(cancellationToken);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to get history by source {Source}", source);
      return null;
    }
  }

  public async Task<PlayHistoryListDto?> GetHistoryByDateAsync(DateTime date, int? limit = null, int? offset = null, CancellationToken cancellationToken = default)
  {
    try
    {
      var dateStr = date.ToString("yyyy-MM-dd");
      var query = new List<string>();
      if (limit.HasValue) query.Add($"limit={limit}");
      if (offset.HasValue) query.Add($"offset={offset}");
      var queryString = query.Any() ? "?" + string.Join("&", query) : "";

      return await _httpClient.GetFromJsonAsync<PlayHistoryListDto>($"/api/playhistory/date/{dateStr}{queryString}", cancellationToken);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to get history by date {Date}", date);
      return null;
    }
  }

  public async Task<PlayHistoryListDto?> SearchHistoryAsync(string query, int? limit = null, int? offset = null, CancellationToken cancellationToken = default)
  {
    try
    {
      var queryParams = new List<string> { $"q={Uri.EscapeDataString(query)}" };
      if (limit.HasValue) queryParams.Add($"limit={limit}");
      if (offset.HasValue) queryParams.Add($"offset={offset}");
      var queryString = "?" + string.Join("&", queryParams);

      return await _httpClient.GetFromJsonAsync<PlayHistoryListDto>($"/api/playhistory/search{queryString}", cancellationToken);
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
      return await _httpClient.GetFromJsonAsync<PlayHistoryStatsDto>("/api/playhistory/statistics", cancellationToken);
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
      var response = await _httpClient.DeleteAsync("/api/playhistory", cancellationToken);
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
      var response = await _httpClient.DeleteAsync($"/api/playhistory/{id}", cancellationToken);
      return response.IsSuccessStatusCode;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to delete history item {Id}", id);
      return false;
    }
  }
}
