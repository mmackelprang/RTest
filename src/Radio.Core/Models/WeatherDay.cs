namespace Radio.Core.Models;

/// <summary>
/// One day of the forecast. Temperatures are stored in both units so the UI can
/// switch without a round-trip; downstream callers pick the field that matches
/// their configured temperature unit.
/// </summary>
/// <param name="Date">Calendar date this forecast covers, in the kiosk's local time.</param>
/// <param name="DayName">
/// Display label per the spec: <c>"Today"</c>, <c>"Tomorrow"</c>, or a
/// 3-letter weekday abbreviation (<c>"Mon"</c>, <c>"Tue"</c>, etc.) for days
/// further out. Computed by <c>NwsWeatherService</c> using invariant culture so
/// the kiosk renders English regardless of host locale (matches PR #1's
/// invariant-culture convention).
/// </param>
/// <param name="HighF">Daytime high, degrees Fahrenheit, rounded to the nearest integer.</param>
/// <param name="LowF">Overnight low, degrees Fahrenheit, rounded to the nearest integer.</param>
/// <param name="HighC">Daytime high, degrees Celsius, rounded to the nearest integer.</param>
/// <param name="LowC">Overnight low, degrees Celsius, rounded to the nearest integer.</param>
/// <param name="ConditionShort">Short condition label (e.g. <c>"Sunny"</c>, <c>"PM Showers"</c>).</param>
/// <param name="ConditionLong">Verbose NWS narrative (e.g. <c>"Sunny, with a high near 72…"</c>).</param>
/// <param name="PrecipitationProbabilityPct">Chance of precipitation, 0..100. Zero when NWS reports null.</param>
/// <param name="IconKey">
/// Stable identifier (e.g. <c>"sunny"</c>, <c>"partly-cloudy"</c>, <c>"rain"</c>).
/// NOT a Material/MDI icon name and NOT an NWS URL — the Web layer maps
/// <see cref="IconKey"/> to a concrete Material Symbol so Designer can swap the
/// icon set without an API change.
/// </param>
/// <param name="NwsForecastUrl">
/// Optional debug link to the NWS source period. May be <c>null</c> when the
/// upstream payload didn't carry one.
/// </param>
public sealed record WeatherDay(
  DateOnly Date,
  string DayName,
  int HighF,
  int LowF,
  int HighC,
  int LowC,
  string ConditionShort,
  string ConditionLong,
  int PrecipitationProbabilityPct,
  string IconKey,
  string? NwsForecastUrl);
