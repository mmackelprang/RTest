namespace Radio.Core.Interfaces;

/// <summary>
/// Powers the kiosk panel off after a period on the sleep screen, and back on (<c>ENC-22</c>).
///
/// <para>
/// Lives in Core so the encoder router in <c>Radio.Infrastructure</c> can ask it, before any other
/// sleep gating, whether an input is spent lighting a dark panel.
/// </para>
///
/// <para>
/// ⚠ <b>The knobs are the only wake source for a dark panel.</b> The touchscreen is powered by the
/// panel and leaves the USB bus when it goes dark (<c>ENC-15</c>, re-measured for <c>ENC-22</c>), so
/// an implementation must never leave the panel off while the encoder is not connected. See
/// <c>docs/queue/ENC-22.md</c> for the safety rules this contract carries.
/// </para>
/// </summary>
public interface IPanelPowerService
{
  /// <summary>
  /// True when the panel has been commanded off, or may be dark and has not yet been confirmed back on
  /// (after a confirmed or an unconfirmed power-off). False while the panel's state is merely unknown
  /// at start-up, where the unconditional power-on is being retried in the background. A diagnostic
  /// read; the router decides through <see cref="OnEncoderInput"/>.
  /// </summary>
  bool IsPanelOff { get; }

  /// <summary>
  /// Called by the encoder router for every turn and every press edge, before the sleep gate.
  ///
  /// <para>
  /// Returns <c>true</c> when the input must be <b>consumed</b>: the panel was dark and this input
  /// is being spent powering it on, or a knob wake is still inside its grace window (the panel's own
  /// power-up splash is on screen, so nothing the knob did would be visible). Returns <c>false</c>
  /// when the input should go on to the router's normal sleep gating.
  /// </para>
  ///
  /// <para>
  /// Synchronous and non-blocking: it is called on the encoder read thread. The power-on command is
  /// dispatched, not awaited.
  /// </para>
  /// </summary>
  /// <param name="source">What the input was, for the log line (e.g. <c>encoder-turn</c>).</param>
  bool OnEncoderInput(string source);
}
