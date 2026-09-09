using Radio.Web.Models;

namespace Radio.Web.Services;

/// <summary>
/// The GV bridge health predicate (GV-12). Pure and static so it can be exercised with a
/// literal timestamp instead of a wall clock — the house rule from CLAUDE.md § Test Timing:
/// count events, never race one clock against another.
/// </summary>
public static class GvBridgeHealth
{
  /// <summary>
  /// How stale <see cref="GvBridgeStatusDto.LastApiSuccessAt"/> may be before the bridge is
  /// considered unhealthy. ~2 min per docs/queue/GV-12.md:46.
  /// </summary>
  public static readonly TimeSpan LastSuccessStaleAfter = TimeSpan.FromMinutes(2);

  /// <summary>
  /// True when the bridge looks usable. A null status (the poll itself failed) is unhealthy.
  /// </summary>
  /// <remarks>
  /// ⚠⚠ THE ASYMMETRY IS THE DESIGN. A field that is PRESENT and says "bad" makes this false;
  /// a field that is ABSENT contributes nothing. This predicate gates an unhealthy→healthy
  /// EDGE, so any term that can never clear would pin the state and silently disable the whole
  /// of GV-12 — see plan GV-12 §0.4 for the two live ways that happens. It is NOT a hedge and
  /// NOT laziness about unknowns.
  ///
  /// ⛔ Do not reuse this for a status BANNER without re-deriving it. A banner wants the
  /// opposite failure direction: unknown should read as wrong, loudly. Two predicates, two
  /// directions; give a banner its own rather than tightening this one.
  ///
  /// ⚠ Available is checked non-defensively on purpose: it is the one field RotaryPhone always
  /// sends, and the 2026-09-08 capture read `available:false` throughout the total outage while
  /// `degraded` and `authBlackout` both read false. If every other term is absent this
  /// degrades to `!Available`, which is sufficient to have caught that incident.
  /// </remarks>
  public static bool IsHealthy(GvBridgeStatusDto? status, DateTimeOffset now)
  {
    if (status is null)
    {
      return false;                       // the poll itself failed — GvBridgeStatusService
    }                                     // maps that to a null status (GvBridgeStatusService.cs:118)

    if (!status.Available)
    {
      return false;
    }

    if (status.CookiesValid == false || status.Degraded == true || status.AuthBlackout == true)
    {
      return false;                       // present AND bad. `== ` against bool? is deliberate:
    }                                     // null must not satisfy any of these.

    if (status.LastApiSuccessAt is DateTime last)
    {
      // Reported. Treat a stale success as unhealthy; treat ABSENT as no signal (see remarks).
      // DateTimeKind is not guaranteed on a deserialized value, so compare in UTC explicitly
      // rather than trusting the kind RotaryPhone happened to serialize.
      var lastUtc = last.Kind == DateTimeKind.Unspecified
        ? DateTime.SpecifyKind(last, DateTimeKind.Utc)
        : last.ToUniversalTime();
      if (now.UtcDateTime - lastUtc > LastSuccessStaleAfter)
      {
        return false;
      }
    }

    return true;
  }
}
