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
  /// </summary>
  public const int HudHoldMs = 1500;

  /// <summary>
  /// Minimum interval between coalesced HUD broadcasts, in milliseconds (20 Hz).
  ///
  /// <para>
  /// Trailing-edge, always emitting the final value. The audio action itself is not throttled —
  /// volume applies per event at full rate; only the broadcast and render are coalesced.
  /// </para>
  /// </summary>
  public const int HudCoalesceMs = 50;
}
