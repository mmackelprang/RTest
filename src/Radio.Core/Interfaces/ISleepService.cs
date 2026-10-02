namespace Radio.Core.Interfaces;

/// <summary>
/// Which of the console's three lit states it is in, as the encoder router must see it.
///
/// <para>
/// Handoff §8.2 describes five. <b>The two dark states are not members of this enum.</b>
/// <c>ENC-15</c> withdrew them because touch cannot wake a dark panel — the touchscreen is powered by
/// the panel and leaves the USB bus when it goes dark. <c>ENC-22</c> reinstates a dark panel for the
/// <b>knob-only</b> design, but as a separate layer rather than a state here:
/// <see cref="IPanelPowerService"/> is asked first by the router, consumes any input that lights a
/// dark panel, and leaves the console in whichever of these three states it was already in. See
/// <c>docs/queue/ENC-22.md</c> and <c>design/INTEGRATIONS.md</c> §1.
/// </para>
///
/// <para>
/// <c>ENC-23</c> adds one exception to "leaves the console in whichever state": a <b>VOLUME press</b>
/// that lights a panel darkened by a deep sleep (<see cref="IPanelPowerService.PowerOffNow"/>) is also
/// spent waking it, so the owner's "click the Volume knob to wake" is one press, not two. The decision
/// is the router's; this enum is unchanged.
/// </para>
/// </summary>
public enum ConsoleWakeState
{
  /// <summary>Full UI. Every knob acts.</summary>
  Awake,

  /// <summary>
  /// The dim clock is on screen and <b>audio is still playing</b>. Reached by the 30-minute idle
  /// timer or by navigating to <c>/sleep</c> directly. VOLUME acts in place here; every other knob
  /// is spent waking (handoff §8.3).
  /// </summary>
  Ambient,

  /// <summary>
  /// Audio is paused and muted. Reached by the topbar Sleep pill (a tap, or a hold, which also asks
  /// for the panel to be powered off — <c>ENC-23</c>'s deep sleep; that request is refused when the
  /// encoder is not connected and stable, and the console is in Standby either way), a VOLUME
  /// long-press, or the API.
  /// A <b>turn</b> here never resumes audio — only a press or a screen tap does (D22).
  /// </summary>
  Standby,
}

/// <summary>
/// Abstraction for sleep/standby mode management.
/// Lives in Core so Infrastructure (e.g., RotaryEncoderActionRouter) can
/// depend on it without referencing Radio.API.
/// </summary>
public interface ISleepService
{
  /// <summary>
  /// True when audio is parked — paused and muted. <b>This is the audio truth and nothing else.</b>
  /// It is deliberately <i>not</i> affected by the wake claim below: a console whose resume is in
  /// flight still has paused audio, and reporting otherwise would make
  /// <c>GET /api/system/sleep</c> lie.
  /// </summary>
  bool IsSleeping { get; }

  /// <summary>
  /// True while a client reports the <c>/sleep</c> route on screen. Set by the page itself, on first
  /// render and on dispose, so all three ways of reaching that route produce the same server-side
  /// fact.
  /// </summary>
  /// <remarks>
  /// ⚠ The "three" counts <b>routes to the page</b> — the idle timer, the Sleep pill, and a direct
  /// navigation — and is right about that. It is <b>not</b> a count of the ways sleep is entered:
  /// ADR-029 §16.4 finds five of those, because it separates the server push and the browserless
  /// server-side entry from the taps that produce them. Named here because §16.4's whole finding is
  /// that an unexamined "three client paths" claim propagated through four documents.
  /// </remarks>
  bool IsSleepScreenVisible { get; }

  /// <summary>
  /// Raised when <see cref="IsSleepScreenVisible"/> <b>changes</b> — not on a re-report of the state
  /// already held. The argument is the value this report wrote; a subscriber that must not act on an
  /// out-of-order delivery should re-read <see cref="IsSleepScreenVisible"/> instead.
  /// </summary>
  /// <remarks>
  /// Added for <c>ENC-22</c>, whose panel power-off timer is keyed on the sleep screen being up rather
  /// than on <see cref="IsSleeping"/>: the idle path never sets <see cref="IsSleeping"/>.
  /// </remarks>
  event EventHandler<bool>? SleepScreenVisibilityChanged;

  /// <summary>
  /// The state the encoder router gates on. <b>Reads <see cref="ConsoleWakeState.Awake"/> from the
  /// instant a wake is claimed</b>, which is earlier than either <see cref="IsSleeping"/> flipping
  /// or the browser leaving the route.
  /// </summary>
  ConsoleWakeState WakeState { get; }

  Task EnterSleepAsync();
  Task WakeAsync(string wakeSource = "unknown");

  /// <summary>
  /// Records that a client has put the sleep screen on screen, or taken it off. Releases an
  /// outstanding wake claim when the report is a <b>change</b>; a re-report of the state already
  /// held deliberately leaves the claim alone, so a future heartbeat could not wipe a claim mid-wake.
  /// </summary>
  /// <remarks>
  /// ⚠ The "either way" this used to say meant "on both edges, up and down", and read as "on every
  /// call". The implementation gates the release on <c>changed</c> and its own comment explains at
  /// length why a re-report must not clear it — so the interface was promising the one input shape
  /// the implementation is written to refuse.
  /// </remarks>
  /// <remarks>
  /// ⚠ <b>Task-returning because it stops attended playback</b> (ADR-029 §16.5), not because the
  /// flag write needs to be. The write is synchronous and complete before the returned task is
  /// awaited; what the task carries is the stop. It was <c>void</c> until ADR-029 Amendment 2, and
  /// the reason it is not any more is plan constraint <c>C-49</c>: this repo has a fresh, expensive
  /// lesson about dispatching a stop that nothing observes. §16.5 left the choice between awaiting
  /// and dispatching open and argued for awaiting; this is that choice, taken.
  /// </remarks>
  Task SetSleepScreenVisibleAsync(bool visible);

  /// <summary>
  /// Claims the single input that is spent waking, synchronously.
  ///
  /// <para>
  /// Returns <c>true</c> to exactly one caller per wake. Every later caller gets <c>false</c> and
  /// finds <see cref="WakeState"/> already reading <see cref="ConsoleWakeState.Awake"/>, so its
  /// input acts instead of being discarded. Returns <c>false</c> immediately when the console is
  /// already awake, without burning a claim.
  /// </para>
  /// </summary>
  bool TryClaimWake();
}
