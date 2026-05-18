using System.Globalization;
using Radio.Web.Models;

namespace Radio.Web.Formatting;

/// <summary>
/// Resolved playback progress snapshot — elapsed/total in seconds plus the
/// derived progress percentage and display strings. Consumed by
/// <c>NowPlayingPanel</c> and <c>NowPlayingDock</c> via
/// <see cref="PlaybackProgressProjection"/> so the two surfaces share one
/// rounding / clamping / formatting contract.
/// </summary>
/// <param name="ElapsedSeconds">Elapsed playback time, in seconds.</param>
/// <param name="TotalSeconds">Total track duration, in seconds. Zero when unknown / streaming.</param>
/// <param name="Percent">Progress percentage on [0, 100]. Zero when total is zero.</param>
/// <param name="ElapsedDisplay">Formatted elapsed string. <c>"0:00"</c> for sub-second positions.</param>
/// <param name="TotalDisplay">Formatted total string. Em-dash when total is zero / unknown.</param>
public record PlaybackProgress(
  double ElapsedSeconds,
  double TotalSeconds,
  double Percent,
  string ElapsedDisplay,
  string TotalDisplay);

/// <summary>
/// Pure-function projection helpers that convert raw position/duration values
/// into the rendered shape used by the now-playing surfaces.
///
/// <para>
/// Extracted in Arc 3 PR C (item #14) from inline math previously duplicated
/// between <c>NowPlayingPanel.razor</c> and <c>NowPlayingDock.razor</c>. The
/// two surfaces had drifted — the dock added a zero-when-null guard the panel
/// lacked, and the percentage clamp lived only in the dock. This helper is
/// the single source of truth so they stay in lock-step.
/// </para>
///
/// <para>
/// Contract:
/// <list type="bullet">
///   <item>Sub-second elapsed values render as <c>0:00</c> (not em-dash) so
///         the progress label never flashes "—" during early playback.</item>
///   <item>Zero / unknown duration renders the total as the em-dash placeholder
///         and the progress percent stays at zero.</item>
///   <item>Overflow (elapsed &gt; total) clamps the percent at 100 — the
///         display strings still reflect the raw elapsed value so a tail-of-track
///         skew is visible rather than silently truncated.</item>
/// </list>
/// </para>
/// </summary>
public static class PlaybackProgressProjection
{
  private const string Dash = "—";

  /// <summary>
  /// Projects raw elapsed / total seconds into a <see cref="PlaybackProgress"/>
  /// snapshot. Negative inputs are clamped to zero.
  /// </summary>
  public static PlaybackProgress From(double elapsedSeconds, double totalSeconds)
  {
    var elapsed = elapsedSeconds > 0 ? elapsedSeconds : 0;
    var total = totalSeconds > 0 ? totalSeconds : 0;
    var percent = total > 0 ? Math.Clamp(elapsed / total * 100.0, 0, 100) : 0;
    var elapsedDisplay = elapsed >= 1.0
      ? Durations.FormatTrack(TimeSpan.FromSeconds(elapsed))
      : "0:00";
    var totalDisplay = total > 0
      ? Durations.FormatTrack(TimeSpan.FromSeconds(total))
      : Dash;
    return new PlaybackProgress(elapsed, total, percent, elapsedDisplay, totalDisplay);
  }

  /// <summary>
  /// Convenience overload that resolves elapsed / total from a
  /// <see cref="NowPlayingDto"/> (used by <c>NowPlayingDock</c> when applying
  /// a hub payload). Null DTO surfaces as the empty-state shape:
  /// <c>(0, 0, 0, "0:00", "—")</c>.
  /// </summary>
  public static PlaybackProgress From(NowPlayingDto? dto)
  {
    var elapsed = dto?.Position?.TotalSeconds ?? 0;
    var total = dto?.Duration?.TotalSeconds ?? 0;
    return From(elapsed, total);
  }

  /// <summary>
  /// Convenience overload that parses elapsed / total from a
  /// <see cref="PlaybackStateDto"/>'s string-encoded position and duration.
  /// Mirrors the parsing logic <c>NowPlayingPanel</c> / <c>NowPlayingDock</c>
  /// previously inlined — <see cref="TimeSpan.TryParse(string, IFormatProvider, out TimeSpan)"/>
  /// against <see cref="CultureInfo.InvariantCulture"/>; unparseable / null
  /// strings become zero.
  /// </summary>
  public static PlaybackProgress From(PlaybackStateDto? state)
  {
    double elapsed = 0;
    double total = 0;
    if (state != null)
    {
      if (!string.IsNullOrEmpty(state.Position)
          && TimeSpan.TryParse(state.Position, CultureInfo.InvariantCulture, out var pos))
      {
        elapsed = pos.TotalSeconds;
      }

      if (!string.IsNullOrEmpty(state.Duration)
          && TimeSpan.TryParse(state.Duration, CultureInfo.InvariantCulture, out var dur))
      {
        total = dur.TotalSeconds;
      }
    }
    return From(elapsed, total);
  }
}
