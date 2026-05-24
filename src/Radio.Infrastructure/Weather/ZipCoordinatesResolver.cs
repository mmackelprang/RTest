using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Radio.Infrastructure.Weather;

/// <summary>
/// Resolves a US ZIP code to its centroid (lat, lon) and place name.
/// Tries the built-in fallback table first (instant, no network), then falls
/// back to zippopotam.us for ZIPs we don't ship locally.
///
/// Per ADR-022 §2.2 + Designer §10 Q1 the fallback table ships only the
/// default ZIP (27312 / Pittsboro, NC). Other ZIPs require zippopotam.us; if
/// that's unreachable AND the ZIP isn't in the fallback table, the resolver
/// returns <c>null</c> and the calling service surfaces a "no forecast"
/// state to the UI (which then hides the forecast pane).
/// </summary>
public sealed class ZipCoordinatesResolver
{
  private readonly IHttpClientFactory _httpClientFactory;
  private readonly ILogger<ZipCoordinatesResolver> _logger;

  // Static fallback table. Keep small — see ADR §5.5 for why we don't ship
  // the full 41k-ZIP centroid dataset.
  private static readonly IReadOnlyDictionary<string, ZipCoordinates> _fallback =
    new Dictionary<string, ZipCoordinates>(StringComparer.Ordinal)
    {
      ["27312"] = new("27312", 35.7156m, -79.1845m, "Pittsboro", "NC"),
    };

  public ZipCoordinatesResolver(
    IHttpClientFactory httpClientFactory,
    ILogger<ZipCoordinatesResolver> logger)
  {
    _httpClientFactory = httpClientFactory;
    _logger = logger;
  }

  /// <summary>
  /// Resolves a ZIP to coordinates. Tries the fallback table first, then
  /// zippopotam.us. Returns <c>null</c> on any of:
  ///   - ZIP isn't 5 digits
  ///   - Fallback table miss + zippopotam.us 404 (ZIP not in zippopotam's data)
  ///   - Fallback table miss + zippopotam.us unreachable (network down)
  /// Never throws on upstream failure.
  /// </summary>
  public async Task<ZipCoordinates?> ResolveAsync(string zip, CancellationToken ct = default)
  {
    if (!IsValidZip(zip))
    {
      _logger.LogDebug("ZIP {Zip} rejected: not 5 digits", zip);
      return null;
    }

    // Fast path: fallback table. Built-in for cold-start-without-internet
    // scenarios on the user's own kiosk.
    if (_fallback.TryGetValue(zip, out var local))
    {
      return local;
    }

    // Slow path: zippopotam.us. Uses the named "weather-zippopotam" HttpClient
    // configured in WeatherServiceExtensions (User-Agent set there).
    try
    {
      var client = _httpClientFactory.CreateClient("weather-zippopotam");
      var response = await client.GetAsync($"/us/{zip}", ct).ConfigureAwait(false);

      if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
      {
        // Genuine unknown ZIP — return null so callers surface "ZIP not
        // recognized" in the Settings UI.
        _logger.LogInformation("Zippopotam returned 404 for ZIP {Zip}", zip);
        return null;
      }

      response.EnsureSuccessStatusCode();
      var payload = await response.Content.ReadFromJsonAsync<ZippopotamResponse>(ct).ConfigureAwait(false);
      if (payload?.Places is null || payload.Places.Count == 0)
      {
        _logger.LogWarning("Zippopotam returned empty places list for ZIP {Zip}", zip);
        return null;
      }

      var first = payload.Places[0];
      if (!decimal.TryParse(first.Latitude, System.Globalization.CultureInfo.InvariantCulture, out var lat) ||
          !decimal.TryParse(first.Longitude, System.Globalization.CultureInfo.InvariantCulture, out var lon))
      {
        _logger.LogWarning("Zippopotam returned unparseable lat/lon for ZIP {Zip}: {Lat},{Lon}", zip, first.Latitude, first.Longitude);
        return null;
      }

      return new ZipCoordinates(zip, lat, lon, first.PlaceName ?? zip, first.StateAbbreviation ?? string.Empty);
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested)
    {
      throw;
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "ZIP resolution failed for {Zip}; returning null (no fallback for this ZIP)", zip);
      return null;
    }
  }

  /// <summary>True iff the string is exactly 5 ASCII digits.</summary>
  public static bool IsValidZip(string? zip)
  {
    if (string.IsNullOrEmpty(zip) || zip.Length != 5)
    {
      return false;
    }
    foreach (var ch in zip)
    {
      if (ch < '0' || ch > '9')
      {
        return false;
      }
    }
    return true;
  }

  // ------------------------ zippopotam.us response shapes ------------------------
  // Only the fields we actually use are bound; everything else is ignored by
  // System.Text.Json default tolerance.

  private sealed class ZippopotamResponse
  {
    [JsonPropertyName("places")]
    public List<ZippopotamPlace>? Places { get; set; }
  }

  private sealed class ZippopotamPlace
  {
    [JsonPropertyName("place name")]
    public string? PlaceName { get; set; }

    [JsonPropertyName("longitude")]
    public string? Longitude { get; set; }

    [JsonPropertyName("latitude")]
    public string? Latitude { get; set; }

    [JsonPropertyName("state abbreviation")]
    public string? StateAbbreviation { get; set; }
  }
}

/// <summary>
/// Resolved coordinates for a US ZIP code.
/// </summary>
/// <param name="Zip">The ZIP that was resolved.</param>
/// <param name="Latitude">Centroid latitude in decimal degrees.</param>
/// <param name="Longitude">Centroid longitude in decimal degrees.</param>
/// <param name="PlaceName">Place name (e.g. "Pittsboro").</param>
/// <param name="StateAbbreviation">2-letter state code (e.g. "NC"); empty when unknown.</param>
public sealed record ZipCoordinates(
  string Zip,
  decimal Latitude,
  decimal Longitude,
  string PlaceName,
  string StateAbbreviation)
{
  /// <summary>"PlaceName, ST" or just "PlaceName" if state is unknown.</summary>
  public string LocationName =>
    string.IsNullOrEmpty(StateAbbreviation) ? PlaceName : $"{PlaceName}, {StateAbbreviation}";
}
