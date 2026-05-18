using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Radio.API.Hubs;
using Radio.API.Services;

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
}
