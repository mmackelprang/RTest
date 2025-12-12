using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Radio.Web.Models;

namespace Radio.Web.Services.ApiClients;

/// <summary>
/// API client service for system management endpoints (2 endpoints)
/// </summary>
public class SystemApiService
{
  private readonly HttpClient _httpClient;
  private readonly ILogger<SystemApiService> _logger;

  public SystemApiService(HttpClient httpClient, ILogger<SystemApiService> logger)
  {
    _httpClient = httpClient;
    _logger = logger;
  }

  public async Task<SystemStatsDto?> GetSystemStatsAsync(CancellationToken cancellationToken = default)
  {
    try
    {
      return await _httpClient.GetFromJsonAsync<SystemStatsDto>("/api/system/stats", cancellationToken);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to get system stats");
      return null;
    }
  }

  public async Task<SystemLogsResponse?> GetSystemLogsAsync(string? level = "warning", int? limit = 100, int? maxAgeMinutes = null, CancellationToken cancellationToken = default)
  {
    try
    {
      var queryParams = new List<string>();
      if (!string.IsNullOrEmpty(level))
        queryParams.Add($"level={level}");
      if (limit.HasValue)
        queryParams.Add($"limit={limit.Value}");
      if (maxAgeMinutes.HasValue)
        queryParams.Add($"maxAgeMinutes={maxAgeMinutes.Value}");

      var query = queryParams.Count > 0 ? "?" + string.Join("&", queryParams) : "";
      return await _httpClient.GetFromJsonAsync<SystemLogsResponse>($"/api/system/logs{query}", cancellationToken);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to get system logs");
      return null;
    }
  }
}
