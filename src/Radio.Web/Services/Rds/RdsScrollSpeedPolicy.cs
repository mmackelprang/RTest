namespace Radio.Web.Services.Rds;

/// <summary>
/// Modest adaptive-speed policy for the RDS ticker: when the accumulated
/// buffer backlog is large, scroll a little faster so a burst of new chunks
/// (a song change publishes up to ~64 chars at once) doesn't stretch the
/// time-to-display of the newest text.
/// </summary>
/// <remarks>
/// The buffer cap already bounds the worst-case backlog, so this is a
/// latency optimisation, not a correctness requirement. The boost is applied
/// by <c>RadioControlPanel</c> on top of the user-configured
/// <c>RtScrollSpeedPxPerSec</c> and flows through the normal speed parameter
/// — the JS engine restarts its animation leg from the preserved offset on a
/// speed change, so the transition is seamless (no jump). Kept deliberately
/// simple (single threshold, single factor) and unit-testable.
/// </remarks>
public static class RdsScrollSpeedPolicy
{
  /// <summary>
  /// Buffer-fill fraction above which the catch-up boost engages. 75% of the
  /// cap means the ticker is carrying nearly its full history — reading pace
  /// matters less than surfacing the newest chunk.
  /// </summary>
  public const double BacklogThresholdFraction = 0.75;

  /// <summary>
  /// Speed multiplier while above the threshold. 1.5× keeps 40 px/s → 60 px/s
  /// — news-ticker pace, still comfortably readable (HANDOFF §5 cites 60–80
  /// px/s as the old-school news-ticker range).
  /// </summary>
  public const double CatchUpFactor = 1.5;

  /// <summary>
  /// Effective scroll speed for the current buffer state.
  /// </summary>
  /// <param name="baseSpeedPxPerSec">Configured RtScrollSpeedPxPerSec.</param>
  /// <param name="bufferLength">Current accumulated buffer length in chars.</param>
  /// <param name="maxChars">The buffer's configured cap.</param>
  public static int EffectiveSpeed(int baseSpeedPxPerSec, int bufferLength, int maxChars)
  {
    if (maxChars <= 0 || bufferLength <= maxChars * BacklogThresholdFraction)
    {
      return baseSpeedPxPerSec;
    }

    return (int)Math.Round(baseSpeedPxPerSec * CatchUpFactor);
  }
}
