namespace Radio.Core.Configuration;

/// <summary>
/// Configuration for the sleep-screen weather forecast (ADR-022 §2.5).
/// Bound from the <c>Display:Weather</c> section of the config tree.
/// Lives under the new <c>Display:*</c> namespace (alongside PR #1's
/// <c>Display:TimeFormat</c> / <c>Display:ShowSeconds</c>) rather than under
/// <c>AudioOutput:DeviceDisplay</c> because weather has nothing to do with
/// audio output — see ADR §5.6 for the namespace rationale.
///
/// Consumed via <see cref="Microsoft.Extensions.Options.IOptionsMonitor{TOptions}"/>
/// so changes saved on the System Configuration page take effect on the next
/// forecast fetch without a service restart (same pattern as
/// <c>FingerprintingOptions</c> consumers — see "Config Bridge" in MEMORY).
/// </summary>
public sealed class WeatherDisplayOptions
{
  /// <summary>
  /// The configuration section name. Bind with
  /// <c>builder.Services.Configure&lt;WeatherDisplayOptions&gt;(builder.Configuration.GetSection(WeatherDisplayOptions.SectionName))</c>.
  /// </summary>
  public const string SectionName = "Display:Weather";

  /// <summary>
  /// Master switch. When <c>false</c>, the sleep screen never shows the
  /// forecast pane and the API treats forecast requests as 404. Defaults to
  /// <c>true</c> so the feature lights up on first deploy without manual
  /// configuration (ADR §6 rollout).
  /// </summary>
  public bool Enabled { get; set; } = true;

  /// <summary>
  /// 5-digit US ZIP code the forecast is fetched for. Defaults to
  /// <c>"27312"</c> (Pittsboro, NC) — the user's home location.
  /// </summary>
  public string Zip { get; set; } = "27312";

  /// <summary>
  /// Forecast cache fresh-TTL in minutes. NWS updates roughly hourly, so
  /// anything less aggressive than 60 minutes is wasted bandwidth. Valid
  /// range: 15..360 (enforced client-side in the Settings UI; the service
  /// also clamps to this range defensively).
  /// </summary>
  public int RefreshIntervalMinutes { get; set; } = 60;

  /// <summary>
  /// <c>"F"</c> for Fahrenheit, <c>"C"</c> for Celsius, or <c>"both"</c> to
  /// display both side-by-side. Selects which of the per-day temperature
  /// fields the UI renders. The data model carries both so this toggle never
  /// triggers a re-fetch.
  /// </summary>
  public string TemperatureUnit { get; set; } = "F";

  /// <summary>
  /// Contact email used in the NWS <c>User-Agent</c> header per their policy
  /// (ADR §2.6 / §10 Q3). NWS allows anonymous traffic but reserves the right
  /// to rate-limit it — set this to a real address when configuring a
  /// long-term deployment.
  ///
  /// Empty string defaults to <c>radioconsole@localhost.local</c> at the
  /// HttpClient layer so the feature works out of the box.
  /// </summary>
  public string ContactEmail { get; set; } = string.Empty;
}
