namespace Radio.Core.Configuration;

/// <summary>
/// Panel power-off in sleep (<c>ENC-22</c>). Bound from the <c>Sleep</c> configuration section and
/// read through <c>IOptionsMonitor</c>, so an edit to <c>appsettings.Production.json</c> takes effect
/// without a restart — including on a console already sitting on the sleep screen, where the change
/// re-arms the countdown from the moment it is read.
/// </summary>
public class PanelPowerOptions
{
  /// <summary>Configuration section name.</summary>
  public const string SectionName = "Sleep";

  /// <summary>
  /// Minutes of continuous sleep screen before the panel is powered off. <b>0 (the default) switches
  /// the timer off</b>: the sleep screen never powers the panel off on its own, while the safety
  /// behaviour (power the panel on at start-up, and whenever the encoder is lost) still runs.
  ///
  /// <para>
  /// 0 disables only this timer, not panel power-off. A deep sleep (<c>ENC-23</c>: a hold of the
  /// topbar Sleep pill, or <c>POST /api/system/sleep</c> with <c>panelOff: true</c>) still powers the
  /// panel off at once whatever this says, under the same encoder safety rule.
  /// </para>
  ///
  /// <para>
  /// ⚠ Shipped disabled on purpose. The one check that cannot be made from a shell — a real knob,
  /// turned by a hand, delivered while the panel is dark — has not been made yet. See
  /// <c>archive/uat/OWNER-REVIEW.md</c> ("Phase 2g") for the check and the enable step.
  /// </para>
  /// </summary>
  public double PanelOffAfterMinutes { get; set; }

  /// <summary>
  /// How long the encoder must have been continuously connected before the panel may be powered off.
  /// Applies after a reconnect: a USB link that has just come back is not yet trusted to stay.
  /// </summary>
  public double EncoderStableSeconds { get; set; } = 30;

  /// <summary>
  /// After a knob powers the panel on, further encoder input is consumed for this long. The panel
  /// shows its own firmware splash for about two seconds on power-up (owner observation,
  /// 2026-09-28), so a fast spin that woke the panel must not go on to change the volume unseen.
  /// </summary>
  public double WakeGraceMilliseconds { get; set; } = 2000;

  /// <summary>
  /// The desktop session bus Mutter listens on. <c>radio-api</c> runs as the session user
  /// (<c>mmack</c>, uid 1000), so it can reach this bus directly.
  /// </summary>
  public string SessionBusAddress { get; set; } = "unix:path=/run/user/1000/bus";
}
