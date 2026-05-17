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
}
