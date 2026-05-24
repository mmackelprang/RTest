using System.Text.Json.Serialization;

namespace Radio.Infrastructure.Weather.Dtos;

/// <summary>
/// Subset of the NWS forecast endpoint response. We only bind the fields we
/// render or use for cache freshness.
/// </summary>
internal sealed class NwsForecastResponse
{
  [JsonPropertyName("properties")]
  public NwsForecastProperties? Properties { get; set; }
}

internal sealed class NwsForecastProperties
{
  /// <summary>
  /// When the upstream generator produced this forecast. Used as
  /// <c>WeatherForecast.GeneratedAtUtc</c>; the UI shows it as "as of HH:mm".
  /// </summary>
  [JsonPropertyName("generatedAt")]
  public DateTimeOffset? GeneratedAt { get; set; }

  /// <summary>
  /// Up to 14 day/night periods covering the next 7 days. We aggregate
  /// day+night pairs into single calendar-day WeatherDay records.
  /// </summary>
  [JsonPropertyName("periods")]
  public List<NwsForecastPeriod>? Periods { get; set; }
}

/// <summary>
/// One NWS forecast period (typically half-day: "Today" / "Tonight" / "Monday" /
/// "Monday Night" / …). NWS guarantees temperatureUnit is always "F" for the
/// US grid.
/// </summary>
internal sealed class NwsForecastPeriod
{
  /// <summary>Sequential index NWS assigns to each period.</summary>
  [JsonPropertyName("number")]
  public int Number { get; set; }

  /// <summary>Period name — e.g. "Today", "Tonight", "Monday", "Monday Night".</summary>
  [JsonPropertyName("name")]
  public string? Name { get; set; }

  /// <summary>Period start (local wall-clock for the grid).</summary>
  [JsonPropertyName("startTime")]
  public DateTimeOffset? StartTime { get; set; }

  /// <summary>Period end.</summary>
  [JsonPropertyName("endTime")]
  public DateTimeOffset? EndTime { get; set; }

  /// <summary>
  /// True for the daytime period (typically 06:00 → 18:00 local). When false
  /// this is the matching overnight period.
  /// </summary>
  [JsonPropertyName("isDaytime")]
  public bool IsDaytime { get; set; }

  /// <summary>Temperature value. Unit is in <see cref="TemperatureUnit"/>.</summary>
  [JsonPropertyName("temperature")]
  public int Temperature { get; set; }

  /// <summary>"F" or "C". For US grids this is always "F".</summary>
  [JsonPropertyName("temperatureUnit")]
  public string? TemperatureUnit { get; set; }

  /// <summary>
  /// Chance-of-precipitation value object — may be null when NWS didn't
  /// estimate one for this period.
  /// </summary>
  [JsonPropertyName("probabilityOfPrecipitation")]
  public NwsValueObject? ProbabilityOfPrecipitation { get; set; }

  /// <summary>Short condition label — e.g. "Sunny", "PM Showers".</summary>
  [JsonPropertyName("shortForecast")]
  public string? ShortForecast { get; set; }

  /// <summary>Verbose narrative.</summary>
  [JsonPropertyName("detailedForecast")]
  public string? DetailedForecast { get; set; }

  /// <summary>Icon URL — fed to <c>NwsIconMapper.MapToIconKey</c>.</summary>
  [JsonPropertyName("icon")]
  public string? Icon { get; set; }
}

/// <summary>
/// NWS wraps several scalar fields in a <c>{unitCode, value}</c> object.
/// </summary>
internal sealed class NwsValueObject
{
  [JsonPropertyName("value")]
  public int? Value { get; set; }
}
