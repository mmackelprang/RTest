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
