using System.Text.Json.Serialization;

namespace Radio.Infrastructure.Weather.Dtos;

/// <summary>
/// Subset of the <c>GET &lt;observationStations&gt;</c> response shape we consume.
/// NWS returns a GeoJSON FeatureCollection ordered by distance from the
/// originating grid point — we take <c>features[0]</c> per HANDOFF
/// §4.4 ("No multi-station fallback").
/// </summary>
internal sealed class NwsStationsResponse
{
  [JsonPropertyName("features")]
  public List<NwsStationFeature>? Features { get; set; }
}

internal sealed class NwsStationFeature
{
  [JsonPropertyName("properties")]
  public NwsStationProperties? Properties { get; set; }
}

internal sealed class NwsStationProperties
{
  /// <summary>4-letter ICAO-style station identifier (e.g. "KIGX").</summary>
  [JsonPropertyName("stationIdentifier")]
  public string? StationIdentifier { get; set; }
}
