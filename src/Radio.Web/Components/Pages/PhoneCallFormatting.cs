using System.Globalization;
using System.Text;
using Radio.Web.Models;

namespace Radio.Web.Components.Pages;

/// <summary>
/// Shared call-row formatting helpers (direction icon/colour + duration). Single
/// source of truth used by both PhoneHistoryPanel and the unified Messages feed
/// (PhoneMessagesPanel) so the two render call rows identically. PR2/PR3 reuse it
/// for voicemail/text rows that surface call-back context.
/// </summary>
public static class PhoneCallFormatting
{
  public static string GetCallDirectionIcon(CallHistoryEntryDto entry) => entry.Direction switch
  {
    CallDirection.Incoming when entry.AnsweredOn != CallAnsweredOn.NotAnswered => "call_received",
    CallDirection.Incoming => "call_missed",
    CallDirection.Outgoing => "call_made",
    _ => "phone"
  };

  public static string GetCallDirectionColor(CallHistoryEntryDto entry) => entry.Direction switch
  {
    CallDirection.Incoming when entry.AnsweredOn != CallAnsweredOn.NotAnswered => "var(--rz-success)",
    CallDirection.Incoming => "var(--rz-danger)",
    CallDirection.Outgoing => "var(--rz-info)",
    _ => "var(--rz-text-color)"
  };

  // Call durations arrive from RotaryPhone as a raw TimeSpan string with full
  // tick precision (e.g. "00:00:37.9710594"). Strip the sub-second noise and
  // show a compact "m:ss" (or "h:mm:ss" for calls of an hour or more). Falls
  // back to the raw string if it ever arrives in an unparseable format.
  public static string FormatDuration(string? raw)
  {
    if (string.IsNullOrWhiteSpace(raw)
        || !TimeSpan.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture, out var ts))
    {
      return raw ?? "";
    }

    ts = TimeSpan.FromSeconds(Math.Round(ts.TotalSeconds));
    return ts.TotalHours >= 1 ? ts.ToString(@"h\:mm\:ss") : ts.ToString(@"m\:ss");
  }

  // Glanceable, relative feed timestamp (replaces the verbose "6/28/2026 4:43 PM"
  // ToString("g") in the Messages feed). Rules per the phone dark-theme handoff
  // copy.md: today → "4:43 PM", yesterday → "Yesterday", within the last week →
  // weekday ("Mon"), same calendar year → "Jun 28", older → "6/28/25". Formatted
  // with InvariantCulture so the kiosk (en-US) and CI (any locale) agree — the
  // strings are asserted verbatim in tests. Pass a local DateTime; `nowLocal`
  // exists only so tests can pin "now" deterministically.
  public static string FormatFeedTimestamp(DateTime localTime, DateTime? nowLocal = null)
  {
    var now = nowLocal ?? DateTime.Now;
    var today = now.Date;
    var day = localTime.Date;

    if (day == today)
    {
      return localTime.ToString("h:mm tt", CultureInfo.InvariantCulture);
    }
    if (day == today.AddDays(-1))
    {
      return "Yesterday";
    }
    // 2–6 days ago → weekday name. (Exactly 7 days ago falls through so it can't
    // collide with today's weekday.)
    if (day > today.AddDays(-7))
    {
      return localTime.ToString("ddd", CultureInfo.InvariantCulture);
    }
    if (localTime.Year == now.Year)
    {
      return localTime.ToString("MMM d", CultureInfo.InvariantCulture);
    }
    return localTime.ToString("M/d/yy", CultureInfo.InvariantCulture);
  }

  // Present a raw phone number in the compact secondary slot beneath a resolved
  // contact name. US 10-digit (or 11-digit with a leading country "1") →
  // "(908) 555-0142". Anything else (short codes, already-formatted, international)
  // is returned trimmed as-is so we never mangle a number we don't understand.
  public static string FormatPhoneNumber(string? raw)
  {
    if (string.IsNullOrWhiteSpace(raw))
    {
      return "";
    }

    var digits = new StringBuilder(raw.Length);
    foreach (var ch in raw)
    {
      if (char.IsDigit(ch))
      {
        digits.Append(ch);
      }
    }

    var d = digits.ToString();
    if (d.Length == 11 && d[0] == '1')
    {
      d = d[1..];
    }
    if (d.Length == 10)
    {
      return $"({d[..3]}) {d[3..6]}-{d[6..]}";
    }
    return raw.Trim();
  }
}
