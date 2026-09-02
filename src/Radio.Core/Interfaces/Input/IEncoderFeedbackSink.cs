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
}

/// <summary>
/// Event args for one HUD card update.
/// </summary>
/// <remarks>
/// <see cref="EncoderIndex"/> decides <b>where</b> the card renders — the HUD divides the 1920 px
/// viewport into quarters and puts the card in this encoder's own quarter, so the readout appears
/// above the knob that produced it. The remaining fields decide <b>what</b> it says. The two are
/// deliberately independent: the router's index-to-handler mapping is still the pre-ENC-5 one, so
/// the card is in the right place before it says the right word, and it will say the right word
/// without the HUD changing.
/// </remarks>
public class EncoderHudEventArgs : EventArgs
{
  /// <summary>Encoder index (0-3). Selects the screen quarter.</summary>
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
