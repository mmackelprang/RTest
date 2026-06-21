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
}
