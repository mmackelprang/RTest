using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Radio.Web.Models;
using Radio.Web.Services;

namespace Radio.Web.Tests.Services;

public class GvBridgeStatusServiceTests
{
  // The instant of the 2026-09-08 recovery, used as the fake clock's origin.
  private static readonly DateTimeOffset Origin =
    new(2026, 9, 8, 15, 31, 17, TimeSpan.Zero);

  private static GvBridgeStatusService NewService(TimeProvider clock) =>
    new(scopeFactory: null!, NullLogger<GvBridgeStatusService>.Instance,
        pollSeconds: 10, timeProvider: clock);

  [Fact]
  public void ApplyStatus_DerivesIsAvailable_AndFiresChange()
  {
    var svc = new GvBridgeStatusService(
      scopeFactory: null!, NullLogger<GvBridgeStatusService>.Instance, pollSeconds: 10);

    GvBridgeStatusDto? observed = null;
    var fired = 0;
    svc.StatusChanged += s => { observed = s; fired++; };

    // null status → degraded
    svc.ApplyStatusForTest(null);
    Assert.False(svc.IsAvailable);
    Assert.Equal(1, fired);

    // available
    svc.ApplyStatusForTest(new GvBridgeStatusDto { Available = true });
    Assert.True(svc.IsAvailable);
    Assert.Equal(2, fired);
    Assert.NotNull(observed);

    // no change in availability → still fires (UI may want fresh fields), but
    // IsAvailable holds
    svc.ApplyStatusForTest(new GvBridgeStatusDto { Available = true });
    Assert.True(svc.IsAvailable);
  }

  /// <summary>
  /// GV-12 — the TimeProvider seam is real, not decorative. Until this test existed the ctor
  /// parameter had no caller that passed anything but the default, so every "the staleness gate
  /// works" claim actually rested on <see cref="TimeProvider.System"/> and the suite could not
  /// have told the difference between a wired seam and a dead one.
  /// </summary>
  [Fact]
  public void IsHealthy_ReadsTheInjectedClock()
  {
    var clock = new FakeTimeProvider(Origin);
    var svc = NewService(clock);

    // Fresh: a success 30 s before the injected "now", well inside the 2-minute threshold.
    var status = new GvBridgeStatusDto
    {
      Available = true,
      ActiveMode = "GoogleVoice",
      LastApiSuccessAt = Origin.UtcDateTime.AddSeconds(-30)
    };
    svc.ApplyStatusForTest(status);
    Assert.True(svc.IsHealthy);

    // Stale: the SAME DTO, byte for byte. Nothing about the status changed — only the injected
    // clock moved past the threshold. It can only flip if the service reads _timeProvider, so a
    // regression that hard-codes DateTimeOffset.UtcNow fails here.
    clock.Advance(TimeSpan.FromMinutes(3));
    svc.ApplyStatusForTest(status);
    Assert.False(svc.IsHealthy);
  }

  /// <summary>
  /// Pins <see cref="GvBridgeHealth.LastSuccessStaleAfter"/> against RotaryPhone's real
  /// publishing cadence, so the 2× margin is a gate rather than an assumption.
  /// </summary>
  /// <remarks>
  /// ⚠ MEASURED ON THE APPLIANCE, NOT DERIVED. On `radio` at 2026-09-09T01:58Z–02:02Z (UTC —
  /// the box reports UTC; that is the evening of 2026-09-08 in the owner's EDT), polling
  /// http://localhost:5004/api/gvbridge/status, `lastApiSuccessAt` advanced on a clean
  /// 60-second cadence:
  ///
  ///   01:58:08.126  01:59:08.603  02:00:09.517  02:01:09.916  02:02:10.339
  ///
  /// Max observed age 60 s against a 120 s threshold — a 2× margin. ⚠ The margin is 2×, not
  /// large: this is ONE five-minute window on ONE day, and the cadence is RotaryPhone's to
  /// change without telling us.
  ///
  /// ⚠ WHAT BREAKS IF ROTARYPHONE EVER SLOWS THAT CADENCE PAST 120 s: a perfectly healthy
  /// bridge reports a timestamp older than the threshold on every poll, IsHealthy is pinned
  /// false, the unhealthy→healthy edge never occurs, and GV-12 silently stops refetching — the
  /// stuck-panel incident returns with a green suite and no error anywhere.
  ///
  /// ⛔ AND THIS TEST DOES NOT DETECT THAT — an earlier version of this remark claimed *"if the
  /// cadence changes, this fails"*, which is FALSE and was corrected by `KIOSK-3` after a
  /// reviewer read the body against the claim. There is no clock and no box here: just a
  /// FakeTimeProvider and a literal AddSeconds(-60). Nothing in it observes RotaryPhone, so a
  /// cadence slip cannot fail it. What it actually pins is the THRESHOLD — lower
  /// LastSuccessStaleAfter below the measured worst case and this goes red. That is worth
  /// having, and it is not the same guarantee.
  ///
  /// ⚠ A cadence slip past 120 s is therefore an UNMONITORED assumption, in this consumer and in
  /// the kiosk launcher that now shares the threshold (`radio-console-open`, GV_STALE_AFTER).
  /// If it ever slips, re-derive the threshold from the new measurement rather than nudging it
  /// until something passes.
  /// </remarks>
  [Fact]
  public void MeasuredSixtySecondCadence_StaysHealthy()
  {
    var clock = new FakeTimeProvider(Origin);
    var svc = NewService(clock);

    svc.ApplyStatusForTest(new GvBridgeStatusDto
    {
      Available = true,
      ActiveMode = "GoogleVoice",
      LastApiSuccessAt = Origin.UtcDateTime.AddSeconds(-60)   // the worst age actually observed
    });

    Assert.True(svc.IsHealthy);
  }
}
