using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Radio.API.Hubs;
using Radio.API.Services;
using Radio.Core.Interfaces;
using Radio.Core.Interfaces.Audio;

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
  private static (SleepService service, Mock<IClientProxy> allClients) CreateService(
    IAudioManager? audioManager = null,
    IEventPlaybackService? eventPlayback = null)
  {
    var hubContextMock = new Mock<IHubContext<AudioStateHub>>();
    var clientsMock = new Mock<IHubClients>();
    var allClientsMock = new Mock<IClientProxy>();
    clientsMock.SetupGet(c => c.All).Returns(allClientsMock.Object);
    hubContextMock.SetupGet(h => h.Clients).Returns(clientsMock.Object);

    var service = new SleepService(
      NullLogger<SleepService>.Instance,
      hubContextMock.Object,
      audioManager,
      eventPlayback);

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

  // --- ENC-6: WakeAsync can wake from Ambient, where audio was never parked --------------------

  [Fact]
  public async Task WakeAsync_FromAmbient_BroadcastsTheWakeEvenThoughAudioWasNeverParked()
  {
    // The Ambient wake is a NAVIGATION, not an audio change. SleepStateChanged(false) is the only
    // thing Sleep.razor listens for to leave /sleep, so skipping it here would strand the kiosk on
    // a clock with the knobs already acting.
    var (service, allClients) = CreateService();
    service.SetSleepScreenVisible(true);

    await service.WakeAsync("encoder-turn");

    allClients.Verify(
      c => c.SendCoreAsync(
        "SleepStateChanged",
        It.Is<object?[]>(args => MatchesBool(args, false)),
        It.IsAny<CancellationToken>()),
      Times.Once);
    Assert.False(service.IsSleeping);
  }

  [Fact]
  public async Task WakeAsync_FromAmbient_DoesNotTouchAudio()
  {
    // Ambient's defining property is that audio never stopped. A wake from it must not "restore" a
    // mute state that was never saved.
    var audio = new Mock<IAudioManager>();
    var (service, _) = CreateService(audioManager: audio.Object);
    service.SetSleepScreenVisible(true);

    await service.WakeAsync("encoder-turn");

    audio.VerifySet(m => m.IsMuted = It.IsAny<bool>(), Times.Never);
  }

  [Fact]
  public async Task WakeAsync_WithNothingToWakeFrom_StillDoesNotRebroadcast()
  {
    // The shipped guard, restated against the new condition: awake plus no sleep screen is nothing
    // to wake from, and a broadcast there would navigate every other tab home for no reason.
    var (service, allClients) = CreateService();

    await service.WakeAsync("api");

    allClients.Verify(
      c => c.SendCoreAsync(
        It.IsAny<string>(),
        It.IsAny<object?[]>(),
        It.IsAny<CancellationToken>()),
      Times.Never);
  }

  // ─── /sleep stops attended playback (ADR-029 D7 §7.5, closing §14 Q8) ────

  private static EventPlaybackSnapshot SnapshotIn(EventPlaybackState state, string id = "evp-1") =>
    new(id, EventPlaybackKind.RemoteMedia, "Voicemail from Jane", state,
      TimeSpan.FromSeconds(29), TimeSpan.Zero, DateTimeOffset.UtcNow, null);

  [Fact]
  public async Task EnteringSleepStopsAPlayingAttendedPlayback()
  {
    // /sleep runs under EmptyLayout and offers no transport, so attended playback may not exist
    // there. ⚠ And this is a STOP, not a reliance on the mute below it: WakeAsync restores
    // _wasMutedBeforeSleep, so a muted-but-still-playing voicemail would become audible again
    // mid-word the instant somebody touched the panel in a dark room.
    var playback = new StoppableEventPlayback { Current = SnapshotIn(EventPlaybackState.Playing) };
    var (service, _) = CreateService(eventPlayback: playback);

    await service.EnterSleepAsync();

    Assert.Equal(new[] { "evp-1" }, playback.StopIds);
  }

  [Theory]
  [InlineData(EventPlaybackState.Completed)]
  [InlineData(EventPlaybackState.Stopped)]
  [InlineData(EventPlaybackState.Failed)]
  public async Task EnteringSleepDoesNotStopAPlaybackThatHasAlreadyEnded(EventPlaybackState state)
  {
    // ⚠ A non-null Current is NOT the same as audio in the room. IEventPlaybackService.Current
    // RETAINS the last snapshot after a playback ends — that surface is the only place an acquisition
    // failure can be read from — so the STATE decides, not the null check. A stop here would be a
    // pointless 404 against every sleep after any voicemail the console has ever played.
    var playback = new StoppableEventPlayback { Current = SnapshotIn(state) };
    var (service, _) = CreateService(eventPlayback: playback);

    await service.EnterSleepAsync();

    Assert.Empty(playback.StopIds);
  }

  [Fact]
  public async Task EnteringSleepStillSleepsWhenTheStopThrows()
  {
    // Sleep is not allowed to fail because a voicemail would not stop. The pause, the mute and the
    // SleepStateChanged broadcast all still have to happen.
    var audio = new Mock<IAudioManager>();
    audio.SetupGet(m => m.IsMuted).Returns(false);
    var playback = new StoppableEventPlayback
    {
      Current = SnapshotIn(EventPlaybackState.Playing),
      Throws = new InvalidOperationException("the seam is wedged")
    };

    var (service, allClients) = CreateService(
      audioManager: audio.Object, eventPlayback: playback);

    await service.EnterSleepAsync();

    Assert.True(service.IsSleeping);
    audio.VerifySet(m => m.IsMuted = true, Times.Once);
    allClients.Verify(
      c => c.SendCoreAsync(
        "SleepStateChanged",
        It.Is<object?[]>(args => MatchesBool(args, true)),
        It.IsAny<CancellationToken>()),
      Times.Once);
  }

  /// <summary>
  /// The attended-playback seam reduced to what <c>EnterSleepAsync</c> touches: a retained
  /// <see cref="Current"/> and a <see cref="StopAsync"/> that records, or throws.
  /// </summary>
  private sealed class StoppableEventPlayback : IEventPlaybackService
  {
    public EventPlaybackSnapshot? Current { get; set; }

    public Exception? Throws { get; set; }

    public List<string> StopIds { get; } = [];

    public event EventHandler<EventPlaybackSnapshot>? PlaybackChanged
    {
      add { }
      remove { }
    }

    public Task<EventPlaybackSnapshot> StartAsync(
      EventPlaybackRequest request, CancellationToken cancellationToken = default)
      => throw new NotSupportedException("Sleep never starts a playback.");

    public Task<bool> StopAsync(string playbackId, CancellationToken cancellationToken = default)
    {
      if (Throws is { } ex)
      {
        throw ex;
      }

      StopIds.Add(playbackId);
      return Task.FromResult(true);
    }

    public Task<bool> SeekAsync(
      string playbackId, TimeSpan position, CancellationToken cancellationToken = default)
      => Task.FromResult(false);

    public Task<bool> PauseAsync(string playbackId, CancellationToken cancellationToken = default)
      => Task.FromResult(false);

    public Task<bool> ResumeAsync(string playbackId, CancellationToken cancellationToken = default)
      => Task.FromResult(false);
  }
}
