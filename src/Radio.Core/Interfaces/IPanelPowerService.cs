namespace Radio.Core.Interfaces;

/// <summary>
/// What <see cref="IPanelPowerService.OnEncoderInput"/> decided about one knob input. Anything other
/// than <see cref="Pass"/> means the input is <b>consumed</b>: the router runs no handler for it.
/// </summary>
public enum PanelInputOutcome
{
  /// <summary>The panel is lit and outside any wake grace window; the input goes on to the router's sleep gate.</summary>
  Pass,

  /// <summary>
  /// The panel was dark after the <c>ENC-22</c> sleep-screen timer (or an unconfirmed power-on), and this
  /// input is being spent powering it on. Lighting it is all the input buys.
  /// </summary>
  LitPanel,

  /// <summary>
  /// The panel was dark because of a deep sleep (<see cref="IPanelPowerService.PowerOffNow"/>, <c>ENC-23</c>),
  /// and this input is being spent powering it on. The router may additionally spend it waking the console;
  /// which inputs do that is the router's decision, not this service's.
  /// </summary>
  LitPanelFromDeepSleep,

  /// <summary>
  /// The panel is lit, but a knob wake is still inside its grace window: the panel's own power-up splash
  /// is on screen, so nothing the knob did would be visible.
  /// </summary>
  ConsumedInGrace,
}

/// <summary>The outcome of <see cref="IPanelPowerService.PowerOffNow"/>.</summary>
public enum PanelPowerOffResult
{
  /// <summary>
  /// The panel was wanted on; power-off has been requested. Not awaited, and not necessarily a command
  /// sent: if the last power-on was never confirmed (a retry is pending), the service already counts the
  /// panel as off, so the request matches the applied state and nothing is sent.
  /// </summary>
  PoweredOff,

  /// <summary>The panel was already wanted off. Nothing was sent; the dark panel is now treated as a deep sleep.</summary>
  AlreadyOff,

  /// <summary>Refused under safety rule 1: no encoder is connected, so nothing could wake the panel.</summary>
  RefusedEncoderNotConnected,

  /// <summary>Refused under safety rule 1: the encoder is connected but has not been for <c>EncoderStableSeconds</c>.</summary>
  RefusedEncoderNotStable,

  /// <summary>
  /// The service is not started or is disposed — or, as <c>SystemController</c> uses it, no panel power
  /// service is registered on this host at all. Nothing was sent.
  /// </summary>
  Unavailable,
}

/// <summary>
/// Powers the kiosk panel off — after a period on the sleep screen (<c>ENC-22</c>), or at once on a
/// deep-sleep request (<c>ENC-23</c>) — and back on.
///
/// <para>
/// Lives in Core so the encoder router in <c>Radio.Infrastructure</c> can ask it, before any other
/// sleep gating, whether an input is spent lighting a dark panel, and so the API can request a deep
/// sleep without referencing the Linux implementation.
/// </para>
///
/// <para>
/// ⚠ <b>The knobs are the only wake source for a dark panel.</b> The touchscreen is powered by the
/// panel and leaves the USB bus when it goes dark (<c>ENC-15</c>, re-measured for <c>ENC-22</c>), so
/// an implementation must never leave the panel off while the encoder is not connected. See
/// <c>archive/queue/ENC-22.md</c> for the safety rules this contract carries.
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
  /// Returns <see cref="PanelInputOutcome.LitPanel"/> or <see cref="PanelInputOutcome.LitPanelFromDeepSleep"/>
  /// when the panel was dark and this input is being spent powering it on;
  /// <see cref="PanelInputOutcome.ConsumedInGrace"/> when a knob wake is still inside its grace window;
  /// and <see cref="PanelInputOutcome.Pass"/> when the input should go on to the router's normal sleep
  /// gating. Every value other than <see cref="PanelInputOutcome.Pass"/> means the input is consumed.
  /// </para>
  ///
  /// <para>
  /// Synchronous and non-blocking: it is called on the encoder read thread. The power-on command is
  /// dispatched, not awaited.
  /// </para>
  /// </summary>
  /// <param name="source">What the input was, for the log line (e.g. <c>encoder-turn</c>).</param>
  PanelInputOutcome OnEncoderInput(string source);

  /// <summary>
  /// Deep sleep (<c>ENC-23</c>): powers the panel off now, whatever <c>Sleep:PanelOffAfterMinutes</c> says
  /// (it works when that is 0), and marks the dark panel as a deep sleep so the next knob input reports
  /// <see cref="PanelInputOutcome.LitPanelFromDeepSleep"/>.
  ///
  /// <para>
  /// ⛔ Still under safety rule 1: refused — not deferred — unless the encoder is connected and has been
  /// for <c>EncoderStableSeconds</c>. A refusal sends nothing and leaves the panel as it was.
  /// </para>
  ///
  /// <para>
  /// The decision is synchronous; the power-off is requested of the pump, not awaited, so
  /// <see cref="PanelPowerOffResult.PoweredOff"/> means "requested", not "confirmed by the compositor".
  /// </para>
  /// </summary>
  /// <param name="source">What asked, for the log line (e.g. <c>api-panel-off</c>).</param>
  PanelPowerOffResult PowerOffNow(string source);
}
