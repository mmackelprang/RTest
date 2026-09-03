namespace Radio.Core.Interfaces.Input;

/// <summary>
/// What a HUD card is showing, and why it appeared.
///
/// <para>
/// The Web side treats the wire value as an open string and renders nothing for a phase it does not
/// know, so a newer API build cannot throw on an older kiosk. This enum is the API-side source of
/// those names.
/// </para>
/// </summary>
public enum EncoderHudPhase
{
  /// <summary>A knob was turned (or a value otherwise changed) and the card shows the result.</summary>
  Value,

  /// <summary>A button went down. The client starts the progress ring at 300 ms.</summary>
  HoldStart,

  /// <summary>The button was released before the threshold. The ring collapses; the short action fired.</summary>
  HoldCancel,

  /// <summary>The hold reached the threshold and the long action fired while still held.</summary>
  HoldCommit,

  /// <summary>
  /// A selector overlay is open and previewing. Nothing has been committed. Coalesced like
  /// <see cref="Value"/> — a moving highlight is a sampled value, not a discrete edge.
  /// </summary>
  SelectorPreview,

  /// <summary>
  /// A commit landed on an unavailable row. The overlay stays open and flashes that row.
  /// Handoff §6.6 State C — never a silent no-op.
  /// </summary>
  SelectorBlocked,

  /// <summary>A real switch is in flight. Handoff §6.6 State D — spinner, card stays up.</summary>
  SelectorCommitting,

  /// <summary>The switch failed. Handoff §6.6 State E — reason plus what is still playing.</summary>
  SelectorFailed,

  /// <summary>
  /// A short message replacing the list for its own duration — ENC-7's "Saved to 05", "PRESETS
  /// FULL", "Only radio stations can be saved". Declared here rather than in ENC-7 so the phase set
  /// is one enum and the Web's dispatch table is written once.
  /// </summary>
  SelectorNotice,
}

/// <summary>
/// Which phases are samples of a moving value and may therefore be coalesced, and which are
/// discrete edges that must not be dropped.
/// </summary>
public static class EncoderHudPhases
{
  /// <summary>
  /// True for phases that represent "the current value, sampled" — a turning knob. False for edges
  /// whose loss would strand something on screen: a progress ring that never resolves, a spinner
  /// that never clears, a flash that never fires.
  /// </summary>
  public static bool IsCoalescable(EncoderHudPhase phase) =>
    phase is EncoderHudPhase.Value or EncoderHudPhase.SelectorPreview;
}

/// <summary>
/// Event args for one HUD card update.
/// </summary>
/// <remarks>
/// <see cref="EncoderIndex"/> decides <b>where</b> the card renders — the knobs are a vertical
/// column to the left of the LCD, so the HUD anchors the card to the left edge of the viewport on
/// this encoder's own band and the readout appears beside the knob that produced it, at the same
/// height. The bands are <see cref="Radio.Core.Configuration.FrontPanelGeometry"/>, which is the
/// single definition of the panel. The remaining fields decide <b>what</b> it says. The two are
/// deliberately independent, and ENC-5 is the demonstration: it reassigned three of the four
/// index-to-handler pairs without touching this type or the HUD's geometry, because a card's
/// position comes from the index the event arrived on rather than from the router's table.
/// </remarks>
public class EncoderHudEventArgs : EventArgs
{
  /// <summary>Encoder index (0-3), top of the knob column downwards. Selects the screen band.</summary>
  public int EncoderIndex { get; init; }

  /// <summary>Label row text, uppercased by CSS — e.g. "VOLUME", "TUNING", "SOURCE".</summary>
  public required string Label { get; init; }

  /// <summary>Why this update was published.</summary>
  public EncoderHudPhase Phase { get; init; } = EncoderHudPhase.Value;

  /// <summary>
  /// Volume as whole percentage points (0-100), or null when this card is not a volume card.
  /// Present so the card can render numerals and a fill bar without a second round trip.
  /// </summary>
  public int? VolumePercent { get; init; }

  /// <summary>True when the console is muted. Drives the muted variant of the volume card.</summary>
  public bool IsMuted { get; init; }

  /// <summary>Primary line — a frequency, a track title, a source or mode name.</summary>
  public string? PrimaryText { get; init; }

  /// <summary>Secondary line — band and step, artist and album, or null.</summary>
  public string? SecondaryText { get; init; }

  /// <summary>
  /// True when the primary line is a radio frequency and should be rendered with
  /// <c>.display-frequency</c>. A flag rather than a parsed value: the Web must not re-derive
  /// formatting the API already did.
  /// </summary>
  public bool PrimaryIsFrequency { get; init; }

  /// <summary>
  /// How long the client should hold this card before dismissing it, in milliseconds. Null means
  /// the default (<see cref="Radio.Core.Configuration.EncoderInteractionTimings.HudHoldMs"/>).
  ///
  /// <para>
  /// Carried on the payload rather than derived from <see cref="Phase"/> because the handoff
  /// specifies four different durations across five states (1500 value / 1500 blocked / 2000 saved /
  /// 4000 selector idle / 4000 failed), and ENC-7 adds more. One nullable field beats a lookup
  /// table each row has to extend.
  /// </para>
  /// </summary>
  public int? DurationMs { get; init; }

  /// <summary>
  /// The selector list, when this is a selector phase. Null on every non-selector phase.
  ///
  /// <para>
  /// <b>Always the complete list, never a delta.</b> <c>EncoderFeedbackService</c> coalesces by
  /// replacing the pending update for an encoder, so a rows-less update arriving inside the 50 ms
  /// window would discard the rows the overlay needs. Every selector update is self-contained.
  /// </para>
  /// </summary>
  public IReadOnlyList<EncoderSelectorRow>? Rows { get; init; }

  /// <summary>Index into <see cref="Rows"/> of the highlighted row, or -1 when the list is empty.</summary>
  public int HighlightIndex { get; init; } = -1;

  /// <summary>Overlay heading — "SOURCE" or "PRESETS".</summary>
  public string? Title { get; init; }

  /// <summary>Right-hand side of the heading row — ENC-7's "4 saved". Null for SOURCE.</summary>
  public string? TitleSuffix { get; init; }

  /// <summary>Footer line — "PRESS THE KNOB TO SWITCH" / "PRESS TO PLAY · HOLD TO SAVE".</summary>
  public string? Footer { get; init; }

  /// <summary>Primary line of the instructional empty state, when <see cref="Rows"/> is empty.</summary>
  public string? EmptyPrimary { get; init; }

  /// <summary>Secondary line of the instructional empty state.</summary>
  public string? EmptySecondary { get; init; }
}

/// <summary>
/// Where the router publishes on-screen feedback. Implemented in Radio.Infrastructure and consumed
/// by Radio.API, which is what turns it into a SignalR broadcast.
/// </summary>
public interface IEncoderFeedbackSink
{
  /// <summary>
  /// Publishes one HUD update.
  ///
  /// <para>
  /// A subscriber that throws does not propagate back to the caller: this is on the encoder input
  /// path, and a cosmetic readout must not take the knobs down with it. <paramref name="update"/>
  /// itself is validated — a null throws, and an out-of-range encoder index is dropped.
  /// </para>
  /// </summary>
  void Publish(EncoderHudEventArgs update);

  /// <summary>
  /// Fired for each update that survives coalescing.
  ///
  /// <para>
  /// The thread varies by edge and callers must not assume one: the leading edge of a burst is
  /// raised synchronously on the publishing thread, while a trailing-edge flush is raised on the
  /// coalescer's timer thread.
  /// </para>
  /// </summary>
  event EventHandler<EncoderHudEventArgs>? Feedback;
}
