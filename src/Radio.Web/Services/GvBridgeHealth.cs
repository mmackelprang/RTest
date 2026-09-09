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
    }                                     // .PollOnceAsync's catch block maps that to a null status

    if (!status.Available)
    {
      return false;
    }

    if (status.CookiesValid == false || status.Degraded == true || status.AuthBlackout == true)
    {
      return false;                       // present AND bad. `== ` against bool? is deliberate:
    }                                     // null must not satisfy any of these.

    // Reported AND interpretable. Treat a stale success as unhealthy; treat ABSENT as no signal
    // (see remarks) — and treat an UNSPECIFIED DateTimeKind as no signal too, for the same
    // reason.
    //
    // ⚠⚠ UNSPECIFIED IS SKIPPED, NOT ASSUMED UTC, AND THE DIFFERENCE IS A SILENT NO-OP.
    // An unmarked timestamp is one that arrived without a `Z`, which means we do not know what
    // clock produced it. RotaryPhone runs in EDT, so reading such a value as UTC would place it
    // 4 hours in the past — permanently beyond LastSuccessStaleAfter, pinning this predicate at
    // unhealthy, so the unhealthy→healthy edge never fires and GV-12 stops working with a green
    // suite and no error anywhere. Guessing a kind is exactly the "a field we did not receive is
    // evidence of ill health" mistake §0.4 exists to prevent; a value we cannot interpret gets
    // the same answer as a value we did not get. Only Utc (no-op) and Local (converted) are
    // trusted enough to gate on.
    if (status.LastApiSuccessAt is DateTime last && last.Kind != DateTimeKind.Unspecified)
    {
      var lastUtc = last.ToUniversalTime();
      if (now.UtcDateTime - lastUtc > LastSuccessStaleAfter)
      {
        return false;
      }
    }

    return true;
  }
}
