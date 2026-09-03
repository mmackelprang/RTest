using Radio.Web.Models;
using Radio.Web.Services;
using Xunit;

namespace Radio.Web.Tests.Services;

/// <summary>
/// Proves the notification latch (ENC-12 plan §0.5).
///
/// <para>
/// <b>The rule is two independent latches: at most one config-fault notification per severity on
/// escalation, plus at most one disconnect and one reconnect notification, per browser session.</b>
/// Configured and Transient never announce; on the configuration ladder Degraded is rank 1 and
/// HardFault rank 2. Presence is latched separately and does not move that ladder, so a disconnect and
/// a Degraded in the same session are two notifications, not one. Nothing is ever reset — not on
/// recovery, not on reconnect — and that is the whole anti-storm property, named as a requirement in
/// the punch list, in encoder handoff §7.6, and in the task brief.
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
  public void PresenceAndConfigurationAreLatchedIndependently_AndNeitherRepeats()
  {
    // A DECISION, pinned here so it cannot be "tidied" into one ladder later.
    //
    // The rule as implemented is TWO latches, not one: the disconnect branch never touches
    // _highestAnnounced, and the configuration branch never consults the disconnect flags. So a
    // single session can produce a disconnect toast AND a Degraded toast — both of which plan §0.5's
    // single-ladder prose calls "rank 1", and which that prose therefore reads as a double
    // announcement. The implementation is verbatim the plan's own Task 6 code and the behaviour is
    // kept: presence and configuration are genuinely different facts about the cabinet, and the
    // session total stays bounded at a small number. It is §0.5's wording that was corrected.
    var sut = new EncoderFaultAnnouncer();
    int announcements = 0;

    void Feed(string? status, bool? isConnected)
    {
      if (sut.Evaluate(status, isConnected, wasEverConnected: true) is not null)
      {
        announcements++;
      }
    }

    Feed("Unknown", isConnected: false);     // "Knobs disconnected"
    Feed("Configured", isConnected: true);   // "Knobs connected"
    Feed("Degraded", isConnected: true);     // "Knob settings couldn't be applied"
    Assert.Equal(3, announcements);

    // And nothing repeats. Running the whole sequence again says nothing at all.
    Feed("Unknown", isConnected: false);
    Feed("Configured", isConnected: true);
    Feed("Degraded", isConnected: true);
    Assert.Equal(3, announcements);
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
