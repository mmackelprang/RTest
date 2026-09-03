namespace Radio.Core.Configuration;

/// <summary>
/// The interaction timings the encoder HUD and the host-side long-press synthesis share.
///
/// <para>
/// These live in Core because the two halves run in different processes: the synthesis is in
/// Radio.Infrastructure (Radio.API), the rendering is in Radio.Web. The handoff asks the synthesis
/// to reuse <c>RadioControlPanel.LongPressThresholdMs</c>, which is a private const in a Razor
/// component in the Web project — reachable as a value, not as a reference. This is that value,
/// with the component repointed at it so there is one definition rather than two that agree today.
/// </para>
/// </summary>
public static class EncoderInteractionTimings
{
  /// <summary>
  /// How long a button must be held before the long action fires, in milliseconds.
  ///
  /// <para>
  /// The long action fires <b>at</b> this threshold while the button is still held, and the
  /// subsequent release does nothing. Releasing before it fires the short action instead.
  /// </para>
  /// </summary>
  public const int LongPressThresholdMs = 600;

  /// <summary>
  /// When the progress ring starts drawing, in milliseconds after the press.
  /// The first 300 ms is indistinguishable from a click, so drawing earlier would put a ring on
  /// screen for every ordinary press.
  /// </summary>
  public const int LongPressRingStartMs = 300;

  /// <summary>
  /// How long a HUD card stays up after the last input, in milliseconds. Long enough to read a
  /// two-digit number after the hand stops; short enough not to camp on the visualizer.
  ///
  /// <para>
  /// <b>This is the tail, not the lifetime.</b> <c>EncoderHudService.ArmDismissLocked</c> cancels
  /// and re-arms on every detent, so a card is up for <i>turn duration + this</i>. Lengthening it
  /// therefore lengthens only what remains after the hand stops and cannot make continuous turning
  /// sticky. ENC-20 raised it from 1500, and that row is also why it matters more than it did: at
  /// 1 volume point per detent an adjustment takes many more detents than it used to, so the
  /// turning phase is longer and the reading phase is the same glance it always was.
  /// </para>
  ///
  /// <para>
  /// ⚠ It also cannot lengthen a <i>suspended</i> state, and that is worth knowing before reaching
  /// for it as a general dial. <c>ArmDismissLocked</c> is reached only when <c>!IsHolding</c> —
  /// during a button hold the timer is cancelled outright rather than extended — and a
  /// <c>SelectorCommitting</c> card is armed against
  /// <see cref="SelectorCommitCeilingMs"/> instead. Neither path reads this value.
  /// </para>
  ///
  /// <para>
  /// <b>Deliberately longer than <see cref="SelectorNoticeMs"/> (2000 ms), and the inversion is not
  /// an oversight.</b> A notice reports a committed action, which sounds like it deserves the longer
  /// window — but the READ CONTEXT differs. A selector notice ends an interaction the user is
  /// already inside and already looking at, so it only has to survive a glance that has begun. The
  /// volume knob is reached for <i>without</i> looking, so its card must additionally survive the
  /// user's latency in deciding to look up at all. <see cref="SelectorNoticeMs"/>,
  /// <see cref="SelectorNoticeShortMs"/> and <see cref="SelectorBlockedFlashMs"/> are unchanged for
  /// that reason.
  /// </para>
  /// </summary>
  public const int HudHoldMs = 2500;

  /// <summary>
  /// Minimum interval between coalesced HUD broadcasts, in milliseconds (20 Hz).
  ///
  /// <para>
  /// Trailing-edge, always emitting the final value. The audio action itself is not throttled —
  /// volume applies per event at full rate; only the broadcast and render are coalesced.
  /// </para>
  /// </summary>
  public const int HudCoalesceMs = 50;

  /// <summary>
  /// How long a selector overlay stays up with nothing committed, in milliseconds (handoff §6.5).
  ///
  /// <para>
  /// Longer than a value card's <see cref="HudHoldMs"/> (2500 ms) because a list has to be read,
  /// and because dismissing it costs nothing: nothing has been committed, so a timeout is not a
  /// lost action.
  ///
  /// <para>
  /// ⚠ The margin is much narrower than it was. ENC-20 raised the value card from 1500 to 2500, so
  /// the gap fell from 2500 ms to 1500 ms while this constant stayed put. Still the right order,
  /// but no longer a comfortable one — raise this if the value card is ever lengthened again,
  /// rather than letting a list get the same dwell as a two-digit number.
  /// </para>
  /// </para>
  /// </summary>
  public const int SelectorIdleDismissMs = 4000;

  /// <summary>
  /// How long a commit on an unavailable row flashes that row before the overlay returns to
  /// previewing, in milliseconds (handoff §6.6 State C).
  /// </summary>
  public const int SelectorBlockedFlashMs = 1500;

  /// <summary>
  /// How long a failed switch stays on screen before dismissing, in milliseconds (§6.6 State E).
  /// It has to outlast a glance across a room, because the whole point is that the user learns the
  /// old source is still playing rather than concluding the knob is broken.
  /// </summary>
  public const int SelectorFailedMs = 4000;

  /// <summary>
  /// The longest a commit-in-flight card is allowed to stay on screen before it is dismissed
  /// anyway, in milliseconds.
  ///
  /// <para>
  /// <b>A failsafe, not a UX duration.</b> Handoff §6.6 State D is explicit that the spinner stays
  /// up until the switch succeeds or fails, and every path out of the commit publishes a terminal
  /// phase, so under normal operation this never fires. It exists because the terminal phase
  /// travels over SignalR: if the API process dies or the hub connection drops mid-commit, nothing
  /// else would ever clear the card. ENC-4 shipped exactly that bug on the hold ring — a device
  /// that vanished mid-hold left a card up indefinitely — and this is the same hazard on a
  /// different phase. Deliberately far longer than any real switch so it cannot be mistaken for a
  /// timeout on a slow Bluetooth connect.
  /// </para>
  /// </summary>
  public const int SelectorCommitCeilingMs = 30000;

  /// <summary>
  /// How long a selector notice stays up, in milliseconds — "Saved to 05", "PRESETS FULL"
  /// (handoff §6.6 State F).
  /// </summary>
  public const int SelectorNoticeMs = 2000;

  /// <summary>
  /// How long a short v1-boundary notice stays up, in milliseconds — "Only radio stations can be
  /// saved". Shorter than <see cref="SelectorNoticeMs"/> because it reports a rule rather than a
  /// result, and the user is still holding the knob when it appears (handoff §4.4).
  /// </summary>
  public const int SelectorNoticeShortMs = 1500;

  /// <summary>
  /// How many rows the selector overlay shows at once. Seven rows plus chrome is what fits the
  /// 600 px content area (handoff §6.6); a longer list scrolls a window of this size around the
  /// highlight.
  /// </summary>
  public const int SelectorVisibleRows = 7;
}
