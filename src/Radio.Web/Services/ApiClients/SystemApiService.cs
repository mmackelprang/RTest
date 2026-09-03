using System.Net.Http.Json;
using System.Text.Json;
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
  private static readonly JsonSerializerOptions JsonOptions = new()
  {
    PropertyNameCaseInsensitive = true
  };

  public SystemApiService(HttpClient httpClient, ILogger<SystemApiService> logger)
  {
    _httpClient = httpClient;
    _logger = logger;
  }

  public async Task<SystemStatsDto?> GetSystemStatsAsync(CancellationToken cancellationToken = default)
  {
    try
    {
      return await _httpClient.GetFromJsonAsync<SystemStatsDto>("/api/system/stats", JsonOptions, cancellationToken);
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
      {
        queryParams.Add($"level={level}");
      }

      if (limit.HasValue)
      {
        queryParams.Add($"limit={limit.Value}");
      }

      if (maxAgeMinutes.HasValue)
      {
        queryParams.Add($"maxAgeMinutes={maxAgeMinutes.Value}");
      }

      var query = queryParams.Count > 0 ? "?" + string.Join("&", queryParams) : "";
      return await _httpClient.GetFromJsonAsync<SystemLogsResponse>($"/api/system/logs{query}", JsonOptions, cancellationToken);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to get system logs");
      return null;
    }
  }

  public async Task<bool> SetSleepAsync(bool sleep, CancellationToken cancellationToken = default)
  {
    try
    {
      var response = await _httpClient.PostAsJsonAsync("/api/system/sleep", new { sleep }, cancellationToken);
      return response.IsSuccessStatusCode;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to set sleep state to {Sleep}", sleep);
      return false;
    }
  }

  /// <summary>
  /// Reports whether the <c>/sleep</c> route is on screen, and returns the resulting state.
  /// </summary>
  /// <remarks>
  /// Returns <c>null</c> on any failure, and every caller must render correctly from that: the
  /// bUnit rig fails every outbound request by design, and the kiosk can call this while the API is
  /// still starting. Failing means the caller keeps its default, which is the Ambient copy.
  /// </remarks>
  public async Task<SleepStateDto?> SetSleepScreenVisibleAsync(
    bool visible,
    CancellationToken cancellationToken = default)
  {
    try
    {
      var response = await _httpClient.PostAsJsonAsync(
        "/api/system/sleep-screen", new { visible }, cancellationToken);
      if (!response.IsSuccessStatusCode)
      {
        return null;
      }

      return await response.Content.ReadFromJsonAsync<SleepStateDto>(JsonOptions, cancellationToken);
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Failed to report sleep screen visibility {Visible}", visible);
      return null;
    }
  }

  public async Task<bool> PowerOffSystemAsync(CancellationToken cancellationToken = default)
  {
    try
    {
      var response = await _httpClient.PostAsync("/api/system/poweroff", null, cancellationToken);
      return response.IsSuccessStatusCode;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to power off system");
      return false;
    }
  }

  public async Task<bool> RestartServicesAsync(CancellationToken cancellationToken = default)
  {
    try
    {
      var response = await _httpClient.PostAsync("/api/system/restart-services", null, cancellationToken);
      return response.IsSuccessStatusCode;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to restart services");
      return false;
    }
  }
}
