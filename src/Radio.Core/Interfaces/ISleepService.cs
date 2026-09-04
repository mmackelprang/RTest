namespace Radio.Core.Interfaces;

/// <summary>
/// Which of the console's three reachable states it is in, as the encoder router must see it.
///
/// <para>
/// Handoff §8.2 describes five. <b>The two dark states are withdrawn by <c>ENC-15</c></b>: the
/// touchscreen is powered by the panel and leaves the USB bus when it blanks, and the encoder has no
/// evdev node at all, so a blanked panel would have one application-mediated wake path rather than
/// two. Blanking does not ship, so nothing can reach a dark state and there is no enum member for
/// one. See <c>design/INTEGRATIONS.md</c> §1 and <c>design/FUTURE-WORK.md</c> §7 (Sleep Mode).
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
  /// Audio is paused and muted. Reached by the topbar Sleep pill, a VOLUME long-press, or the API.
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
  /// The state the encoder router gates on. <b>Reads <see cref="ConsoleWakeState.Awake"/> from the
  /// instant a wake is claimed</b>, which is earlier than either <see cref="IsSleeping"/> flipping
  /// or the browser leaving the route.
  /// </summary>
  ConsoleWakeState WakeState { get; }

  Task EnterSleepAsync();
  Task WakeAsync(string wakeSource = "unknown");

  /// <summary>
  /// Records that a client has put the sleep screen on screen, or taken it off. Releases any
  /// outstanding wake claim either way, because both edges mean the transition has settled.
  /// </summary>
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
