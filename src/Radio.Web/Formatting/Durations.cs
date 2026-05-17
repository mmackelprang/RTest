using System.Globalization;

namespace Radio.Web.Formatting;

/// <summary>
/// Track/queue duration formatting helpers.
/// Stateless static helpers — no DI, no configuration.
/// </summary>
public static class Durations
{
  /// <summary>
  /// Em-dash (U+2014) returned for empty / zero / unknown durations.
  /// </summary>
  private const string Dash = "—";

  /// <summary>
  /// Formats a track-length <see cref="TimeSpan"/> for the now-playing surface
  /// and queue rows.
  /// <list type="bullet">
  ///   <item><description>Below 1 hour: <c>{m}:{ss:00}</c> (e.g. <c>3:00</c>).</description></item>
  ///   <item><description>1 hour or more: <c>{h}:{mm:00}:{ss:00}</c> (e.g. <c>1:02:14</c>).</description></item>
  ///   <item><description>Zero / sub-second values: em-dash.</description></item>
  /// </list>
  /// </summary>
  public static string FormatTrack(TimeSpan t)
  {
    if (t.TotalSeconds < 1.0 || t == TimeSpan.Zero)
    {
      return Dash;
    }

    // Round to whole seconds first so 180.66s → 180s → 3:00 (not 3:01).
    var totalSeconds = (long)Math.Floor(t.TotalSeconds);
    var hours = totalSeconds / 3600;
    var minutes = (totalSeconds % 3600) / 60;
    var seconds = totalSeconds % 60;

    return hours >= 1
      ? string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}:{2:00}", hours, minutes, seconds)
      : string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}", minutes, seconds);
  }

  /// <summary>
  /// Overload accepting a nullable <see cref="TimeSpan"/>. Null returns the em-dash placeholder.
  /// </summary>
  public static string FormatTrack(TimeSpan? t) => t.HasValue ? FormatTrack(t.Value) : Dash;

  /// <summary>
  /// Always-long duration form (<c>h:mm:ss</c>) for queue/playlist totals.
  /// Unlike <see cref="FormatTrack(TimeSpan)"/>, this never collapses to <c>m:ss</c>.
  /// </summary>
  public static string FormatLong(TimeSpan t)
  {
    var totalSeconds = (long)Math.Floor(t.TotalSeconds);
    if (totalSeconds < 0)
    {
      totalSeconds = 0;
    }
    var hours = totalSeconds / 3600;
    var minutes = (totalSeconds % 3600) / 60;
    var seconds = totalSeconds % 60;
    return string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}:{2:00}", hours, minutes, seconds);
  }
}
