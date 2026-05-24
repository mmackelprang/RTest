using System.Text.Json.Serialization;

namespace Radio.Infrastructure.Weather.Dtos;

/// <summary>
/// Subset of the <c>GET /stations/{id}/observations/latest</c> response shape
/// we consume. Fields are mapped into the public
/// <see cref="Radio.Core.Models.CurrentObservation"/> record by
/// <see cref="NwsWeatherService"/>.
/// </summary>
internal sealed class NwsObservationResponse
{
  [JsonPropertyName("properties")]
  public NwsObservationProperties? Properties { get; set; }
}

internal sealed class NwsObservationProperties
{
  /// <summary>ISO-8601 timestamp the observation was taken by the station.</summary>
  [JsonPropertyName("timestamp")]
  public DateTimeOffset? Timestamp { get; set; }

  /// <summary>Short human label (e.g. "Partly Cloudy"). NWS labels are
  /// already capitalized.</summary>
  [JsonPropertyName("textDescription")]
  public string? TextDescription { get; set; }

  /// <summary>NWS icon URL — same shape as forecast period icons; mapped via
  /// <see cref="NwsIconMapper.MapToIconKey"/>.</summary>
  [JsonPropertyName("icon")]
  public string? Icon { get; set; }

  /// <summary>Temperature value + unitCode (typically <c>wmoUnit:degC</c>).</summary>
  [JsonPropertyName("temperature")]
  public NwsObservationValue? Temperature { get; set; }
}

internal sealed class NwsObservationValue
{
  [JsonPropertyName("value")]
  public double? Value { get; set; }

  [JsonPropertyName("unitCode")]
  public string? UnitCode { get; set; }
}
