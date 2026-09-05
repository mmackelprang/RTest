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
  public async Task WakeState_WithTheSleepScreenUpAndAudioPlaying_IsAmbient()
  {
    // The overnight state, and the one the machine actually reaches: the browser idled onto /sleep
    // and nothing paused audio.
    var (service, _) = CreateService();

    await service.SetSleepScreenVisibleAsync(true);

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
  public async Task TryClaimWake_GrantsExactlyOneClaim_AndTheStateReadsAwakeFromThatInstant()
  {
    // The latch, and the whole reason it exists: with a 10 ms poll, a dozen detents arrive before
    // the browser has left /sleep. Exactly one is spent waking; the rest must find an awake console
    // and act. A fast spin loses one detent, not twelve.
    var (service, _) = CreateService();
    await service.SetSleepScreenVisibleAsync(true);

    Assert.True(service.TryClaimWake());
    Assert.Equal(ConsoleWakeState.Awake, service.WakeState);
    Assert.False(service.TryClaimWake());
  }

  [Fact]
  public async Task SetSleepScreenVisible_False_ReleasesTheClaim()
  {
    // The claim is released by the browser confirming it left /sleep, not by WakeAsync finishing:
    // WakeAsync completes while the page is still up, and releasing there would drop the console
    // straight back into Ambient and start consuming inputs again.
    var (service, _) = CreateService();
    await service.SetSleepScreenVisibleAsync(true);
    Assert.True(service.TryClaimWake());

    await service.SetSleepScreenVisibleAsync(false);

    Assert.Equal(ConsoleWakeState.Awake, service.WakeState);
    await service.SetSleepScreenVisibleAsync(true);
    Assert.Equal(ConsoleWakeState.Ambient, service.WakeState);
  }

  [Fact]
  public async Task EnterSleepAsync_ReleasesAnOutstandingClaim()
  {
    // Otherwise a wake that was claimed and never confirmed would leave the console permanently
    // reading Awake, and the next Standby would not consume anything.
    var (service, _) = CreateService();
    await service.SetSleepScreenVisibleAsync(true);
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
    await service.SetSleepScreenVisibleAsync(true);

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
    await service.SetSleepScreenVisibleAsync(true);

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


  // --- the SECOND edge: the sleep SCREEN going up (ADR-029 sec 16.5) -------
  //
  // WARNING: this block is the whole of what ADR-029 Amendment 2 asked PHN-1e for. Sec 7.5 hung its
  // rule on EnterSleepAsync alone, and sec 16.4 measured that against the tree: the 30-minute idle
  // timer - the case sec 7.5's own motivating sentence names - reaches /sleep by
  // window.location.href and calls NOTHING server-side, so IsSleeping is false on it and the rule
  // never fired. Every test here drives the service WITHOUT EnterSleepAsync, which is what makes it
  // the idle path rather than a second spelling of the tests above.

  [Fact]
  public async Task TheSleepScreenGoingUpStopsAPlayingAttendedPlayback()
  {
    // The idle path, exactly: no EnterSleepAsync anywhere in this test.
    var playback = new StoppableEventPlayback { Current = SnapshotIn(EventPlaybackState.Playing) };
    var (service, _) = CreateService(eventPlayback: playback);

    await service.SetSleepScreenVisibleAsync(true);

    Assert.Equal(new[] { "evp-1" }, playback.StopIds);
  }

  [Fact]
  public async Task TheIdlePathStopsPlaybackWhileIsSleepingStaysFalse()
  {
    // The finding itself, pinned as an assertion rather than left in a comment. IsSleeping is the
    // obvious predicate and it is the WRONG one: on this path it never becomes true, which is
    // precisely why the EnterSleepAsync-only rule missed the idle timer for the whole of sec 7.5's
    // life. If someone "simplifies" the trigger back to an IsSleeping check, this reds.
    var playback = new StoppableEventPlayback { Current = SnapshotIn(EventPlaybackState.Playing) };
    var (service, _) = CreateService(eventPlayback: playback);

    await service.SetSleepScreenVisibleAsync(true);

    Assert.False(service.IsSleeping);
    Assert.Equal(ConsoleWakeState.Ambient, service.WakeState);
    Assert.Equal(new[] { "evp-1" }, playback.StopIds);
  }

  [Theory]
  [InlineData(EventPlaybackState.Preparing)]
  [InlineData(EventPlaybackState.Waiting)]
  [InlineData(EventPlaybackState.Playing)]
  [InlineData(EventPlaybackState.Paused)]
  public async Task TheSleepScreenGoingUpStopsEveryLiveState(EventPlaybackState state)
  {
    // Preparing is in the list deliberately: a fetch or a synthesis still in flight would otherwise
    // start audio moments after the panel went dark.
    //
    // Waiting (PHN-1f, owner decision D28) for the same reason with a longer fuse and a certainty in
    // place of a maybe: a queued playback is HOLDING acquired audio and will start it the instant the
    // blocking source ends, which can be up to GvMedia:MaxQueuedWaitSeconds after the screen goes
    // dark. ⚠ This list is HAND-WRITTEN, so TheSleepRuleCoversEveryNonTerminalState below is what
    // keeps it honest for the member after this one.
    var playback = new StoppableEventPlayback { Current = SnapshotIn(state) };
    var (service, _) = CreateService(eventPlayback: playback);

    await service.SetSleepScreenVisibleAsync(true);

    Assert.Equal(new[] { "evp-1" }, playback.StopIds);
  }

  [Fact]
  public async Task EnteringSleepStopsAWaitingPlayback()
  {
    // ⛔ C-56, on the EnterSleepAsync edge. A waiting playback is LIVE: it has already acquired its
    // audio and is parked only until the blocking source leaves the ducking set. /sleep runs under
    // EmptyLayout and renders no transport at all, so letting one survive means audio starting up to
    // GvMedia:MaxQueuedWaitSeconds later on a dark panel with no stop control anywhere on screen.
    //
    // MUTATION (§2.1): remove EventPlaybackState.Waiting from SleepService's allow-list.
    var playback = new StoppableEventPlayback { Current = SnapshotIn(EventPlaybackState.Waiting) };
    var (service, _) = CreateService(eventPlayback: playback);

    await service.EnterSleepAsync();

    Assert.Equal(new[] { "evp-1" }, playback.StopIds);
  }

  [Fact]
  public async Task TheSleepScreenReportStopsAWaitingPlayback()
  {
    // The same rule on the OTHER edge — the idle path, which reaches /sleep by
    // window.location.href and calls nothing else server-side. No EnterSleepAsync anywhere here, and
    // IsSleeping stays false throughout, which is the whole reason this edge exists.
    //
    // MUTATION (§2.1): remove EventPlaybackState.Waiting from SleepService's allow-list.
    var playback = new StoppableEventPlayback { Current = SnapshotIn(EventPlaybackState.Waiting) };
    var (service, _) = CreateService(eventPlayback: playback);

    await service.SetSleepScreenVisibleAsync(true);

    Assert.False(service.IsSleeping);
    Assert.Equal(new[] { "evp-1" }, playback.StopIds);
  }

  [Fact]
  public async Task TheSleepRuleCoversEveryNonTerminalState()
  {
    // ⚠ Written against the ENUM rather than a hand-listed set, because the failure C-56 describes is
    // SILENT: SleepService's rule is an allow-list, so a new non-terminal member is excluded by
    // default and nothing else in the suite would notice. This reds when someone adds a member and
    // does not list it there.
    //
    // The terminal three are the deny-list Radio.Web's EventPlaybackSnapshotDto.IsLive uses, so the
    // two rules with opposite polarity end up asserted against ONE definition rather than two.
    //
    // ⚠ It drives the REAL SleepService rather than restating its predicate. A test that re-listed
    // the allow-list would be a copy of the bug: it would agree with the code by construction and
    // could never disagree with it.
    var terminal = new[]
    {
      EventPlaybackState.Completed, EventPlaybackState.Stopped, EventPlaybackState.Failed
    };

    var live = Enum.GetValues<EventPlaybackState>().Except(terminal).ToArray();

    // A guard on the guard: if someone deletes members instead of adding them, an empty loop would
    // pass silently.
    Assert.NotEmpty(live);

    foreach (var state in live)
    {
      // A fresh service per state: the screen report is an EDGE, so a second true on the same
      // instance is a no-op and every iteration after the first would assert nothing.
      var playback = new StoppableEventPlayback { Current = SnapshotIn(state) };
      var (service, _) = CreateService(eventPlayback: playback);

      await service.SetSleepScreenVisibleAsync(true);

      // Asserted with a MESSAGE rather than as a bare collection compare, because the whole value of
      // this test is naming the member that was forgotten.
      Assert.True(
        playback.StopIds.SequenceEqual(new[] { "evp-1" }),
        $"EventPlaybackState.{state} is not terminal, so entering /sleep must stop it — add it to "
        + "SleepService.StopAttendedPlaybackAsync's allow-list (plan PHN-1f C-56).");
    }
  }

  [Theory]
  [InlineData(EventPlaybackState.Completed)]
  [InlineData(EventPlaybackState.Stopped)]
  [InlineData(EventPlaybackState.Failed)]
  public async Task TheSleepScreenGoingUpDoesNotStopAPlaybackThatHasAlreadyEnded(
    EventPlaybackState state)
  {
    // Current is RETAINED after a playback ends, so the state decides and not the null check -
    // otherwise every idle timeout after any voicemail the console has ever played fires a 404.
    var playback = new StoppableEventPlayback { Current = SnapshotIn(state) };
    var (service, _) = CreateService(eventPlayback: playback);

    await service.SetSleepScreenVisibleAsync(true);

    Assert.Empty(playback.StopIds);
  }

  [Fact]
  public async Task TheSleepScreenGoingDownStopsNothing()
  {
    // MainLayout reports false on its own first render, on every navigation home. A rule that fired
    // on any report at all would kill a voicemail every time the console came back from /sleep -
    // the exact inversion of what sec 7.5 asks for.
    //
    // ⚠ The transition is DRIVEN here, true -> false. An earlier version called
    // SetSleepScreenVisibleAsync(false) from the initial state, where the flag is already false, so
    // the going-down case it names was never actually exercised.
    var playback = new StoppableEventPlayback { Current = SnapshotIn(EventPlaybackState.Playing) };
    var (service, _) = CreateService(eventPlayback: playback);

    await service.SetSleepScreenVisibleAsync(true);
    Assert.Equal(new[] { "evp-1" }, playback.StopIds);

    // A second playback starts while the console is on /sleep, and the user then leaves.
    playback.Current = SnapshotIn(EventPlaybackState.Playing, "evp-2");
    playback.StopIds.Clear();

    await service.SetSleepScreenVisibleAsync(false);

    Assert.Empty(playback.StopIds);
    Assert.Equal(ConsoleWakeState.Awake, service.WakeState);
  }

  [Fact]
  public async Task AGoingDownReportWithAnOutstandingWakeClaimStillStopsNothing()
  {
    // ⚠ The specific shape a WakeState-transition rule got wrong. With audio parked and a wake
    // claimed by a knob, WakeState reads Awake; a report that the screen went AWAY then reads as a
    // transition Awake -> Standby and a transition-based rule would stop attended playback on a
    // report meaning the opposite. Deciding from the report's own argument cannot express that bug.
    var playback = new StoppableEventPlayback { Current = SnapshotIn(EventPlaybackState.Playing) };
    var (service, _) = CreateService(eventPlayback: playback);

    await service.SetSleepScreenVisibleAsync(true);
    await service.EnterSleepAsync();
    Assert.True(service.TryClaimWake());
    Assert.Equal(ConsoleWakeState.Awake, service.WakeState);

    playback.Current = SnapshotIn(EventPlaybackState.Playing, "evp-2");
    playback.StopIds.Clear();

    await service.SetSleepScreenVisibleAsync(false);

    Assert.Empty(playback.StopIds);
  }

  [Fact]
  public async Task ARepeatedSleepScreenReportStopsOnlyOnce()
  {
    // ⚠ IDEMPOTENCE COMES FROM THE PLAYBACK'S STATE, NOT FROM AN EDGE, and the distinction is the
    // point of this test. StopAttendedPlaybackAsync decides on the snapshot: the second report finds
    // a Stopped snapshot and returns without an HTTP call. That is a stronger guarantee than an edge
    // gave, because it also holds when the two reports come from DIFFERENT clients - which an
    // edge on a single global flag cannot see.
    var playback = new StoppableEventPlayback { Current = SnapshotIn(EventPlaybackState.Playing) };
    var (service, _) = CreateService(eventPlayback: playback);

    await service.SetSleepScreenVisibleAsync(true);
    await service.SetSleepScreenVisibleAsync(true);

    Assert.Equal(new[] { "evp-1" }, playback.StopIds);
  }

  [Fact]
  public async Task AStaleVisibleFlagDoesNotSUPPRESSTheStop()
  {
    // ⭐ THE FINDING THAT REWROTE THE PREDICATE, pinned. Found by the comment-accuracy reviewer
    // against a rule written as "stop when WakeState LEAVES Awake".
    //
    // _isSleepScreenVisible can be left STALE-TRUE while the console is genuinely awake on Home, and
    // the tree documents both halves of how: Sleep.razor's dispose report is best-effort behind a
    // 2 s CTS ("a hard browser navigation can tear the circuit down before this lands"), and
    // MainLayout's corrective false on first render is fire-and-forget with the failure swallowed.
    // Lose both - one API blip on a WiFi-only box - and the flag sits true.
    //
    // A voicemail then starts. Thirty minutes later the idle timer navigates to /sleep and the page
    // reports visible=true. Nothing CHANGED and the state was already Ambient, so a
    // transition-based rule does not fire, and attended audio plays on a surface with no transport -
    // exactly the failure sec 7.5 exists to prevent, on exactly the path sec 16.4 rewrote the rule
    // for. The old reasoning ("a before-state that is already Ambient is one where the earlier edge
    // already ran") fails because the earlier edge ran against a DIFFERENT playback, hours earlier.
    var playback = new StoppableEventPlayback { Current = SnapshotIn(EventPlaybackState.Playing) };
    var (service, _) = CreateService(eventPlayback: playback);

    // The stale flag, reached the way the box reaches it: a report that was never corrected.
    await service.SetSleepScreenVisibleAsync(true);
    Assert.Equal(new[] { "evp-1" }, playback.StopIds);

    // Console is really awake on Home now, but nothing told the server. A new voicemail starts.
    playback.Current = SnapshotIn(EventPlaybackState.Playing, "evp-2");
    playback.StopIds.Clear();

    // The idle timer lands on /sleep and the page reports itself. This is NOT a change.
    await service.SetSleepScreenVisibleAsync(true);

    Assert.Equal(new[] { "evp-2" }, playback.StopIds);
  }

  [Fact]
  public async Task AScreenReportArrivingAfterStandbyDoesNotStopASecondTime()
  {
    // The pill and the server push both park the room FIRST and navigate after, so this ordering is
    // the normal one for four of sec 16.4's five entry points. EnterSleepAsync already stopped; the
    // report that follows finds the console in Standby and is not an edge out of Awake.
    var playback = new StoppableEventPlayback { Current = SnapshotIn(EventPlaybackState.Playing) };
    var (service, _) = CreateService(eventPlayback: playback);

    await service.EnterSleepAsync();
    await service.SetSleepScreenVisibleAsync(true);

    Assert.Equal(new[] { "evp-1" }, playback.StopIds);
  }

  [Fact]
  public async Task AReportDoesNotDisturbAnOutstandingWakeClaim()
  {
    // ENC-6's latch is not this rule's business and must survive it. A report that is not a change
    // leaves the claim alone - which is what stops a future re-report heartbeat from wiping a claim
    // mid-wake and dropping the console back into Ambient, consuming a second input for one wake.
    var playback = new StoppableEventPlayback { Current = SnapshotIn(EventPlaybackState.Playing) };
    var (service, _) = CreateService(eventPlayback: playback);

    await service.SetSleepScreenVisibleAsync(true);
    Assert.True(service.TryClaimWake());
    Assert.Equal(ConsoleWakeState.Awake, service.WakeState);

    await service.SetSleepScreenVisibleAsync(true);

    Assert.Equal(ConsoleWakeState.Awake, service.WakeState);
  }

  [Fact]
  public async Task AWedgedStopDoesNotLeaveTheConsoleUnwakeable()
  {
    // ⚠ A HANG FIX, pinned. EnterSleepAsync calls the stop while holding _lock, and WakeAsync takes
    // that same _lock - so an unbounded wait on a wedged IEventPlaybackService.StopAsync makes the
    // console unwakeable by EVERY route at once: the encoder, the screen tap and the REST call all
    // queue behind it. This repo has a documented class of hang in exactly that layer.
    //
    // The fake parks inside StopAsync until released and honours the token, so the only thing that
    // can free this test is SleepService's own timeout. Delete the CTS and this hangs forever
    // instead of passing.
    //
    // ⚠ Bounded-negative-safe by construction: the assertions are that sleep COMPLETED and the wake
    // COMPLETED, so machine slowness can only make this test slower, never green-when-it-should-red.
    var playback = new StoppableEventPlayback
    {
      Current = SnapshotIn(EventPlaybackState.Playing),
      BlockUntilReleased = true
    };
    var (service, _) = CreateService(eventPlayback: playback);

    await service.EnterSleepAsync();

    Assert.True(service.IsSleeping);
    Assert.Empty(playback.StopIds);

    await service.WakeAsync("encoder-turn");

    Assert.False(service.IsSleeping);
    playback.ReleaseStop();
  }

  [Fact]
  public async Task TheReportDoesNotAnswerUntilTheStopHasCompleted()
  {
    // C-49, the constraint this arc paid for once already: a stop that nothing observes is a stop
    // that can silently not happen. ADR-029 sec 16.5 left awaiting-vs-dispatching open and argued
    // for awaiting; this is the test that keeps that choice honest, and it reds the moment somebody
    // writes `_ = StopAttendedPlaybackAsync()`.
    //
    // DETERMINISTIC, not patient. The rendezvous is the fake reporting that StopAsync was ENTERED -
    // CLAUDE.md Test Timing's rule, count the observation rather than time it. There is no
    // Task.Delay here and no wall clock on either side.
    var playback = new StoppableEventPlayback
    {
      Current = SnapshotIn(EventPlaybackState.Playing),
      BlockUntilReleased = true
    };
    var (service, _) = CreateService(eventPlayback: playback);

    var reporting = service.SetSleepScreenVisibleAsync(true);
    await playback.StopEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

    Assert.False(reporting.IsCompleted);

    playback.ReleaseStop();
    await reporting.WaitAsync(TimeSpan.FromSeconds(5));

    Assert.Equal(new[] { "evp-1" }, playback.StopIds);
  }

  [Fact]
  public async Task TheScreenReportStillSettlesWhenTheStopThrows()
  {
    // Reporting the sleep screen is not allowed to fail because a voicemail would not stop: the
    // flag has to be recorded either way, or the encoder router gates on a state the box is not in
    // and the page never gets its response.
    var playback = new StoppableEventPlayback
    {
      Current = SnapshotIn(EventPlaybackState.Playing),
      Throws = new InvalidOperationException("the seam is wedged")
    };
    var (service, _) = CreateService(eventPlayback: playback);

    await service.SetSleepScreenVisibleAsync(true);

    Assert.True(service.IsSleepScreenVisible);
    Assert.Equal(ConsoleWakeState.Ambient, service.WakeState);
  }

  [Fact]
  public async Task TheScreenReportSurvivesAnAbsentPlaybackSeam()
  {
    // IEventPlaybackService is optional in the container, and ENC-6's encoder gating predates it.
    var (service, _) = CreateService();

    await service.SetSleepScreenVisibleAsync(true);

    Assert.Equal(ConsoleWakeState.Ambient, service.WakeState);
  }

  /// <summary>
  /// The attended-playback seam reduced to what the two sleep edges touch: a retained
  /// <see cref="Current"/> and a <see cref="StopAsync"/> that records, throws, or parks until the
  /// test releases it.
  /// </summary>
  private sealed class StoppableEventPlayback : IEventPlaybackService
  {
    public EventPlaybackSnapshot? Current { get; set; }

    public Exception? Throws { get; set; }

    public List<string> StopIds { get; } = [];

    /// <summary>
    /// When set, <see cref="StopAsync"/> signals <see cref="StopEntered"/> and then parks until
    /// <see cref="ReleaseStop"/>. That is the seam a caller who merely DISPATCHED the stop would
    /// race past, which is what makes the await observable.
    /// </summary>
    public bool BlockUntilReleased { get; set; }

    public TaskCompletionSource StopEntered { get; } =
      new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly TaskCompletionSource _release =
      new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void ReleaseStop() => _release.TrySetResult();

    public event EventHandler<EventPlaybackSnapshot>? PlaybackChanged
    {
      add { }
      remove { }
    }

    public Task<EventPlaybackSnapshot> StartAsync(
      EventPlaybackRequest request, CancellationToken cancellationToken = default)
      => throw new NotSupportedException("Sleep never starts a playback.");

    public async Task<bool> StopAsync(
      string playbackId, CancellationToken cancellationToken = default)
    {
      if (Throws is { } ex)
      {
        throw ex;
      }

      if (BlockUntilReleased)
      {
        StopEntered.TrySetResult();
        // Honour the caller's token. SleepService time-boxes this call precisely so a wedged seam
        // cannot make the console unwakeable, and a fake that ignored the token could not show it.
        await _release.Task.WaitAsync(cancellationToken);
      }

      StopIds.Add(playbackId);

      // ⚠ THE SNAPSHOT TRANSITIONS, as the real service's does. An earlier version of this fake left
      // Current on Playing forever, which quietly made two tests pass for the wrong reason: they
      // read as "the rule is edge-triggered" when what actually prevents a second stop in the real
      // system is that the second call finds a terminal snapshot. A fake that cannot reach the
      // terminal state cannot tell those apart.
      if (Current is { } c && c.Id == playbackId)
      {
        Current = c with { State = EventPlaybackState.Stopped };
      }

      return true;
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
