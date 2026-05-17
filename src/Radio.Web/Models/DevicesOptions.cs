namespace Radio.Web.Models;

/// <summary>
/// Strongly-typed binding for the <c>Devices</c> configuration section.
/// Populated from <c>appsettings.json</c> (or any host-specific override layer)
/// and consumed by components that render device names — the alias map lets
/// operators rewrite gnarly driver strings ("CABLE Input (VB-Audio Virtual Cable)")
/// to friendly forms ("VB-Audio Cable In") without changing the underlying audio device.
/// </summary>
public class DevicesOptions
{
  /// <summary>
  /// Configuration section name. Bind with <c>builder.Services.Configure&lt;DevicesOptions&gt;(builder.Configuration.GetSection(DevicesOptions.SectionName))</c>.
  /// </summary>
  public const string SectionName = "Devices";

  /// <summary>
  /// Whole-string alias map: raw <see cref="AudioDeviceDto.Name"/> → friendly display name.
  /// Keys are matched case-sensitively against the raw device name; values are surfaced
  /// by <see cref="Formatting.DisplayNames.Device"/> with no further cleanup.
  /// Defaults to an empty map so unconfigured deployments fall through to the heuristic.
  /// </summary>
  public Dictionary<string, string> Aliases { get; set; } = new();
}
