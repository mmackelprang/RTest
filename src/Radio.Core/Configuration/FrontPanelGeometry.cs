namespace Radio.Core.Configuration;

/// <summary>
/// One knob's position on the cabinet's front panel, and the screen band its readout uses.
/// </summary>
/// <remarks>
/// See <see cref="FrontPanelGeometry"/> for where these numbers come from and why
/// <see cref="HudBandYPx"/> is a round number rather than the measured one.
/// </remarks>
public sealed record EncoderPanelPosition
{
  /// <summary>Encoder index as the host reports it, 0-3, top of the column downwards.</summary>
  public required int Index { get; init; }

  /// <summary>
  /// The word engraved on the panel beneath this knob.
  ///
  /// <para>
  /// This is what the cabinet says, not what the software currently does with the knob. ENC-5's
  /// remap brought indices 0, 1 and 3 into line with the engraving. Index 2 still disagrees: it is
  /// engraved PRESETS and drives the visualiser until ENC-7 introduces the handler that reconciles
  /// them.
  /// </para>
  /// </summary>
  public required string EngravedName { get; init; }

  /// <summary>
  /// Knob centre measured down from the top edge of the panel, in millimetres.
  /// </summary>
  public required double PanelCentreYMm { get; init; }

  /// <summary>
  /// The vertical centre, in CSS pixels of the 720 px viewport, of the band this knob's HUD card
  /// is placed on.
  ///
  /// <para>
  /// <b>Deliberately rounded.</b> The measured projections of the four knobs onto the screen are
  /// 93.05 / 271.02 / 448.98 / 626.95 px; these are 90 / 270 / 450 / 630. See
  /// <see cref="FrontPanelGeometry"/> for the reason the rounding is the specified value and not
  /// an approximation of one.
  /// </para>
  /// </summary>
  public required int HudBandYPx { get; init; }
}

/// <summary>
/// The as-built front panel, expressed once.
///
/// <para>
/// <b>Source of truth: <c>design/hardware/front-panel-layout_4.svg</c></b> (dated 2026-09-01,
/// committed to this repo). Every value here is derived from that drawing at
/// <see cref="DrawingPxPerMm"/>, and the drawing wins over this file if they ever disagree. If the
/// panel is recut, the drawing changes first and these numbers are re-derived from it.
/// </para>
///
/// <para>
/// <b>Why one definition.</b> The four band positions, the four engraved names and the
/// index-to-knob mapping are all facts about that one drawing, and four separate surfaces need
/// them: the encoder HUD, the encoder diagnostics card, the encoder Settings table, and the SOURCE
/// and PRESETS selector overlays. A recut should move one line rather than five. This project has
/// already paid for the alternative once - <c>CLAUDE.md</c> records three callers carrying three
/// different kiosk flag sets as how the appliance drifted in the first place.
/// </para>
///
/// <para>
/// This lives in Core, beside <see cref="EncoderInteractionTimings"/>, for the same reason that
/// class does: the panel is a physical fact about the appliance rather than a property of any one
/// project, and <c>Radio.Core.Interfaces.Input.EncoderHudEventArgs</c> - which is in Core - has to
/// be able to describe what an encoder index means.
/// </para>
/// </summary>
public static class FrontPanelGeometry
{
  /// <summary>Repo-relative path of the drawing every value here derives from.</summary>
  public const string DrawingPath = "design/hardware/front-panel-layout_4.svg";

  /// <summary>
  /// The drawing's scale: user units per millimetre. Established from its VESA-75 reference
  /// square, which measures 212.5984 px across for 75.000 mm.
  /// </summary>
  public const double DrawingPxPerMm = 2.8346;

  /// <summary>Number of encoders on the panel.</summary>
  public const int EncoderCount = 4;

  /// <summary>Height of the kiosk viewport, in CSS pixels. The LCD's active area fills it.</summary>
  public const int ViewportHeightPx = 720;

  /// <summary>
  /// The four knobs, top of the column downwards, indexed to match <see cref="EncoderPanelPosition.Index"/>.
  ///
  /// <para>
  /// <b>Panel:</b> 406.4 x 152.4 mm, one vertical column of four 15 mm knobs at x = 25.4 mm, uniform
  /// 29.63 mm pitch, column centred on the panel's vertical centre (y = 76.2 mm).
  /// </para>
  ///
  /// <para>
  /// <b>Screen bands:</b> the LCD's active area is 119.89 mm tall and vertically centred on the
  /// panel, so each knob's panel height projects onto the 720 px viewport at
  /// <c>(panelY - 16.255) / 119.89 * 720</c>, giving 93.05 / 271.02 / 448.98 / 626.95 px. The bands
  /// below are those values rounded to the clean quarters of the 720 px axis. <b>The rounding is
  /// the specified layout, not a shortcut:</b> the largest deviation is 3.05 px, which is 0.508 mm
  /// on the panel at this screen's 6.006 px/mm, while the nearest wrong band is 178 px away - so
  /// the alignment a person actually reads ("beside this knob, not that one") is unaffected. The
  /// measured values were rejected because a number like 93.05 in a source file reads as a
  /// measurement and invites the next reader to re-measure or silently round it, where 90 reads as
  /// a layout decision.
  /// </para>
  /// </summary>
  public static readonly IReadOnlyList<EncoderPanelPosition> Encoders =
  [
    new() { Index = 0, EngravedName = "VOLUME", PanelCentreYMm = 31.75, HudBandYPx = 90 },
    new() { Index = 1, EngravedName = "SOURCE", PanelCentreYMm = 61.38, HudBandYPx = 270 },
    new() { Index = 2, EngravedName = "PRESETS", PanelCentreYMm = 91.02, HudBandYPx = 450 },
    new() { Index = 3, EngravedName = "TUNING", PanelCentreYMm = 120.65, HudBandYPx = 630 },
  ];

  /// <summary>
  /// The knob at <paramref name="encoderIndex"/>, with an out-of-range index clamped to the ends of
  /// the column rather than throwing.
  ///
  /// <para>
  /// Clamping rather than throwing because the index arrives over the wire: a HUD card from a host
  /// that reports a fifth encoder should land somewhere on screen, not take the render down.
  /// </para>
  /// </summary>
  public static EncoderPanelPosition ForIndex(int encoderIndex)
    => Encoders[Math.Clamp(encoderIndex, 0, EncoderCount - 1)];
}
