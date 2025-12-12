using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Radio.Web.Models;

namespace Radio.Web.Services.ApiClients;

/// <summary>
/// API client service for metrics and observability endpoints (5 endpoints)
/// </summary>
public class MetricsApiService
{
  private readonly HttpClient _httpClient;
  private readonly ILogger<MetricsApiService> _logger;

  public MetricsApiService(HttpClient httpClient, ILogger<MetricsApiService> logger)
  {
    _httpClient = httpClient;
    _logger = logger;
  }

  public async Task<List<MetricDto>?> GetAllMetricsAsync(CancellationToken cancellationToken = default)
  {
    try
    {
      return await _httpClient.GetFromJsonAsync<List<MetricDto>>("/api/metrics", cancellationToken);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to get all metrics");
      return null;
    }
  }

  public async Task<MetricDto?> GetMetricAsync(string metricName, CancellationToken cancellationToken = default)
  {
    try
    {
      return await _httpClient.GetFromJsonAsync<MetricDto>($"/api/metrics/{metricName}", cancellationToken);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to get metric {MetricName}", metricName);
      return null;
    }
  }

  public async Task<List<MetricHistoryDto>?> GetMetricHistoryAsync(string metricName, string timeRange, CancellationToken cancellationToken = default)
  {
    try
    {
      return await _httpClient.GetFromJsonAsync<List<MetricHistoryDto>>($"/api/metrics/{metricName}/history?range={timeRange}", cancellationToken);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to get metric history for {MetricName}", metricName);
      return null;
    }
  }

  public async Task<Dictionary<string, double>?> GetMetricsSummaryAsync(CancellationToken cancellationToken = default)
  {
    try
    {
      return await _httpClient.GetFromJsonAsync<Dictionary<string, double>>("/api/metrics/summary", cancellationToken);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to get metrics summary");
      return null;
    }
  }

  public async Task<bool> ResetMetricAsync(string metricName, CancellationToken cancellationToken = default)
  {
    try
    {
      var response = await _httpClient.DeleteAsync($"/api/metrics/{metricName}", cancellationToken);
      return response.IsSuccessStatusCode;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to reset metric {MetricName}", metricName);
      return false;
    }
  }
}
