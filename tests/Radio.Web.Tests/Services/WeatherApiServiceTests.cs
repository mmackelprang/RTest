using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Radio.Core.Models;
using Radio.Web.Services.ApiClients;
using Radio.Web.Tests.TestHelpers;

namespace Radio.Web.Tests.Services;

/// <summary>
/// Unit tests for <see cref="WeatherApiService"/> focused on the
/// current-conditions iteration of the DTO round-trip per
/// HANDOFF-sleep-weather-current-conditions.md §9.4. The existing v2 API
/// surface (404 / 400 / 503 translation) is exercised end-to-end by the
/// controller tests; here we lock the deserialization of the new
/// <see cref="WeatherForecast.Current"/> nullable field.
/// </summary>
public class WeatherApiServiceTests
{
  [Fact]
  public async Task DeserializesForecast_WithCurrentField()
  {
    // Canned API response with current populated — exercises the
    // System.Text.Json defaults that auto-handle the new nullable field
    // without explicit configuration on WeatherApiService.
    var json = """
      {
        "zip": "27312",
        "locationName": "Pittsboro, NC",
        "generatedAtUtc": "2026-05-24T18:00:00Z",
        "fetchedAtUtc": "2026-05-24T18:01:23Z",
        "isStale": false,
        "days": [
          { "date": "2026-05-24", "dayName": "Today", "highF": 77, "lowF": 66, "highC": 25, "lowC": 19, "conditionShort": "Partly Sunny", "conditionLong": "Partly sunny.", "precipitationProbabilityPct": 20, "iconKey": "partly-cloudy", "nwsForecastUrl": null }
        ],
        "current": {
          "tempF": 48,
          "tempC": 9,
          "conditionShort": "Partly Cloudy",
          "iconKey": "partly-cloudy-night",
          "observedAtUtc": "2026-05-24T17:53:00Z",
          "isStale": false
        }
      }
      """;
    var service = CreateService(HttpStatusCode.OK, json);

    var forecast = await service.GetForecastAsync("27312");

    Assert.NotNull(forecast);
    Assert.NotNull(forecast!.Current);
    Assert.Equal(48, forecast.Current!.TempF);
    Assert.Equal(9, forecast.Current.TempC);
    Assert.Equal("Partly Cloudy", forecast.Current.ConditionShort);
    Assert.Equal("partly-cloudy-night", forecast.Current.IconKey);
    Assert.False(forecast.Current.IsStale);
  }

  [Fact]
  public async Task DeserializesForecast_WithCurrentNull()
  {
    // Canned API response with current: null — confirms the nullable field
    // round-trips cleanly through the typed deserializer.
    var json = """
      {
        "zip": "27312",
        "locationName": "Pittsboro, NC",
        "generatedAtUtc": "2026-05-24T18:00:00Z",
        "fetchedAtUtc": "2026-05-24T18:01:23Z",
        "isStale": false,
        "days": [
          { "date": "2026-05-24", "dayName": "Today", "highF": 77, "lowF": 66, "highC": 25, "lowC": 19, "conditionShort": "Partly Sunny", "conditionLong": "Partly sunny.", "precipitationProbabilityPct": 20, "iconKey": "partly-cloudy", "nwsForecastUrl": null }
        ],
        "current": null
      }
      """;
    var service = CreateService(HttpStatusCode.OK, json);

    var forecast = await service.GetForecastAsync("27312");

    Assert.NotNull(forecast);
    Assert.Null(forecast!.Current);
    Assert.NotEmpty(forecast.Days);
  }

  /// <summary>
  /// Helper — build a WeatherApiService backed by a canned HTTP response.
  /// </summary>
  private static WeatherApiService CreateService(HttpStatusCode status, string jsonBody)
  {
    var handler = new CannedResponseHandler(status, jsonBody);
    var client = new HttpClient(handler)
    {
      BaseAddress = new Uri(HermeticTestRig.ApiBaseUrl),
    };
    return new WeatherApiService(client, NullLogger<WeatherApiService>.Instance);
  }

  /// <summary>
  /// HttpMessageHandler that always returns the same status + body. Avoids
  /// the Moq.Protected dance for these simple deserialization-focused tests.
  /// </summary>
  private sealed class CannedResponseHandler : HttpMessageHandler
  {
    private readonly HttpStatusCode _status;
    private readonly string _body;

    public CannedResponseHandler(HttpStatusCode status, string body)
    {
      _status = status;
      _body = body;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
      return Task.FromResult(new HttpResponseMessage(_status)
      {
        Content = new StringContent(_body, Encoding.UTF8, "application/json"),
      });
    }
  }
}
