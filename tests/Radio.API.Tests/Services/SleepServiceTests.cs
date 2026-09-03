using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Radio.API.Hubs;
using Radio.API.Services;
using Radio.Core.Interfaces;

namespace Radio.API.Tests.Services;

/// <summary>
/// Tests for <see cref="SleepService"/>'s SignalR broadcast contract.
/// PR D #25 of the Arc follow-up backlog — verifies the server broadcasts
/// <c>SleepStateChanged</c> with the correct <c>bool</c> payload when sleep
/// state changes. The Web's <c>AudioStateHubService</c> already subscribes
/// to this event (Arc 1 PR 6), so confirming the server side fires it
/// closes the round-trip.
/// </summary>
public class SleepServiceTests
{
  private static (SleepService service, Mock<IClientProxy> allClients) CreateService()
  {
    var hubContextMock = new Mock<IHubContext<AudioStateHub>>();
    var clientsMock = new Mock<IHubClients>();
    var allClientsMock = new Mock<IClientProxy>();
    clientsMock.SetupGet(c => c.All).Returns(allClientsMock.Object);
    hubContextMock.SetupGet(h => h.Clients).Returns(clientsMock.Object);

    var service = new SleepService(
      NullLogger<SleepService>.Instance,
      hubContextMock.Object,
      audioManager: null);

    return (service, allClientsMock);
  }

  [Fact]
  public async Task EnterSleepAsync_BroadcastsSleepStateChangedTrue()
  {
    var (service, allClients) = CreateService();

    await service.EnterSleepAsync();

    allClients.Verify(
      c => c.SendCoreAsync(
        "SleepStateChanged",
        It.Is<object?[]>(args => MatchesBool(args, true)),
        It.IsAny<CancellationToken>()),
      Times.Once);

    Assert.True(service.IsSleeping);
  }

  [Fact]
  public async Task WakeAsync_BroadcastsSleepStateChangedFalse()
  {
    var (service, allClients) = CreateService();

    // Pre-condition: must be sleeping before wake fires.
    await service.EnterSleepAsync();
    allClients.Invocations.Clear();

    await service.WakeAsync("test");

    allClients.Verify(
      c => c.SendCoreAsync(
        "SleepStateChanged",
        It.Is<object?[]>(args => MatchesBool(args, false)),
        It.IsAny<CancellationToken>()),
      Times.Once);

    Assert.False(service.IsSleeping);
  }

  // Helper — Moq expression trees can't contain pattern-matching, so the
  // predicate body lives in a regular static method.
  private static bool MatchesBool(object?[] args, bool expected)
  {
    if (args == null || args.Length != 1)
    {
      return false;
    }
    var first = args[0];
    if (first is bool b)
    {
      return b == expected;
    }
    return false;
  }

  [Fact]
  public async Task EnterSleepAsync_AlreadySleeping_DoesNotRebroadcast()
  {
    var (service, allClients) = CreateService();

    await service.EnterSleepAsync();
    allClients.Invocations.Clear();

    // Second call should no-op.
    await service.EnterSleepAsync();

    allClients.Verify(
      c => c.SendCoreAsync(
        "SleepStateChanged",
        It.IsAny<object?[]>(),
        It.IsAny<CancellationToken>()),
      Times.Never);
  }

  [Fact]
  public async Task WakeAsync_NotSleeping_DoesNotRebroadcast()
  {
    var (service, allClients) = CreateService();

    // Not sleeping — wake should no-op.
    await service.WakeAsync("test");

    allClients.Verify(
      c => c.SendCoreAsync(
        "SleepStateChanged",
        It.IsAny<object?[]>(),
        It.IsAny<CancellationToken>()),
      Times.Never);
  }

  // --- ENC-6: the three states, and the wake claim latch -------------------------------------

  [Fact]
  public void WakeState_WithNoSleepScreenAndNotSleeping_IsAwake()
  {
    var (service, _) = CreateService();

    Assert.Equal(ConsoleWakeState.Awake, service.WakeState);
    Assert.False(service.IsSleepScreenVisible);
  }

  [Fact]
  public void WakeState_WithTheSleepScreenUpAndAudioPlaying_IsAmbient()
  {
    // The overnight state, and the one the machine actually reaches: the browser idled onto /sleep
    // and nothing paused audio.
    var (service, _) = CreateService();

    service.SetSleepScreenVisible(true);

    Assert.Equal(ConsoleWakeState.Ambient, service.WakeState);
    Assert.False(service.IsSleeping);
  }

  [Fact]
  public async Task WakeState_WhenSleeping_IsStandbyEvenBeforeTheScreenReportsItself()
  {
    // Standby is defined by audio being parked, not by a browser having caught up. The pill calls
    // the API and only then navigates, so there is a real window where IsSleeping is true and no
    // client has reported the route yet - a knob turned in that window must not act.
    var (service, _) = CreateService();

    await service.EnterSleepAsync();

    Assert.Equal(ConsoleWakeState.Standby, service.WakeState);
    Assert.False(service.IsSleepScreenVisible);
  }

  [Fact]
  public void TryClaimWake_WhenAwake_ReturnsFalseAndBurnsNoClaim()
  {
    var (service, _) = CreateService();

    Assert.False(service.TryClaimWake());
    Assert.Equal(ConsoleWakeState.Awake, service.WakeState);
  }

  [Fact]
  public void TryClaimWake_GrantsExactlyOneClaim_AndTheStateReadsAwakeFromThatInstant()
  {
    // The latch, and the whole reason it exists: with a 10 ms poll, a dozen detents arrive before
    // the browser has left /sleep. Exactly one is spent waking; the rest must find an awake console
    // and act. A fast spin loses one detent, not twelve.
    var (service, _) = CreateService();
    service.SetSleepScreenVisible(true);

    Assert.True(service.TryClaimWake());
    Assert.Equal(ConsoleWakeState.Awake, service.WakeState);
    Assert.False(service.TryClaimWake());
  }

  [Fact]
  public void SetSleepScreenVisible_False_ReleasesTheClaim()
  {
    // The claim is released by the browser confirming it left /sleep, not by WakeAsync finishing:
    // WakeAsync completes while the page is still up, and releasing there would drop the console
    // straight back into Ambient and start consuming inputs again.
    var (service, _) = CreateService();
    service.SetSleepScreenVisible(true);
    Assert.True(service.TryClaimWake());

    service.SetSleepScreenVisible(false);

    Assert.Equal(ConsoleWakeState.Awake, service.WakeState);
    service.SetSleepScreenVisible(true);
    Assert.Equal(ConsoleWakeState.Ambient, service.WakeState);
  }

  [Fact]
  public async Task EnterSleepAsync_ReleasesAnOutstandingClaim()
  {
    // Otherwise a wake that was claimed and never confirmed would leave the console permanently
    // reading Awake, and the next Standby would not consume anything.
    var (service, _) = CreateService();
    service.SetSleepScreenVisible(true);
    Assert.True(service.TryClaimWake());

    await service.EnterSleepAsync();

    Assert.Equal(ConsoleWakeState.Standby, service.WakeState);
  }
}
