using System.Globalization;

namespace Radio.Web.Formatting;

/// <summary>
/// Relative-time formatting for play history, queue, and event rows.
/// Caller is responsible for converting UTC values to local time before calling.
/// </summary>
public static class Timestamps
{
  /// <summary>
  /// Middle dot separator (U+00B7) used between date and time in the "older" branch.
  /// </summary>
  private const string Separator = "·";

  /// <summary>
  /// Formats a <see cref="DateTime"/> as a short relative timestamp using <see cref="DateTime.Now"/> as the reference.
  /// Calendar-anchored — best for history / queue rows where the absolute date is
  /// meaningful. For very recent events (matches in the last few minutes) prefer
  /// <see cref="FormatRecentRelative(DateTime)"/>, which renders <c>Xs ago / Xm ago / …</c>.
  /// <list type="bullet">
  ///   <item><description>Same calendar day: <c>Today HH:mm</c>.</description></item>
  ///   <item><description>Previous calendar day: <c>Yesterday HH:mm</c>.</description></item>
  ///   <item><description>Otherwise: <c>{MMM d} · HH:mm</c>.</description></item>
  /// </list>
  /// </summary>
  /// <param name="local">A local-time <see cref="DateTime"/>. Caller must have already converted from UTC.</param>
  public static string FormatRelative(DateTime local) => FormatRelative(local, DateTime.Now);

  /// <summary>
  /// Testable overload accepting an explicit <paramref name="now"/> reference.
  /// </summary>
  public static string FormatRelative(DateTime local, DateTime now)
  {
    var localDate = local.Date;
    var nowDate = now.Date;
    var time = local.ToString("HH:mm", CultureInfo.InvariantCulture);

    if (localDate == nowDate)
    {
      return $"Today {time}";
    }

    if (localDate == nowDate.AddDays(-1))
    {
      return $"Yesterday {time}";
    }

    var date = local.ToString("MMM d", CultureInfo.InvariantCulture);
    return $"{date} {Separator} {time}";
  }

  /// <summary>
  /// Formats a UTC <see cref="DateTime"/> as a short elapsed-duration label —
  /// <c>"just now"</c>, <c>"Xs ago"</c>, <c>"Xm ago"</c>, <c>"Xh ago"</c>, or
  /// <c>"Xd ago"</c>. Best for very-recent events (recognition stream, dev tray
  /// event log) where the calendar context of
  /// <see cref="FormatRelative(DateTime)"/> reads as too rigid. Extracted from
  /// <c>NowPlayingPanel.FormatTimeAgo</c> in Arc 3 PR C (item #35) so the
  /// short-relative formatter is reusable across surfaces.
  /// </summary>
  /// <param name="utc">A UTC <see cref="DateTime"/>.</param>
  public static string FormatRecentRelative(DateTime utc) => FormatRecentRelative(utc, DateTime.UtcNow);

  /// <summary>
  /// Testable overload accepting an explicit <paramref name="nowUtc"/> reference.
  /// </summary>
  public static string FormatRecentRelative(DateTime utc, DateTime nowUtc)
  {
    var delta = nowUtc - utc;
    if (delta.TotalSeconds < 1)
    {
      return "just now";
    }
    if (delta.TotalMinutes < 1)
    {
      return $"{(int)delta.TotalSeconds}s ago";
    }
    if (delta.TotalHours < 1)
    {
      return $"{(int)delta.TotalMinutes}m ago";
    }
    if (delta.TotalDays < 1)
    {
      return $"{(int)delta.TotalHours}h ago";
    }
    return $"{(int)delta.TotalDays}d ago";
  }
}
