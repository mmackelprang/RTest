namespace Radio.Web.Models;

/// <summary>
/// Client-derived health of the physical rotary-phone bell (the HT801 ATA leg).
/// Orthogonal to call state: the hero renders the product of the two, it does not
/// collapse them into one enum (bell-failure handoff §2, Axis B).
/// </summary>
/// <remarks>
/// <para>
/// The distinction that matters most is <see cref="Unknown"/> vs <see cref="Suspect"/>.
/// <c>PhoneSystemStatusDto.Ht801Reachable</c> is <c>bool?</c> and a <c>null</c> means
/// "not yet probed / cannot determine" — never "false". Collapsing <c>null</c> into a
/// red "Offline" produced a false alarm on every cold page load, which is the bug this
/// enum exists to make impossible to reintroduce (handoff §3.6, §6.3, §7m).
/// </para>
/// <para>
/// <see cref="Failed"/> has no producer yet. It is populated from the RotaryPhone
/// <c>BellInviteFailed</c> hub event (handoff §6.1), which ships in the follow-up PR.
/// It is modelled here so the state machine is complete and the consumers below do
/// not have to change shape when the event lands.
/// </para>
/// </remarks>
public enum BellHealth
{
  /// <summary>No reason to think the bell is broken. <c>Ht801Reachable == true</c>.</summary>
  Ok,

  /// <summary>
  /// The ATA is known-unreachable, but no ring has actually been attempted yet.
  /// <c>Ht801Reachable == false</c>.
  /// </summary>
  Suspect,

  /// <summary>
  /// A specific ring attempt was confirmed to have failed. Sourced from the
  /// <c>BellInviteFailed</c> hub event (handoff §6.1) — not produced yet.
  /// </summary>
  Failed,

  /// <summary>
  /// We have not been able to check. <c>Ht801Reachable == null</c>, or the status
  /// fetch itself failed. MUST NOT alarm — absence of evidence is not evidence of
  /// failure (handoff §7m).
  /// </summary>
  Unknown,
}

/// <summary>
/// Pure derivation + copy rules for <see cref="BellHealth"/>. Kept free of any
/// component or DI dependency so the branches the UI relies on — above all
/// "<c>null</c> renders as unknown, not as unreachable" — are unit-testable without
/// standing up bUnit, Radzen, or the layout (which is not renderable under bUnit,
/// see <c>MainLayoutTests</c>).
/// </summary>
public static class BellHealthRules
{
  /// <summary>
  /// Maps a phone system-status snapshot onto bell health.
  /// A <c>null</c> status (RotaryPhone.API unreachable, or not fetched yet) and a
  /// <c>null</c> <c>Ht801Reachable</c> both mean <see cref="BellHealth.Unknown"/>.
  /// </summary>
  public static BellHealth FromSystemStatus(PhoneSystemStatusDto? status) =>
    status?.Ht801Reachable switch
    {
      true => BellHealth.Ok,
      false => BellHealth.Suspect,
      _ => BellHealth.Unknown,
    };

  /// <summary>
  /// True when the bell is believed to be broken and the UI should say so.
  /// <see cref="BellHealth.Unknown"/> is deliberately NOT a fault (handoff §7m).
  /// </summary>
  public static bool IsFaulted(BellHealth health) =>
    health is BellHealth.Suspect or BellHealth.Failed;

  /// <summary>CSS modifier for the System Status card's BELL pill (handoff §3.6).</summary>
  public static string PillClass(BellHealth health) => health switch
  {
    BellHealth.Ok => "green",
    BellHealth.Suspect or BellHealth.Failed => "red",
    _ => "gray",
  };

  /// <summary>Text for the System Status card's BELL pill (handoff §3.6).</summary>
  public static string PillText(BellHealth health) => health switch
  {
    BellHealth.Ok => "Online",
    BellHealth.Suspect or BellHealth.Failed => "Offline",
    _ => "Unknown",
  };

  /// <summary>
  /// Accessible name for the topbar /phone nav pill (handoff §5.6). The fault is
  /// carried in text regardless of the glyph, so a screen-reader user gets the same
  /// wayfinding cue as a sighted one.
  /// </summary>
  public static string NavPillAriaLabel(BellHealth health, int unreadCount)
  {
    var basePart = unreadCount > 0 ? $"Phone, {unreadCount} unread" : "Phone";
    return IsFaulted(health) ? $"{basePart} — the phone won't ring" : basePart;
  }
}
