using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Radio.Web.Models;
using Radio.Web.Services;
using Xunit;

namespace Radio.Web.Tests.Services;

/// <summary>
/// Covers the observable surface of <see cref="BellHealthService"/> that MainLayout
/// binds to. The poll loop itself is not exercised here (it needs a live
/// RotaryPhone.API); <see cref="BellHealthService.Publish"/> is the same code path the
/// loop uses to apply a fetched status, so the state machine is fully covered.
/// </summary>
public class BellHealthServiceTests
{
  private static BellHealthService NewService() =>
    new(scopeFactory: null!, NullLogger<BellHealthService>.Instance);

  private static PhoneSystemStatusDto Status(bool? reachable) =>
    new() { Ht801Reachable = reachable };

  [Fact]
  public void InitialHealth_IsUnknown_SoTheBadgeStaysDarkBeforeTheFirstPoll()
  {
    NewService().Health.Should().Be(BellHealth.Unknown);
  }

  [Fact]
  public void Publish_NullStatus_FromColdStart_StaysUnknown_AndDoesNotAlarm()
  {
    // A failed fetch before we have ever learned anything must not invent a fault.
    var svc = NewService();

    svc.Publish(null);

    svc.Health.Should().Be(BellHealth.Unknown);
    BellHealthRules.IsFaulted(svc.Health).Should().BeFalse();
  }

  [Fact]
  public void Publish_ReachableNull_IsUnknown_NotSuspect()
  {
    var svc = NewService();

    svc.Publish(Status(null));

    svc.Health.Should().Be(BellHealth.Unknown);
  }

  [Fact]
  public void Publish_Unreachable_BecomesSuspect_AndRaisesHealthChanged()
  {
    var svc = NewService();
    var raised = new List<BellHealth>();
    svc.HealthChanged += raised.Add;

    svc.Publish(Status(false));

    svc.Health.Should().Be(BellHealth.Suspect);
    raised.Should().ContainSingle().Which.Should().Be(BellHealth.Suspect);
  }

  [Fact]
  public void Publish_SameValueTwice_RaisesOnce()
  {
    // MainLayout re-renders on every HealthChanged. A poll every 15s that always
    // notified would be a needless re-render of the whole layout forever.
    var svc = NewService();
    var raised = 0;
    svc.HealthChanged += _ => raised++;

    svc.Publish(Status(false));
    svc.Publish(Status(false));
    svc.Publish(Status(false));

    raised.Should().Be(1);
  }

  [Fact]
  public void Publish_Recovery_ClearsTheFault()
  {
    // A badge that outlives the fault it points at is the same confidently-wrong
    // screen this work exists to remove.
    var svc = NewService();
    svc.Publish(Status(false));
    svc.Health.Should().Be(BellHealth.Suspect);

    svc.Publish(Status(true));

    svc.Health.Should().Be(BellHealth.Ok);
    BellHealthRules.IsFaulted(svc.Health).Should().BeFalse();
  }

  [Fact]
  public void Publish_FailedFetchAfterAFault_RetainsTheFault()
  {
    // A dropped request is not evidence in either direction. Clearing the fault here
    // would let one network blip switch the topbar badge off while the bell is still
    // dead, then switch it back on at the next poll — and an indicator that flickers is
    // worse than no indicator. Distinct from Ht801Reachable == null, which is the server
    // explicitly saying "not probed" (covered by Publish_ReachableNull_IsUnknown...).
    var svc = NewService();
    var raised = 0;
    svc.Publish(Status(false));
    svc.HealthChanged += _ => raised++;

    svc.Publish(null);

    svc.Health.Should().Be(BellHealth.Suspect);
    raised.Should().Be(0, "a failed fetch must not even notify, let alone clear");
  }

  [Fact]
  public void Publish_ServerReportsNullAfterAFault_DoesClearToUnknown()
  {
    // The other half of the distinction: an actual response carrying a null reachability
    // IS information — the server is telling us it can no longer determine the state.
    var svc = NewService();
    svc.Publish(Status(false));

    svc.Publish(Status(null));

    svc.Health.Should().Be(BellHealth.Unknown);
    BellHealthRules.IsFaulted(svc.Health).Should().BeFalse();
  }

  [Fact]
  public void Publish_ConcurrentIdenticalTransitions_RaisesExactlyOnce()
  {
    // Unlike GvBridgeStatusService (single sequential poll loop), this service has
    // several writers: its own loop plus every open /phone circuit's 5s timer and its
    // Task.Run-wrapped SystemStatusChanged handler. The compare-and-set in Apply() is
    // gated so overlapping writers cannot double-fire HealthChanged.
    var svc = NewService();
    var raised = 0;
    svc.HealthChanged += _ => Interlocked.Increment(ref raised);
    var status = Status(false);

    Parallel.For(0, 128, _ => svc.Publish(status));

    svc.Health.Should().Be(BellHealth.Suspect);
    raised.Should().Be(1);
  }

  [Fact]
  public void Publish_ConcurrentMixedValues_LeavesAConsistentTerminalState()
  {
    // Whatever interleaving occurs, Health must end up as one of the two published
    // values — never a torn or default value — and must agree with the last applied
    // transition rather than drifting.
    var svc = NewService();
    var seen = new System.Collections.Concurrent.ConcurrentBag<BellHealth>();
    svc.HealthChanged += h => seen.Add(h);

    Parallel.For(0, 128, i => svc.Publish(Status(i % 2 == 0)));

    svc.Health.Should().BeOneOf(BellHealth.Ok, BellHealth.Suspect);
    seen.Should().OnlyContain(h => h == BellHealth.Ok || h == BellHealth.Suspect);
    seen.Should().NotBeEmpty();
  }

  [Fact]
  public async Task DisposeAsync_WithoutStart_IsSafe()
  {
    var svc = NewService();

    var act = async () => await svc.DisposeAsync();

    await act.Should().NotThrowAsync();
  }
}
