using Radio.Web.Models;

namespace Radio.Web.Services;

/// <summary>
/// Decides whether a hardware state change is worth interrupting the owner for, once (ENC-12).
///
/// <para>
/// <b>Scoped, not singleton</b> — unlike <see cref="AudioStateStore"/>, which caches one cabinet's
/// hardware state and is correctly process-wide. This tracks what <i>this browser session</i> has
/// already been told. A process-wide latch would mean a page reload never re-announces a fault that
/// is still present, and that a second browser never hears about it at all. On the kiosk, which runs
/// one long-lived circuit, the two behave identically.
/// </para>
///
/// <para>
/// <b>The rule: each session announces each severity at most once, and only on escalation.</b> The
/// remembered level is never reset — not on recovery, not on reconnect. That is deliberate and it is
/// the whole anti-storm property: a tier that oscillates Degraded → Configured → Degraded speaks
/// exactly once. The trade is that a fault which clears and returns an hour later is silent the
/// second time, and the <b>badge</b> is what covers that — it is stateless, tracks the live tier, and
/// is on screen for as long as the fault exists.
/// </para>
/// </summary>
public sealed class EncoderFaultAnnouncer
{
  private EncoderFaultLevel _highestAnnounced = EncoderFaultLevel.None;
  private bool _announcedDisconnect;
  private bool _announcedReconnect;

  /// <summary>
  /// Whether this state change should raise a notification, and what it should say.
  ///
  /// <para>Returns null — meaning stay silent — for the healthy path, for every repeat, and for every
  /// de-escalation.</para>
  /// </summary>
  /// <param name="status">Serialized <c>RotaryEncoderConfigStatus</c> name.</param>
  /// <param name="isConnected">Current presence.</param>
  /// <param name="wasEverConnected">
  /// From <c>EncoderConnectionDto</c> (ENC-0). Absent at boot gets a badge and <b>no</b> toast — the
  /// owner is most likely standing at the cabinet having just installed or unplugged something.
  /// Disappearing mid-session gets a toast, because it is surprising and may land mid-interaction.
  /// Those are the same <c>IsConnected == false</c> and they are not the same event.
  /// </param>
  public (string Summary, string Detail, EncoderFaultLevel Level)? Evaluate(
    string? status, bool? isConnected, bool wasEverConnected)
  {
    if (isConnected == false)
    {
      // Absent at boot: badge only. wasEverConnected is exactly the flag ENC-0 added to tell the two
      // apart, and until now nothing consumed it.
      if (!wasEverConnected || _announcedDisconnect)
      {
        return null;
      }

      _announcedDisconnect = true;
      var copy = EncoderFaultRules.NotificationCopy(status, isConnected)!.Value;
      return (copy.Summary, copy.Detail, EncoderFaultLevel.Warning);
    }

    // Recovery: announced only for an absence we announced (handoff §7.3), and only once per session
    // (plan §0.4 C-3 — a lead that flaps inside furniture must not narrate every bounce).
    if (isConnected == true && _announcedDisconnect && !_announcedReconnect)
    {
      _announcedReconnect = true;
      return ("Knobs connected", "The knobs are working again.", EncoderFaultLevel.None);
    }

    EncoderFaultLevel level = EncoderFaultRules.Level(status, isConnected);
    if (level <= _highestAnnounced || level == EncoderFaultLevel.None)
    {
      return null;
    }

    _highestAnnounced = level;
    var faultCopy = EncoderFaultRules.NotificationCopy(status, isConnected);
    return faultCopy is null ? null : (faultCopy.Value.Summary, faultCopy.Value.Detail, level);
  }
}
