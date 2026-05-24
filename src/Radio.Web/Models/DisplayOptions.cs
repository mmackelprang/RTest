namespace Radio.Web.Models;

/// <summary>
/// Strongly-typed binding for the <c>Display</c> configuration section.
/// Controls how the kiosk renders wall-clock text on the topbar Time cluster,
/// the sleep screen, and the queue "ends ~" prediction.
///
/// Populated from <c>appsettings.json</c> defaults and overridden at runtime by
/// values stored in the SQLite configuration store (flattened into .NET's
/// configuration tree by <see cref="Radio.Configuration.Bridge.SqliteConfigurationProvider"/>).
/// Hot-reloads via <see cref="Microsoft.Extensions.Options.IOptionsMonitor{TOptions}"/>
/// when the user saves changes from the System Configuration page — the affected
/// components read <c>CurrentValue</c> on their next per-second tick.
/// </summary>
public class DisplayOptions
{
  /// <summary>
  /// Configuration section name. Bind with <c>builder.Services.Configure&lt;DisplayOptions&gt;(builder.Configuration.GetSection(DisplayOptions.SectionName))</c>.
  /// </summary>
  public const string SectionName = "Display";

  /// <summary>
  /// Wall-clock time format. Currently only <c>"12h"</c> (3:45 PM) and <c>"24h"</c> (15:45)
  /// are recognised; any other value falls back to 24-hour formatting. String rather than
  /// enum so future variants (e.g. <c>"12h-no-suffix"</c>) can be added without an API break.
  /// Defaults to <c>"24h"</c> — matches the historical hardcoded <c>HH:mm</c> behaviour so
  /// existing kiosks keep their current appearance on first deploy.
  /// </summary>
  public string TimeFormat { get; set; } = "24h";

  /// <summary>
  /// Whether wall clocks render a seconds component (e.g. <c>15:45:22</c> / <c>3:45:22 PM</c>).
  /// Defaults to <c>false</c> to keep the sleep screen visually calm and the topbar uncluttered.
  /// Honored by the topbar clock and the sleep clock; the queue "ends ~" prediction always
  /// suppresses seconds regardless of this setting (forward-looking estimate, not a clock).
  /// </summary>
  public bool ShowSeconds { get; set; } = false;
}
