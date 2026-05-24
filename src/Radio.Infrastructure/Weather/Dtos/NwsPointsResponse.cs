using System.Text.Json.Serialization;

namespace Radio.Infrastructure.Weather.Dtos;

/// <summary>
/// Subset of the <c>GET /points/{lat},{lon}</c> response shape we consume.
/// </summary>
internal sealed class NwsPointsResponse
{
  [JsonPropertyName("properties")]
  public NwsPointsProperties? Properties { get; set; }
}

internal sealed class NwsPointsProperties
{
  /// <summary>URL of the 7-day forecast endpoint for this grid cell.</summary>
  [JsonPropertyName("forecast")]
  public string? Forecast { get; set; }

  /// <summary>
  /// URL of the observation stations list for this grid cell. NWS returns a
  /// GeoJSON FeatureCollection ordered by distance from the grid point;
  /// <see cref="NwsWeatherService"/> picks the closest (features[0]). May be
  /// null when NWS doesn't emit the field for the grid (treated as "no
  /// observation available").
  /// </summary>
  [JsonPropertyName("observationStations")]
  public string? ObservationStations { get; set; }

  [JsonPropertyName("relativeLocation")]
  public NwsRelativeLocation? RelativeLocation { get; set; }
}

internal sealed class NwsRelativeLocation
{
  [JsonPropertyName("properties")]
  public NwsRelativeLocationProperties? Properties { get; set; }
}

internal sealed class NwsRelativeLocationProperties
{
  [JsonPropertyName("city")]
  public string? City { get; set; }

  [JsonPropertyName("state")]
  public string? State { get; set; }
}
