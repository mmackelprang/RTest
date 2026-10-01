namespace Radio.Infrastructure.Audio.Services;

/// <summary>
/// Maps the console's master volume to a Cast speaker's level while casting (AUD-81).
/// </summary>
/// <remarks>
/// <para>When casting starts the console's level (m0) and the speaker's level (s0) generally
/// differ: <c>AUD-80</c> restores the speaker's remembered level on connect, and <c>AUD-5</c> keeps
/// that reading out of master volume. Setting the speaker to the console's level on the first move
/// would therefore jump. This curve is continuous, monotonic and piecewise linear through the
/// anchor (m0, s0): the first move continues from where the speaker is, console 0 is silent, and
/// the curve is the identity when m0 equals s0.</para>
/// <para><b>Lower segment</b> (m ≤ m0): s = s0·m/m0, the straight line to silence.</para>
/// <para><b>Upper segment</b> (m ≥ m0): s = min(1, s0 + k·(m − m0)) with
/// k = min((1 − s0)/(1 − m0), <see cref="MaxUpperSlope"/>). Uncapped, k is the slope that reaches
/// full volume exactly at console 100 %, and it grows without bound as m0 approaches 1 — at
/// m0 = 0.98, s0 = 0.2 it is 40, so one encoder detent would raise the speaker 40 points. The cap
/// bounds every console step to at most <see cref="MaxUpperSlope"/> times its size in the loud
/// direction.</para>
/// <para><b>What the cap costs, stated plainly:</b> when it binds (the console was already near
/// the top while the speaker was well below it), the speaker does NOT reach 100 % from the
/// console on that connection — at console 100 % it is at s0 + 3·(1 − m0). A change made on the
/// speaker itself (its buttons, Google Home) re-anchors the curve, as does a new connection.</para>
/// <para><b>Why the lower segment is not capped.</b> A curve from (0, 0) to (m0, s0) with every
/// slope at most 3 exists only when s0 ≤ 3·m0, so a cap there must give up either continuity or
/// "console 0 is silent". Neither is worth it: the lower segment is steep only toward silence,
/// which is the safe direction.</para>
/// <para>Degenerate anchors: an unknown speaker level (NaN) or a console anchor at or below
/// <see cref="LowAnchorLimit"/> gives the identity (from a near-silent console, any jump is toward
/// quiet levels).</para>
/// </remarks>
public static class CastConsoleVolumeCurve
{
  /// <summary>Console anchors at or below this map with the identity.</summary>
  public const float LowAnchorLimit = 0.01f;

  /// <summary>
  /// The steepest the upper (louder-than-anchor) segment may be: one console point raises the
  /// speaker by at most this many points.
  /// </summary>
  public const float MaxUpperSlope = 3f;

  /// <summary>
  /// The speaker level (0-1) for console level <paramref name="console"/>, given the anchor
  /// (<paramref name="anchorConsole"/>, <paramref name="anchorSpeaker"/>). Inputs are clamped to
  /// 0-1; the result is always within 0-1.
  /// </summary>
  public static float Map(float console, float anchorConsole, float anchorSpeaker)
  {
    var m = float.IsNaN(console) ? 0f : Math.Clamp(console, 0f, 1f);

    if (float.IsNaN(anchorSpeaker) || float.IsNaN(anchorConsole) || anchorConsole <= LowAnchorLimit)
    {
      return m;
    }

    var m0 = Math.Clamp(anchorConsole, 0f, 1f);
    var s0 = Math.Clamp(anchorSpeaker, 0f, 1f);

    if (m <= m0)
    {
      return Math.Clamp(s0 * m / m0, 0f, 1f);
    }

    // m > m0 implies m0 < 1, so the division is defined; the cap also covers the near-vertical
    // case the division produces as m0 approaches 1.
    var slope = Math.Min((1f - s0) / (1f - m0), MaxUpperSlope);
    return Math.Clamp(s0 + slope * (m - m0), 0f, 1f);
  }
}
