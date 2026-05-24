using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Radio.Core.Models;

namespace Radio.Web.Services.ApiClients;

/// <summary>
/// Typed HttpClient wrapper for <c>/api/weather/forecast</c>. The Sleep page
/// consumes this directly — there is no shared component layer between Web
/// and the controller for weather (ADR §2.4 deliberately chose this for v1).
///
/// Catches all upstream failures and translates them into a single null
/// return so the Sleep page can hide the forecast pane without inspecting
/// HTTP status codes itself. Per ADR §2.7 the controller returns 503 when
/// no forecast can be served — we translate that to null.
/// </summary>
public class WeatherApiService
{
  private readonly HttpClient _httpClient;
  private readonly ILogger<WeatherApiService> _logger;
  private static readonly JsonSerializerOptions JsonOptions = new()
  {
    PropertyNameCaseInsensitive = true,
  };

  public WeatherApiService(HttpClient httpClient, ILogger<WeatherApiService> logger)
  {
    _httpClient = httpClient;
    _logger = logger;
  }

  /// <summary>
  /// Fetches the 3-day forecast for the supplied ZIP (or the server-side
  /// default when zip is null/empty). Returns null on any of:
  ///   * HTTP 404 — feature disabled server-side
  ///   * HTTP 400 — invalid ZIP (logged at Warning so misconfig surfaces)
  ///   * HTTP 503 — upstream failure with no cache available
  ///   * Network / timeout / parse errors
  /// The Sleep page treats null as "hide the forecast pane" per the
  /// load-bearing failure-mode contract in ADR §2.3.
  /// </summary>
  public async Task<WeatherForecast?> GetForecastAsync(string? zip = null, CancellationToken ct = default)
  {
    try
    {
      var url = string.IsNullOrWhiteSpace(zip)
        ? "/api/weather/forecast"
        : $"/api/weather/forecast?zip={Uri.EscapeDataString(zip)}";

      var response = await _httpClient.GetAsync(url, ct).ConfigureAwait(false);

      if (response.StatusCode == HttpStatusCode.NotFound)
      {
        _logger.LogDebug("Weather feature disabled server-side (404)");
        return null;
      }
      if (response.StatusCode == HttpStatusCode.BadRequest)
      {
        _logger.LogWarning("Weather API rejected ZIP as invalid (400): {Zip}", zip);
        return null;
      }
      if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
      {
        _logger.LogInformation("Forecast unavailable for ZIP {Zip} (503)", zip);
        return null;
      }

      response.EnsureSuccessStatusCode();
      return await response.Content.ReadFromJsonAsync<WeatherForecast>(JsonOptions, ct).ConfigureAwait(false);
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested)
    {
      throw;
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Failed to fetch forecast for ZIP {Zip}", zip);
      return null;
    }
  }
}
