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
  public void Publish_NullStatus_StaysUnknown_AndDoesNotAlarm()
  {
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
  public void Publish_FaultThenUnknown_StopsAlarming()
  {
    // Losing contact with RotaryPhone.API must not pin a stale fault on the topbar.
    var svc = NewService();
    svc.Publish(Status(false));

    svc.Publish(null);

    svc.Health.Should().Be(BellHealth.Unknown);
    BellHealthRules.IsFaulted(svc.Health).Should().BeFalse();
  }

  [Fact]
  public async Task DisposeAsync_WithoutStart_IsSafe()
  {
    var svc = NewService();

    var act = async () => await svc.DisposeAsync();

    await act.Should().NotThrowAsync();
  }
}
