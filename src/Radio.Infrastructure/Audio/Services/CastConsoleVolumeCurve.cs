namespace Radio.Infrastructure.Audio.Services;

/// <summary>
/// Maps the console's master volume to a Cast speaker's level while casting (AUD-81).
/// </summary>
/// <remarks>
/// <para>When casting starts the console's level (m0) and the speaker's level (s0) generally
/// differ: <c>AUD-80</c> restores the speaker's remembered level on connect, and <c>AUD-5</c> keeps
/// that reading out of master volume. Setting the speaker to the console's level on the first move
/// would therefore jump. This curve is monotonic and piecewise linear through (0,0), (m0,s0) and
/// (1,1): the first move continues from where the speaker is, 0 is silent, full volume is reachable,
/// and the curve is the identity when m0 equals s0.</para>
/// <para>Degenerate anchors: an unknown speaker level (NaN) or a console anchor at or below
/// <see cref="LowAnchorLimit"/> gives the identity (from a near-silent console, any jump is toward
/// quiet levels); a console anchor at or above <see cref="HighAnchorLimit"/> uses only the lower
/// segment, because the upper one would be a near-vertical step.</para>
/// </remarks>
public static class CastConsoleVolumeCurve
{
  /// <summary>Console anchors at or below this map with the identity.</summary>
  public const float LowAnchorLimit = 0.01f;

  /// <summary>Console anchors at or above this map with the lower segment only.</summary>
  public const float HighAnchorLimit = 0.99f;

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

    if (m <= m0 || m0 >= HighAnchorLimit)
    {
      return Math.Clamp(s0 * m / m0, 0f, 1f);
    }

    return Math.Clamp(s0 + (1f - s0) * (m - m0) / (1f - m0), 0f, 1f);
  }
}
