using System.Net.Http.Json;
using System.Web;
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

  /// <summary>
  /// GET /api/metrics/history - Gets historical time-series data for a metric
  /// </summary>
  public async Task<List<MetricHistoryDto>?> GetMetricHistoryAsync(
    string key, 
    DateTime start, 
    DateTime end, 
    string? resolution = "Minute", 
    CancellationToken cancellationToken = default)
  {
    try
    {
      var query = $"?key={HttpUtility.UrlEncode(key)}" +
                  $"&start={HttpUtility.UrlEncode(start.ToString("o"))}" +
                  $"&end={HttpUtility.UrlEncode(end.ToString("o"))}";
      
      if (!string.IsNullOrEmpty(resolution))
        query += $"&resolution={resolution}";

      return await _httpClient.GetFromJsonAsync<List<MetricHistoryDto>>($"/api/metrics/history{query}", cancellationToken);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to get metric history for {Key}", key);
      return null;
    }
  }

  /// <summary>
  /// GET /api/metrics/snapshots - Gets current/aggregate values for one or more metrics
  /// </summary>
  public async Task<Dictionary<string, double>?> GetMetricSnapshotsAsync(
    List<string> keys, 
    CancellationToken cancellationToken = default)
  {
    try
    {
      var keysParam = string.Join(",", keys.Select(HttpUtility.UrlEncode));
      return await _httpClient.GetFromJsonAsync<Dictionary<string, double>>($"/api/metrics/snapshots?keys={keysParam}", cancellationToken);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to get metric snapshots");
      return null;
    }
  }

  /// <summary>
  /// GET /api/metrics/aggregate - Gets aggregate statistics for a metric over a time range
  /// </summary>
  public async Task<MetricAggregateDto?> GetMetricAggregateAsync(
    string key, 
    DateTime start, 
    DateTime end, 
    CancellationToken cancellationToken = default)
  {
    try
    {
      var query = $"?key={HttpUtility.UrlEncode(key)}" +
                  $"&start={HttpUtility.UrlEncode(start.ToString("o"))}" +
                  $"&end={HttpUtility.UrlEncode(end.ToString("o"))}";

      return await _httpClient.GetFromJsonAsync<MetricAggregateDto>($"/api/metrics/aggregate{query}", cancellationToken);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to get metric aggregate for {Key}", key);
      return null;
    }
  }

  /// <summary>
  /// GET /api/metrics/keys - Gets all available metric keys
  /// </summary>
  public async Task<List<string>?> GetMetricKeysAsync(CancellationToken cancellationToken = default)
  {
    try
    {
      return await _httpClient.GetFromJsonAsync<List<string>>("/api/metrics/keys", cancellationToken);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to get metric keys");
      return null;
    }
  }

  /// <summary>
  /// POST /api/metrics/event - Records a UI event metric from the frontend
  /// </summary>
  public async Task<MetricEventResponse?> RecordUIEventAsync(
    string eventName, 
    Dictionary<string, string>? tags = null, 
    CancellationToken cancellationToken = default)
  {
    try
    {
      var request = new MetricEventRequest(eventName, tags);
      var response = await _httpClient.PostAsJsonAsync("/api/metrics/event", request, cancellationToken);
      
      if (response.IsSuccessStatusCode)
        return await response.Content.ReadFromJsonAsync<MetricEventResponse>(cancellationToken: cancellationToken);
      
      return null;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to record UI event {EventName}", eventName);
      return null;
    }
  }
}
