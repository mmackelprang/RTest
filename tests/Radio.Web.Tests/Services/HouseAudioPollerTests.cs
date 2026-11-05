using System.Net;
using System.Threading.Channels;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Time.Testing;
using Radio.Web.Models;
using Radio.Web.Services;
using Radio.Web.Tests.TestHelpers;

namespace Radio.Web.Tests.Services;

/// <summary>
/// No test here waits on elapsed time (<c>CLAUDE.md</c> § Test Timing). The clock is fake, the
/// poller's timer is armed before <c>Start</c> returns, so <c>Advance</c> cannot arrive early, and
/// every wait is on a snapshot or a request. The only timeout is the deadlock guard.
/// </summary>
public sealed class HouseAudioPollerTests : IAsyncLifetime
{
  private static readonly TimeSpan Interval = TimeSpan.FromSeconds(3);

  private readonly FakeTimeProvider _time = new();
  private readonly FakeHouseController _controller = new();
  private readonly Channel<HouseAudioSnapshot> _snapshots = Channel.CreateUnbounded<HouseAudioSnapshot>();
  private readonly HouseAudioPoller _poller;

  public HouseAudioPollerTests()
  {
    _poller = new HouseAudioPoller(
      _controller.CreateClient(FakeHouseController.StoreWithPassword()), _time, Interval);
    _poller.Updated += snapshot => _snapshots.Writer.TryWrite(snapshot);
  }

  public Task InitializeAsync() => Task.CompletedTask;

  public async Task DisposeAsync() => await _poller.DisposeAsync();

  private async Task<HouseAudioSnapshot> NextSnapshotAsync() =>
    await _snapshots.Reader.ReadAsync().AsTask().WaitAsync(RecordingHandler.RendezvousTimeout);

  private static bool IsSystemPoll(HttpMethod method, string path) =>
    method == HttpMethod.Get && path == "/system";

  private static bool IsZonesPoll(HttpMethod method, string path) =>
    method == HttpMethod.Get && path == "/zones";

  private static bool IsSourcesPoll(HttpMethod method, string path) =>
    method == HttpMethod.Get && path == "/sources";

  private static HouseZone Zone(HouseAudioSnapshot snapshot, string name) =>
    snapshot.Zones.Single(zone => zone.Name == name);

  [Fact]
  public async Task Poller_FirstPoll_ReportsTheWholeController()
  {
    _poller.Start();

    HouseAudioSnapshot snapshot = await NextSnapshotAsync();

    Assert.Equal(HouseAudioHealth.Ok, snapshot.Health);
    Assert.False(snapshot.Stale);
    Assert.Equal("Mackelhome Controller", snapshot.System!.Name);
    Assert.Equal(2, snapshot.Zones.Count);
    Assert.Equal(2, snapshot.Sources.Count);
  }

  [Fact]
  public async Task Poller_PollsAgain_OnEachInterval()
  {
    _poller.Start();
    await NextSnapshotAsync();

    _controller.SetVolume("Kitchen", 60);
    _time.Advance(Interval);

    Assert.Equal(60, Zone(await NextSnapshotAsync(), "Kitchen").Volume);
  }

  [Fact]
  public async Task Poller_FailedPoll_KeepsLastZones_AndMarksStale()
  {
    _poller.Start();
    HouseAudioSnapshot good = await NextSnapshotAsync();

    _controller.Failure = new HttpRequestException("connection refused");
    _time.Advance(Interval);
    HouseAudioSnapshot failed = await NextSnapshotAsync();

    Assert.Equal(HouseAudioHealth.Unreachable, failed.Health);
    Assert.True(failed.Stale);
    Assert.Equal(good.Zones, failed.Zones);
    Assert.Equal(good.Sources, failed.Sources);
    Assert.Equal(good.System, failed.System);

    // It keeps polling, and comes back on its own.
    _controller.Failure = null;
    _time.Advance(Interval);
    HouseAudioSnapshot recovered = await NextSnapshotAsync();

    Assert.Equal(HouseAudioHealth.Ok, recovered.Health);
    Assert.False(recovered.Stale);
  }

  [Fact]
  public async Task Poller_FailedFirstPoll_HasNothingToKeep()
  {
    _controller.ForcedStatus = HttpStatusCode.Unauthorized;
    _poller.Start();

    HouseAudioSnapshot snapshot = await NextSnapshotAsync();

    Assert.Equal(HouseAudioHealth.WrongPassword, snapshot.Health);
    Assert.True(snapshot.Stale);
    Assert.Null(snapshot.System);
    Assert.Empty(snapshot.Zones);
    Assert.Empty(snapshot.Sources);
  }

  [Fact]
  public async Task Poller_AmpLinkDown_NamesIt_AndCarriesTheFreshSystemInfo()
  {
    _poller.Start();
    HouseAudioSnapshot good = await NextSnapshotAsync();

    _controller.AmpLinkDown = true;
    _time.Advance(Interval);
    HouseAudioSnapshot down = await NextSnapshotAsync();

    Assert.Equal(HouseAudioHealth.AmpDisconnected, down.Health);
    Assert.True(down.Stale);
    Assert.False(down.System!.SerialConnected);
    Assert.Equal(good.Zones, down.Zones);
  }

  [Fact]
  public async Task Poller_WithNoPassword_ReportsIt_WithoutCallingOut()
  {
    FakeHouseController controller = new();
    HouseAudioPasswordStore noPassword =
      FakeHouseController.CreateStore(new EphemeralDataProtectionProvider(), null, new RecordingHandler());
    await using HouseAudioPoller poller =
      new(controller.CreateClient(noPassword), _time, Interval);
    Channel<HouseAudioSnapshot> snapshots = Channel.CreateUnbounded<HouseAudioSnapshot>();
    poller.Updated += snapshot => snapshots.Writer.TryWrite(snapshot);

    poller.Start();
    HouseAudioSnapshot snapshot =
      await snapshots.Reader.ReadAsync().AsTask().WaitAsync(RecordingHandler.RendezvousTimeout);

    Assert.Equal(HouseAudioHealth.NoPassword, snapshot.Health);
    Assert.Empty(controller.Handler.Requests);
  }

  [Fact]
  public async Task Poller_StopsCalling_AfterDispose()
  {
    _poller.Start();
    await NextSnapshotAsync();
    await _poller.DisposeAsync();
    int requestsAtDispose = _controller.Handler.Requests.Count;

    _time.Advance(Interval * 5);

    // DisposeAsync has awaited the loop and stopped the timer, so nothing is left to race this read.
    Assert.Equal(requestsAtDispose, _controller.Handler.Requests.Count);
  }

  [Fact]
  public async Task Poller_Dispose_CancelsAPollInFlight_AndRaisesNothingAfterwards()
  {
    _controller.ZonesGate = new TaskCompletionSource();
    _poller.Start();
    await _controller.Handler.WaitForAsync(IsZonesPoll, 1);

    await _poller.DisposeAsync();

    Assert.False(_snapshots.Reader.TryRead(out _));
  }

  [Fact]
  public async Task Poller_Dispose_IsSafeTwice_AndWithoutStart()
  {
    await _poller.DisposeAsync();
    await _poller.DisposeAsync();

    _poller.Start();

    Assert.Empty(_controller.Handler.Requests);
  }

  [Fact]
  public async Task Poller_SlowPoll_DoesNotOverlapTheNext()
  {
    TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
    _controller.SystemGate = gate;
    _poller.Start();
    await _controller.Handler.WaitForAsync(IsSystemPoll, 1);

    // Advance runs due timer callbacks inline, so a poller that polls from its timer callback would
    // have sent its second request before this returns.
    _time.Advance(Interval * 3);
    Assert.Equal(1, _controller.Handler.Count(IsSystemPoll));

    _controller.SystemGate = null;
    gate.SetResult();
    await NextSnapshotAsync();

    // The ticks that passed during the slow poll collapse into one, not three.
    await NextSnapshotAsync();
    await _poller.DisposeAsync();
    Assert.Equal(2, _controller.Handler.Count(IsSystemPoll));
  }

  [Fact]
  public async Task Poller_Start_Twice_RunsOneLoop()
  {
    _poller.Start();
    _poller.Start();
    await NextSnapshotAsync();

    _time.Advance(Interval);
    await NextSnapshotAsync();
    await _poller.DisposeAsync();

    Assert.Equal(2, _controller.Handler.Count(IsSystemPoll));
  }

  [Fact]
  public async Task Poller_PollDuringAHeldZone_DoesNotOverwriteThatZone()
  {
    _poller.Start();
    await NextSnapshotAsync();
    IDisposable hold = _poller.HoldZone("Kitchen");

    _controller.SetVolume("Kitchen", 60);
    _controller.SetVolume("Den", 60);
    _time.Advance(Interval);
    HouseAudioSnapshot held = await NextSnapshotAsync();

    Assert.Equal(20, Zone(held, "Kitchen").Volume);
    Assert.Equal(60, Zone(held, "Den").Volume);

    // Once released, the next poll shows the truth.
    hold.Dispose();
    _time.Advance(Interval);
    Assert.Equal(60, Zone(await NextSnapshotAsync(), "Kitchen").Volume);
  }

  // The tests below park the second poll on /zones, change something, then let it finish. Reaching
  // /sources proves that poll has read its zones, so a volume set after that rendezvous can only be
  // seen by a later poll: the value in the next snapshot says which poll raised it.
  private async Task<TaskCompletionSource> ParkSecondPollOnZonesAsync()
  {
    TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
    _controller.ZonesGate = gate;
    _time.Advance(Interval);
    await _controller.Handler.WaitForAsync(IsZonesPoll, 2);
    _controller.ZonesGate = null;
    return gate;
  }

  private async Task FinishParkedPollAsync(TaskCompletionSource gate)
  {
    gate.SetResult();
    await _controller.Handler.WaitForAsync(IsSourcesPoll, 2);
  }

  [Fact]
  public async Task Poller_SnapshotAfterARelease_ComesFromAPollThatBeganAfterIt()
  {
    _poller.Start();
    await NextSnapshotAsync();
    IDisposable hold = _poller.HoldZone("Kitchen");
    TaskCompletionSource gate = await ParkSecondPollOnZonesAsync();

    hold.Dispose();
    _controller.SetVolume("Kitchen", 30);
    await FinishParkedPollAsync(gate);
    _controller.SetVolume("Kitchen", 60);
    _time.Advance(Interval);

    // The poll that spanned the release read 30, and must not be reported at all.
    Assert.Equal(60, Zone(await NextSnapshotAsync(), "Kitchen").Volume);
  }

  [Fact]
  public async Task Poller_HoldTakenAndReleasedInsideOnePoll_DiscardsThatPoll()
  {
    _poller.Start();
    await NextSnapshotAsync();
    TaskCompletionSource gate = await ParkSecondPollOnZonesAsync();

    _poller.HoldZone("Kitchen").Dispose();
    _controller.SetVolume("Kitchen", 30);
    await FinishParkedPollAsync(gate);
    _controller.SetVolume("Kitchen", 60);
    _time.Advance(Interval);

    Assert.Equal(60, Zone(await NextSnapshotAsync(), "Kitchen").Volume);
  }

  [Fact]
  public async Task Poller_FailedPollThatSpansARelease_IsStillRaised()
  {
    _poller.Start();
    await NextSnapshotAsync();
    IDisposable hold = _poller.HoldZone("Kitchen");
    TaskCompletionSource gate = await ParkSecondPollOnZonesAsync();

    hold.Dispose();
    _controller.Failure = new HttpRequestException("connection refused");
    gate.SetResult();

    HouseAudioSnapshot failed = await NextSnapshotAsync();
    Assert.Equal(HouseAudioHealth.Unreachable, failed.Health);
    Assert.True(failed.Stale);
  }

  [Fact]
  public async Task Poller_FailedPollDuringAHold_IsStillRaised()
  {
    _poller.Start();
    await NextSnapshotAsync();
    using IDisposable hold = _poller.HoldZone("Kitchen");

    _controller.Failure = new HttpRequestException("connection refused");
    _time.Advance(Interval);

    Assert.Equal(HouseAudioHealth.Unreachable, (await NextSnapshotAsync()).Health);
  }

  [Fact]
  public async Task Poller_HoldTakenWhileAPollIsInFlight_StillApplies()
  {
    _poller.Start();
    await NextSnapshotAsync();

    TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
    _controller.ZonesGate = gate;
    _controller.SetVolume("Kitchen", 60);
    _time.Advance(Interval);
    await _controller.Handler.WaitForAsync(IsZonesPoll, 2);

    using IDisposable hold = _poller.HoldZone("Kitchen");
    _controller.ZonesGate = null;
    gate.SetResult();

    Assert.Equal(20, Zone(await NextSnapshotAsync(), "Kitchen").Volume);
  }

  [Fact]
  public async Task Poller_TwoHoldsOnOneZone_LastReleaseEndsIt()
  {
    _poller.Start();
    await NextSnapshotAsync();
    IDisposable first = _poller.HoldZone("Kitchen");
    IDisposable second = _poller.HoldZone("Kitchen");

    _controller.SetVolume("Kitchen", 60);
    first.Dispose();
    first.Dispose();
    _time.Advance(Interval);
    Assert.Equal(20, Zone(await NextSnapshotAsync(), "Kitchen").Volume);

    second.Dispose();
    _time.Advance(Interval);
    Assert.Equal(60, Zone(await NextSnapshotAsync(), "Kitchen").Volume);
  }

  [Fact]
  public async Task Poller_AZoneWithNoName_CannotBreakAHold()
  {
    _controller.HasUnnamedZone = true;
    _poller.Start();
    await NextSnapshotAsync();
    using IDisposable hold = _poller.HoldZone("Kitchen");

    _time.Advance(Interval);

    Assert.Equal(2, (await NextSnapshotAsync()).Zones.Count);
  }

  [Fact]
  public async Task Poller_ThrowingSubscriber_DoesNotStopPolling()
  {
    _poller.Updated += _ => throw new InvalidOperationException("a broken panel");
    _poller.Start();
    await NextSnapshotAsync();

    _time.Advance(Interval);

    await NextSnapshotAsync();
  }
}
