namespace RTLSDRCore;

/// <summary>
/// The hop rule of a station-list scan (AUD-100), kept free of the receiver so it can be tested on
/// its own: which station comes next, and when the scan has gone all the way round.
/// </summary>
public static class StationListScan
{
  /// <summary>
  /// The station after <paramref name="currentHz"/> in the scan's direction, wrapping at the end of
  /// the list as the live seek wraps at the band edge; null when no station other than the current
  /// one exists.
  /// </summary>
  /// <param name="stationsHz">Station frequencies, ascending, each at most once.</param>
  /// <param name="currentHz">The frequency the scan is on.</param>
  /// <param name="ascending">True for Scan Up.</param>
  /// <param name="minGapHz">
  /// A station closer than this to <paramref name="currentHz"/> is the current station, not the next
  /// one, so a radio tuned 10 kHz off a station does not hop 10 kHz onto it. 0 for exact matching only.
  /// </param>
  public static long? Next(IReadOnlyList<long> stationsHz, long currentHz, bool ascending, long minGapHz)
  {
    ArgumentNullException.ThrowIfNull(stationsHz);
    long gap = Math.Max(0, minGapHz);
    long? beyond = null;
    long? wrapped = null;
    foreach (long station in stationsHz)
    {
      if (Math.Abs(station - currentHz) < Math.Max(1, gap))
      {
        continue;
      }

      if (ascending)
      {
        if (station > currentHz)
        {
          beyond = beyond == null ? station : Math.Min(beyond.Value, station);
        }
        else
        {
          wrapped = wrapped == null ? station : Math.Min(wrapped.Value, station);
        }
      }
      else
      {
        if (station < currentHz)
        {
          beyond = beyond == null ? station : Math.Max(beyond.Value, station);
        }
        else
        {
          wrapped = wrapped == null ? station : Math.Max(wrapped.Value, station);
        }
      }
    }

    return beyond ?? wrapped;
  }

  /// <summary>
  /// True when a hop from <paramref name="currentHz"/> to <paramref name="nextHz"/> would reach or
  /// pass <paramref name="startHz"/>, where the scan began, or land closer than
  /// <paramref name="minGapHz"/> to it (the station the scan started on): it has gone all the way
  /// round and stops, as the live seek stops when it returns to its start.
  /// </summary>
  /// <remarks>
  /// A hop covers the arc from <paramref name="currentHz"/> (exclusive) to <paramref name="nextHz"/>
  /// (inclusive) in the scan's direction. A wrapping hop's arc runs through the band edge, so it
  /// covers a start above the top station or below the bottom one.
  /// </remarks>
  public static bool CompletesCircle(long startHz, long currentHz, long nextHz, bool ascending, long minGapHz)
  {
    if (Math.Abs(nextHz - startHz) < Math.Max(1, minGapHz))
    {
      return true;
    }

    if (ascending)
    {
      return nextHz > currentHz
        ? startHz > currentHz && startHz <= nextHz
        : startHz > currentHz || startHz <= nextHz;   // wrapped through the top edge
    }

    return nextHz < currentHz
      ? startHz < currentHz && startHz >= nextHz
      : startHz < currentHz || startHz >= nextHz;     // wrapped through the bottom edge
  }
}
