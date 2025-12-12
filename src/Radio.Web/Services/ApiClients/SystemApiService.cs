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
}
