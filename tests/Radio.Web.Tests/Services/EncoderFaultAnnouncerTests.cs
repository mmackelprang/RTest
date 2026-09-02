using Radio.Web.Models;
using Radio.Web.Services;
using Xunit;

namespace Radio.Web.Tests.Services;

/// <summary>
/// Proves the notification latch (ENC-12 plan §0.5).
///
/// <para>
/// <b>The rule: each browser session announces each reportable severity at most once, and only on
/// escalation.</b> Configured and Transient are rank 0 and never announce; Degraded and Absent are
/// rank 1; HardFault is rank 2. The remembered rank is never reset — not on recovery, not on
/// reconnect — and that is the whole anti-storm property, named as a requirement in the punch list,
/// in encoder handoff §7.6, and in the task brief.
/// </para>
///
/// <para>
/// Every row of §0.5's table is a test below. The trade the rule makes — a fault that clears and
/// returns an hour later is silent the second time — is covered by the badge, which is stateless and
/// on screen for as long as the fault exists.
/// </para>
/// </summary>
public class EncoderFaultAnnouncerTests
{
  [Fact]
  public void AFlappingFault_AnnouncesExactlyOnce()
  {
    // The anti-storm property. Fifty transitions, one notification.
    var sut = new EncoderFaultAnnouncer();
    int announcements = 0;

    for (int i = 0; i < 25; i++)
    {
      if (sut.Evaluate("Degraded", isConnected: true, wasEverConnected: true) is not null)
      {
        announcements++;
      }

      if (sut.Evaluate("Configured", isConnected: true, wasEverConnected: true) is not null)
      {
        announcements++;
      }
    }

    Assert.Equal(1, announcements);
  }

  [Fact]
  public void EscalationFromDegradedToHardFault_SpeaksASecondTime()
  {
    // Strictly worse, and the volume knob has just been clamped. Worth interrupting for.
    var sut = new EncoderFaultAnnouncer();
    Assert.NotNull(sut.Evaluate("Degraded", true, true));
    Assert.NotNull(sut.Evaluate("HardFault", true, true));
  }

  [Fact]
  public void DeEscalation_IsSilent()
  {
    var sut = new EncoderFaultAnnouncer();
    sut.Evaluate("HardFault", true, true);
    Assert.Null(sut.Evaluate("Degraded", true, true));
    Assert.Null(sut.Evaluate("HardFault", true, true));
  }

  [Fact]
  public void AHardFaultWithNoPriorDegrade_AnnouncesOnce()
  {
    // §0.5 table row 7. The latch is "highest rank announced", not "every rank in turn", so a jump
    // straight to rank 2 must still speak — and then stop.
    var sut = new EncoderFaultAnnouncer();
    Assert.NotNull(sut.Evaluate("HardFault", true, true));
    Assert.Null(sut.Evaluate("HardFault", true, true));
    Assert.Null(sut.Evaluate("HardFault", true, true));
  }

  [Fact]
  public void AHealthyBootSaysNothing()
  {
    // Handoff §7.4. No toast, no splash, no banner. This is the single most important assertion in
    // the row: a status message for a thing that always succeeds trains people to ignore status
    // messages.
    var sut = new EncoderFaultAnnouncer();
    Assert.Null(sut.Evaluate("Transient", true, true));
    Assert.Null(sut.Evaluate("Transient", true, true));
    Assert.Null(sut.Evaluate("Configured", true, true));
  }

  [Fact]
  public void AbsentAtBoot_GetsNoToast_ButAbsentMidSessionDoes()
  {
    // The asymmetry ENC-0 added WasEverConnected for, finally consumed. Absent at boot means the
    // owner is most likely standing at the cabinet having just unplugged something.
    Assert.Null(new EncoderFaultAnnouncer().Evaluate("Unknown", isConnected: false, wasEverConnected: false));
    Assert.NotNull(new EncoderFaultAnnouncer().Evaluate("Unknown", isConnected: false, wasEverConnected: true));
  }

  [Fact]
  public void RecoveryIsAnnouncedOnlyForAnAbsenceWeAnnounced()
  {
    // Handoff §7.3: "announce a recovery only for a fault you announced."
    Assert.Null(new EncoderFaultAnnouncer().Evaluate("Configured", isConnected: true, wasEverConnected: true));

    var sut = new EncoderFaultAnnouncer();
    sut.Evaluate("Unknown", isConnected: false, wasEverConnected: true);
    Assert.NotNull(sut.Evaluate("Configured", isConnected: true, wasEverConnected: true));
  }

  [Fact]
  public void AFlappingUsbLead_AnnouncesAtMostOnceEachWay()
  {
    // Plan §0.4 C-3 — a deliberate narrowing of handoff §7.3, because a lead that bounces inside
    // furniture would otherwise produce exactly the storm §7.6 forbids.
    var sut = new EncoderFaultAnnouncer();
    int announcements = 0;
    for (int i = 0; i < 10; i++)
    {
      if (sut.Evaluate("Unknown", false, true) is not null)
      {
        announcements++;
      }

      if (sut.Evaluate("Configured", true, true) is not null)
      {
        announcements++;
      }
    }

    Assert.Equal(2, announcements);
  }

  [Fact]
  public void TheLatchIsPerInstance_SoASecondCircuitIsToldToo()
  {
    // Why the service is scoped rather than singleton (§2.2). A reload must not be permanently
    // silent about a fault that is still present, and a laptop opened during a UAT pass must hear
    // about it at all.
    var first = new EncoderFaultAnnouncer();
    Assert.NotNull(first.Evaluate("Degraded", true, true));
    Assert.NotNull(new EncoderFaultAnnouncer().Evaluate("Degraded", true, true));
  }
}
