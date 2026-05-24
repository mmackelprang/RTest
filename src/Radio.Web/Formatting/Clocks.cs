using System.Globalization;
using Radio.Web.Models;

namespace Radio.Web.Formatting;

/// <summary>
/// Wall-clock formatting helpers. Single source of truth so the three wall-clock
/// surfaces (topbar Time cluster, sleep screen, queue "ends ~" prediction) stay
/// in lock-step as the <see cref="DisplayOptions"/> setting flips between 12h /
/// 24h and seconds on / off.
///
/// All renderings use <see cref="CultureInfo.InvariantCulture"/> so the AM/PM
/// glyph is always uppercase English (<c>AM</c>/<c>PM</c>) regardless of host
/// locale — matches the existing <see cref="Timestamps"/> convention and keeps
/// the LED font (Orbitron) rendering predictable.
/// </summary>
public static class Clocks
{
  /// <summary>
  /// Formats a local wall-clock time per the supplied <see cref="DisplayOptions"/>.
  /// </summary>
  /// <param name="local">The local <see cref="DateTime"/> to render.</param>
  /// <param name="opts">
  /// Display preferences. <c>null</c> is treated as the default 24-hour, no-seconds
  /// behaviour so callers that haven't wired up <see cref="Microsoft.Extensions.Options.IOptionsMonitor{TOptions}"/>
  /// don't silently crash.
  /// </param>
  /// <param name="allowSeconds">
  /// When <c>false</c>, the seconds component is suppressed even if
  /// <see cref="DisplayOptions.ShowSeconds"/> is <c>true</c>. Used by surfaces
  /// where second-precision is meaningless (e.g. the queue end-time prediction).
  /// </param>
  /// <returns>
  /// Formatted clock string. Examples by setting:
  /// <list type="bullet">
  ///   <item><description>24h, no seconds → <c>15:45</c></description></item>
  ///   <item><description>24h, with seconds → <c>15:45:22</c></description></item>
  ///   <item><description>12h, no seconds → <c>3:45 PM</c></description></item>
  ///   <item><description>12h, with seconds → <c>3:45:22 PM</c></description></item>
  /// </list>
  /// </returns>
  public static string FormatWallClock(DateTime local, DisplayOptions? opts, bool allowSeconds = true)
  {
    var resolved = opts ?? new DisplayOptions();
    var showSeconds = allowSeconds && resolved.ShowSeconds;
    var is12Hour = string.Equals(resolved.TimeFormat, "12h", StringComparison.OrdinalIgnoreCase);

    // Format specifiers:
    //   24h, no seconds   → HH:mm    (e.g. 15:45)
    //   24h, with seconds → HH:mm:ss (e.g. 15:45:22)
    //   12h, no seconds   → h:mm tt  (e.g. 3:45 PM)
    //   12h, with seconds → h:mm:ss tt (e.g. 3:45:22 PM)
    //
    // The 12h variants use 'h' (not 'hh') so single-digit hours render without a
    // leading zero — matches the visual mock in the handoff (3:45 PM, not 03:45 PM).
    // The 24h variants keep 'HH' so the topbar/sleep clock width stays stable as
    // the hour rolls past 09→10.
    var format = (is12Hour, showSeconds) switch
    {
      (true, true) => "h:mm:ss tt",
      (true, false) => "h:mm tt",
      (false, true) => "HH:mm:ss",
      (false, false) => "HH:mm",
    };

    return local.ToString(format, CultureInfo.InvariantCulture);
  }
}
