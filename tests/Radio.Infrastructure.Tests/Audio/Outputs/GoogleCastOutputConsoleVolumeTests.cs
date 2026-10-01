using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Radio.Core.Interfaces.Audio;
using Radio.Infrastructure.Audio.Outputs;
using Xunit;

namespace Radio.Infrastructure.Tests.Audio.Outputs;

/// <summary>
/// AUD-81: the console-driven SET_VOLUME / SET_MUTE paths of <see cref="GoogleCastOutput"/> —
/// coalescing, the recent-push echo memory, per-device remembering, and the mute lifecycle.
/// </summary>
/// <remarks>
/// No assertion races a wall clock. In-flight pushes are held by a TaskCompletionSource gate on
/// the SET_VOLUME seam, every burst is awaited through the drain task before asserting, and the
/// echo window is driven with <c>FakeTimeProvider</c>.
/// </remarks>
public class GoogleCastOutputConsoleVolumeTests
{
  /// <summary>
  /// Whether the harness models the measured device (a level change unmutes a muted speaker, the
  /// reply status raised inside the send — <see cref="CastConsoleTestHarness.LevelCommandUnmutes"/>).
  /// Off here; <see cref="GoogleCastOutputConsoleVolumeTests_OnTheDeviceModel"/> runs every test with it on.
  /// </summary>
  protected virtual bool OnTheDeviceModel => false;

  private CastConsoleTestHarness NewHarness() => new(OnTheDeviceModel);

  [Fact]
  public async Task ABurstOfConsoleChanges_IsCoalesced_LatestWins_AndRememberedOnce()
  {
    await using var h = NewHarness();
    await h.ConnectAsync(reportedLevel: 0.40f);
    var target = h.Target();
    h.ClearCommands();
    var rememberedBefore = h.Store.Remembered.Count;

    h.VolumeGate = CastConsoleTestHarness.NewTcs();
    var burst = h.Output.SetDeviceVolumeFromConsoleAsync(0.30f, target.Generation, 0.5f);
    await h.VolumeSendEntered.Task; // 0.30 is now in flight

    // While it is in flight, three more ticks: each replaces the single pending target.
    Assert.Same(burst, h.Output.SetDeviceVolumeFromConsoleAsync(0.32f, target.Generation, 0.52f));
    Assert.Same(burst, h.Output.SetDeviceVolumeFromConsoleAsync(0.34f, target.Generation, 0.54f));
    Assert.Same(burst, h.Output.SetDeviceVolumeFromConsoleAsync(0.36f, target.Generation, 0.56f));
    Assert.Equal(0.36f, h.Output.KnownSpeakerLevel, 3); // the latest target, not yet applied

    h.VolumeGate.SetResult();
    var result = await burst;

    Assert.Equal(new[] { 0.30f, 0.36f }, h.VolumeSends());
    Assert.Equal(2, result.Sends);
    Assert.Equal(0.36f, result.AppliedLevel!.Value, 3);
    Assert.Equal(0.56f, result.AppliedConsoleLevel, 3);
    Assert.False(result.Failed);
    Assert.Equal(0.36f, h.Output.KnownSpeakerLevel, 3);

    // Remembered once, with the final level — not once per tick.
    var remembered = h.Store.Remembered.Skip(rememberedBefore).ToList();
    Assert.Equal(new[] { ("cast-a", 0.36f) }, remembered);
  }

  [Fact]
  public async Task LateAndOutOfOrderConfirmations_OfConsolePushes_AreEchoes_NotExternalChanges()
  {
    await using var h = NewHarness();
    await h.ConnectAsync(reportedLevel: 0.40f);
    var target = h.Target();

    foreach (var level in new[] { 0.30f, 0.32f, 0.34f })
    {
      await h.Output.SetDeviceVolumeFromConsoleAsync(level, target.Generation);
    }

    // The device's confirmations arrive late and out of order, within the echo window.
    h.Time.Advance(TimeSpan.FromSeconds(1));
    h.RaiseStatus(0.32);
    h.RaiseStatus(0.30);
    h.RaiseStatus(0.34);
    h.RaiseStatus(0.30);

    Assert.Empty(h.External);
    Assert.Equal(0.34f, h.Output.KnownSpeakerLevel, 3);

    // The window is what recognised them: once it has passed, the same intermediate level is
    // a real change made on the speaker.
    h.Time.Advance(TimeSpan.FromSeconds(4));
    h.RaiseStatus(0.32);
    var external = Assert.Single(h.External);
    Assert.Equal(0.32f, external.Volume, 3);
  }

  // Hostile review F3, corrected by re-review M2. A speaker that quantises to 1/15 (≈ 0.0667)
  // reports our 0.437 as 0.4667 and our 0.50 as 0.5333 — and says so: StepInterval 1/15 in every
  // status. 0.4667 lies between our pushes; 0.5333, the final level of the move, is an extreme of
  // the range and needs the step-derived tolerance (an earlier revision raised a bare 0.50 here,
  // which a 1/15 speaker never reports).
  private const double FifteenStep = 1.0 / 15.0;

  [Fact]
  public async Task ACoarselyQuantisedEcho_BetweenRecentPushes_IsAbsorbed_NotWrittenBack()
  {
    await using var h = NewHarness();
    await h.ConnectAsync(reportedLevel: 0.40f);
    h.RaiseStatus(6 * FifteenStep, stepInterval: FifteenStep); // 0.40: no change; the step is known
    var target = h.Target();
    var rememberedBefore = h.Store.Remembered.Count;

    await h.Output.SetDeviceVolumeFromConsoleAsync(0.437f, target.Generation);
    await h.Output.SetDeviceVolumeFromConsoleAsync(0.50f, target.Generation);

    h.Time.Advance(TimeSpan.FromSeconds(1));
    h.RaiseStatus(7 * FifteenStep, stepInterval: FifteenStep); // 0.4667: the device's rounding of 0.437
    h.RaiseStatus(8 * FifteenStep, stepInterval: FifteenStep); // 0.5333: its rounding of 0.50

    Assert.Empty(h.External);
    Assert.DoesNotContain(
      h.Store.Remembered.Skip(rememberedBefore),
      r => Math.Abs(r.Volume - 7f / 15f) < 0.001f || Math.Abs(r.Volume - 8f / 15f) < 0.001f);
  }

  // Round-3 review MEDIUM-1: a level that only the RANGE rule can recognise. With no step known
  // the tolerance is 0.01, so 0.45 is 0.05 from both pushes and no point match can absorb it;
  // it lies between them, inside the echo window, so it is an echo (an intermediate level the
  // speaker reported while ramping). Replacing the range check with `false` fails this test.
  [Fact]
  public async Task ALevelBetweenTwoRecentPushes_FarFromBoth_IsAbsorbedByTheRangeRuleAlone()
  {
    await using var h = NewHarness();
    await h.ConnectAsync(reportedLevel: 0.40f);
    var target = h.Target();

    await h.Output.SetDeviceVolumeFromConsoleAsync(0.40f, target.Generation);
    await h.Output.SetDeviceVolumeFromConsoleAsync(0.50f, target.Generation);
    h.Time.Advance(TimeSpan.FromSeconds(1)); // inside the 3 s echo window

    h.RaiseStatus(0.45);
    Assert.Empty(h.External);

    // A level in the same range once the window has passed is a change made on the speaker
    // (0.47: at least 0.02 from 0.45 and from 0.50, so not a "no change" against either baseline).
    h.Time.Advance(TimeSpan.FromSeconds(4));
    h.RaiseStatus(0.47);
    Assert.Equal(0.47f, Assert.Single(h.External).Volume, 3);
  }

  // Re-review M2 (a): one detent — a single push, and the only echo the speaker sends is its
  // quantised version of it, 0.033 away. Without the step-derived tolerance this was an EXTERNAL
  // change on every single-detent move: master rewritten, AUD-80 remembering 0.5333.
  [Fact]
  public async Task ASingleDetentPush_OnAOneFifteenthStepSpeaker_HasItsQuantisedEchoAbsorbed()
  {
    await using var h = NewHarness();
    await h.ConnectAsync(reportedLevel: 0.40f);
    h.RaiseStatus(6 * FifteenStep, stepInterval: FifteenStep);
    var target = h.Target();
    var rememberedBefore = h.Store.Remembered.Count;

    await h.Output.SetDeviceVolumeFromConsoleAsync(0.50f, target.Generation);
    h.RaiseStatus(8 * FifteenStep, stepInterval: FifteenStep);

    Assert.Empty(h.External);
    Assert.Equal(0.50f, h.Output.KnownSpeakerLevel, 3);
    Assert.DoesNotContain(
      h.Store.Remembered.Skip(rememberedBefore),
      r => Math.Abs(r.Volume - 8f / 15f) < 0.001f);
  }

  // Re-review M2 (b): the wider tolerance is half a step, not a licence. A change made on the
  // speaker a step or more beyond our pushes' echoes is still reported (AUD-5).
  [Fact]
  public async Task OnAOneFifteenthStepSpeaker_AChangeMoreThanAStepOutsideTheRange_IsStillReported()
  {
    await using var h = NewHarness();
    await h.ConnectAsync(reportedLevel: 0.40f);
    h.RaiseStatus(6 * FifteenStep, stepInterval: FifteenStep);
    var target = h.Target();

    await h.Output.SetDeviceVolumeFromConsoleAsync(0.437f, target.Generation);
    await h.Output.SetDeviceVolumeFromConsoleAsync(0.50f, target.Generation);
    h.Time.Advance(TimeSpan.FromSeconds(1)); // still inside the echo window

    h.RaiseStatus(9 * FifteenStep, stepInterval: FifteenStep); // 0.60: one step past the 0.5333 echo
    Assert.Equal(9f / 15f, Assert.Single(h.External).Volume, 3);

    h.RaiseStatus(5 * FifteenStep, stepInterval: FifteenStep); // 0.3333: below the range
    Assert.Equal(2, h.External.Count);
    Assert.Equal(5f / 15f, h.External[^1].Volume, 3);
  }

  // Re-review M2 (c): a speaker that reports no step (or a nonsense one) keeps the 0.01 tolerance.
  // Its coarse echo of a single push is therefore still read as external — the known limit.
  [Theory]
  [InlineData(null)]
  [InlineData(0.5)] // above MaxSpeakerStepInterval: ignored
  public async Task WithNoUsableStepReported_TheEchoToleranceFallsBackTo001(double? stepInterval)
  {
    await using var h = NewHarness();
    await h.ConnectAsync(reportedLevel: 0.40f);
    h.RaiseStatus(0.40, stepInterval: stepInterval);
    var target = h.Target();

    await h.Output.SetDeviceVolumeFromConsoleAsync(0.50f, target.Generation);
    h.RaiseStatus(0.505, stepInterval: stepInterval); // within 0.01: an echo
    Assert.Empty(h.External);

    h.RaiseStatus(8 * FifteenStep, stepInterval: stepInterval); // 0.033 away: external
    Assert.Equal(8f / 15f, Assert.Single(h.External).Volume, 3);
  }

  // Re-review M2: the step belongs to the speaker, so a new connection forgets it.
  [Fact]
  public async Task TheSpeakerStep_IsForgotten_OnANewConnection()
  {
    await using var h = NewHarness();
    await h.ConnectAsync("cast-a", reportedLevel: 0.40f);
    h.RaiseStatus(6 * FifteenStep, stepInterval: FifteenStep);

    await h.Output.DisconnectAsync();
    await h.ConnectAsync("cast-b", reportedLevel: 0.40f); // reports no step
    var target = h.Target();

    await h.Output.SetDeviceVolumeFromConsoleAsync(0.50f, target.Generation);
    h.RaiseStatus(8 * FifteenStep);
    Assert.Equal(8f / 15f, Assert.Single(h.External).Volume, 3);
  }

  // Re-review L1. A console push that timed out stays in flight (and in the echo memory) until
  // SharpCaster gives up, up to 35 s. It used to stretch the echo RANGE for all that time: with a
  // later push at 0.70, a genuine speaker change to 0.50 lay "between our pushes" and was absorbed.
  // A push in flight longer than EchoWindow now matches only its own echo, by point tolerance.
  [Fact]
  public async Task AStaleInFlightPush_DoesNotWidenTheEchoRange_ButStillMatchesItsOwnEcho()
  {
    await using var h = NewHarness();
    await h.ConnectAsync(reportedLevel: 0.40f);
    var target = h.Target();

    var timeoutArmed = h.Time.WatchForTimer(TimeSpan.FromSeconds(5)); // ConsoleCommandTimeout
    var held = CastConsoleTestHarness.NewTcs();
    h.VolumeGate = held;
    var burst = h.Output.SetDeviceVolumeFromConsoleAsync(0.30f, target.Generation);
    await h.VolumeSendEntered.Task;
    await timeoutArmed.WaitAsync(TimeSpan.FromSeconds(30)); // safety net only; never the gate
    h.Time.Advance(TimeSpan.FromSeconds(6)); // the push times out; its send stays in flight
    Assert.True((await burst).Failed);

    h.VolumeGate = null;
    await h.Output.SetDeviceVolumeFromConsoleAsync(0.70f, target.Generation);

    h.RaiseStatus(0.50); // changed on the speaker: between the stale 0.30 and the fresh 0.70
    Assert.Equal(0.50f, Assert.Single(h.External).Volume, 3);

    h.RaiseStatus(0.30); // the stale push's own late confirmation: still an echo
    h.RaiseStatus(0.70); // the fresh push's confirmation
    Assert.Single(h.External);

    held.SetResult();
  }

  [Fact]
  public async Task AFineQuantisedEcho_OfASinglePush_IsAbsorbed()
  {
    // Guard, not a mutation target: within 0.01 of the one push, so recognised by both the old
    // single-push rule and the range rule.
    await using var h = NewHarness();
    await h.ConnectAsync(reportedLevel: 0.40f);
    var target = h.Target();

    await h.Output.SetDeviceVolumeFromConsoleAsync(0.437f, target.Generation);
    h.RaiseStatus(0.44);
    h.RaiseStatus(0.43);

    Assert.Empty(h.External);
  }

  [Fact]
  public async Task AGenuineExternalChange_OutsideTheRangeOfRecentPushes_IsStillReported_WithinTheWindow()
  {
    await using var h = NewHarness();
    await h.ConnectAsync(reportedLevel: 0.40f);
    var target = h.Target();

    await h.Output.SetDeviceVolumeFromConsoleAsync(0.437f, target.Generation);
    await h.Output.SetDeviceVolumeFromConsoleAsync(0.50f, target.Generation);
    h.Time.Advance(TimeSpan.FromSeconds(1)); // still inside the 3 s echo window

    h.RaiseStatus(0.70); // above the range: changed on the speaker
    Assert.Equal(0.70f, Assert.Single(h.External).Volume, 3);
    Assert.Equal(0.70f, h.Store.Volumes["cast-a"], 3);

    h.RaiseStatus(0.30); // below the range
    Assert.Equal(2, h.External.Count);
    Assert.Equal(0.30f, h.External[^1].Volume, 3);
  }

  // AUD-81 connect race, measured on the box: SharpCaster raises the GET_STATUS response as a
  // ReceiverStatusChanged event on its receive thread, and it beat the continuation that primes
  // the echo baseline — so the handler compared against the -1f sentinel, fired an EXTERNAL
  // change, and AudioStateUpdateService unmuted a muted console. Here the status is raised from
  // inside the status-read seam, i.e. strictly before the read returns: the losing order, every
  // time, with no clock involved.
  [Fact]
  public async Task AStatusRaisedDuringTheInitialRead_IsPartOfTheInitialSync_AndNeverUnmutesTheConsole()
  {
    await using var h = NewHarness();

    // Mirrors AudioStateUpdateService.OnCastVolumeChanged: an initial sync is ignored; any other
    // event's mute state is written to the console.
    var consoleMuted = true;
    var initialSyncs = new List<CastVolumeChangedEventArgs>();
    h.Output.CastVolumeChanged += (_, e) =>
    {
      if (e.IsInitialSync)
      {
        initialSyncs.Add(e);
      }
      else
      {
        consoleMuted = e.IsMuted;
      }
    };

    await h.ConnectAsync(statusRead: () =>
    {
      h.RaiseStatus(0.30, muted: false);
      return Task.FromResult<(float, bool)?>((0.30f, false));
    });

    Assert.Empty(h.External);
    Assert.True(consoleMuted);
    Assert.Equal(0.30f, Assert.Single(initialSyncs).Volume, 3);

    // The same status again after the sync is still no change: the baseline holds it.
    h.RaiseStatus(0.30, muted: false);
    Assert.Empty(h.External);

    // Anti-vacuity: once the sync is over, a real change on the speaker is still reported.
    h.RaiseStatus(0.55, muted: false);
    Assert.Equal(0.55f, Assert.Single(h.External).Volume, 3);
  }

  // Hostile review F1. A live-discovered device makes ConnectAsync REUSE the output's client, and
  // that client's ReceiverChannel still carries OnReceiverStatusChanged from the previous
  // connection. A status arriving while the next connect is on the network used to be compared
  // with the previous speaker's baseline (here: muted at 0.40) and reported as an EXTERNAL change
  // — unmuting the muted console and remembering 0.75. The status is raised through the reused
  // client's own event, from inside the transport connect: strictly before the publish, every time.
  [Fact]
  public async Task AStatusRaisedDuringAReusedClientsTransportConnect_IsPartOfTheInitialSync_NeverExternal()
  {
    await using var h = NewHarness();
    h.RegisterLiveReceiver("cast-a");
    h.RegisterLiveReceiver("cast-b");

    // Mirrors AudioStateUpdateService.OnCastVolumeChanged, as above.
    var consoleMuted = true;
    h.Output.CastVolumeChanged += (_, e) =>
    {
      if (!e.IsInitialSync)
      {
        consoleMuted = e.IsMuted;
      }
    };

    await h.ConnectAsync("cast-a", reportedLevel: 0.40f, reportedMuted: true, streaming: false);
    var client = h.CurrentClient();
    Assert.NotNull(client);

    var delivered = new List<bool>();
    h.DuringTransportConnect = () =>
    {
      delivered.Add(CastConsoleTestHarness.RaiseStatusThroughClient(client!, 0.75, muted: false));
      return Task.CompletedTask;
    };
    await h.ConnectAsync("cast-b", reportedLevel: 0.75f, reportedMuted: false, streaming: false);
    h.DuringTransportConnect = null;

    // The premise: the client was reused, and the status really reached the output's handler.
    Assert.Same(client, h.CurrentClient());
    Assert.Equal(new[] { true }, delivered);

    Assert.Empty(h.External);
    Assert.True(consoleMuted);
    Assert.DoesNotContain(h.Store.Remembered, r => r.DeviceId == "cast-a" && Math.Abs(r.Volume - 0.75f) < 0.001f);

    // Anti-vacuity: once the connect is over, a real change on the speaker is still reported —
    // through the same client, so the handler is attached exactly once (one event, not two).
    Assert.True(CastConsoleTestHarness.RaiseStatusThroughClient(client!, 0.55, muted: false));
    Assert.Equal(0.55f, Assert.Single(h.External).Volume, 3);
  }

  [Fact]
  public async Task AStatusRaisedDuringAFailedInitialRead_IsAbsorbed_NotReportedAsExternal()
  {
    await using var h = NewHarness();

    // The read itself yields nothing (no volume in the response), but the device's status
    // still arrived as an event while it was in flight.
    await h.ConnectAsync(statusRead: () =>
    {
      h.RaiseStatus(0.30, muted: false);
      return Task.FromResult<(float, bool)?>(null);
    });

    Assert.Empty(h.External);

    // The early status became the baseline, so its repeat is not mistaken for a change.
    h.RaiseStatus(0.30, muted: false);
    Assert.Empty(h.External);
  }

  [Fact]
  public async Task AnExternalChange_IsReported_UpdatesTheKnownLevelFirst_AndIsRemembered()
  {
    await using var h = NewHarness();
    await h.ConnectAsync(reportedLevel: 0.40f);

    float? knownWhenFired = null;
    h.Output.CastVolumeChanged += (_, e) =>
    {
      if (!e.IsInitialSync)
      {
        knownWhenFired = h.Output.KnownSpeakerLevel;
      }
    };

    h.RaiseStatus(0.55);

    Assert.Equal(0.55f, Assert.Single(h.External).Volume, 3);
    // The follower relies on this ordering: the subscriber's master write must find the
    // speaker's known level already equal to it.
    Assert.Equal(0.55f, knownWhenFired!.Value, 3);
    Assert.Equal(0.55f, h.Store.Volumes["cast-a"], 3);
  }

  [Fact]
  public async Task AnExternalChange_DropsAQueuedConsoleTarget_AndBecomesTheKnownLevel()
  {
    await using var h = NewHarness();
    await h.ConnectAsync(reportedLevel: 0.40f);
    var target = h.Target();
    h.ClearCommands();

    h.VolumeGate = CastConsoleTestHarness.NewTcs();
    var burst = h.Output.SetDeviceVolumeFromConsoleAsync(0.30f, target.Generation);
    await h.VolumeSendEntered.Task;                                     // 0.30 in flight
    Assert.Same(burst, h.Output.SetDeviceVolumeFromConsoleAsync(0.36f, target.Generation)); // 0.36 queued

    h.RaiseStatus(0.55); // changed on the speaker meanwhile

    // The re-synced master volume (0.55) must find the speaker already there, or the follower
    // would push a mapped value back at it.
    Assert.Equal(0.55f, h.Target().SpeakerLevel, 3);

    h.VolumeGate.SetResult();
    await burst;

    // The queued 0.36 was never sent. (The in-flight 0.30 was not recallable.)
    Assert.Equal(new[] { 0.30f }, h.VolumeSends());
  }

  [Fact]
  public async Task AConsoleTargetForASupersededConnection_IsNeverSent()
  {
    await using var h = NewHarness();
    await h.ConnectAsync("cast-a", reportedLevel: 0.40f);
    var oldTarget = h.Target();

    await h.Output.DisconnectAsync();
    await h.ConnectAsync("cast-b", reportedLevel: 0.40f);
    var newTarget = h.Target();
    Assert.NotEqual(oldTarget.Generation, newTarget.Generation);
    h.ClearCommands();

    var stale = await h.Output.SetDeviceVolumeFromConsoleAsync(0.9f, oldTarget.Generation);
    Assert.Empty(h.VolumeSends());
    Assert.Null(stale.AppliedLevel);
    Assert.False(h.Store.Remembered.Exists(r => Math.Abs(r.Volume - 0.9f) < 0.001f));

    await h.Output.SetDeviceVolumeFromConsoleAsync(0.2f, newTarget.Generation);
    Assert.Equal(new[] { 0.2f }, h.VolumeSends());
  }

  [Fact]
  public async Task NothingIsSent_WhenTheOutputIsNotStreaming()
  {
    await using var h = NewHarness();
    await h.ConnectAsync(reportedLevel: 0.40f, streaming: false);
    Assert.Null(h.Output.GetConsoleVolumeTarget());
    h.ClearCommands();

    var result = await h.Output.SetDeviceVolumeFromConsoleAsync(0.9f, 1);
    Assert.False(await h.Output.SetDeviceMuteFromConsoleAsync(true, 1));

    Assert.Empty(h.Commands);
    Assert.Null(result.AppliedLevel);
  }

  [Fact]
  public async Task AFailedPush_IsReportedOnce_AndTheKnownLevelIsUnchanged()
  {
    await using var h = NewHarness();
    await h.ConnectAsync(reportedLevel: 0.40f);
    var target = h.Target();
    var rememberedBefore = h.Store.Remembered.Count;

    h.FailNextVolume = new InvalidOperationException("socket closed");
    var result = await h.Output.SetDeviceVolumeFromConsoleAsync(0.8f, target.Generation);

    Assert.True(result.Failed);
    Assert.Null(result.AppliedLevel);
    Assert.Equal(0.40f, h.Output.KnownSpeakerLevel, 3);
    Assert.Equal(rememberedBefore, h.Store.Remembered.Count);
  }

  [Fact]
  public async Task TheLiveRead_ReportsTheSpeakersStatus_BesideTheKnownLevel_AndChangesNothing()
  {
    await using var h = NewHarness();
    Assert.Null(await h.Output.ReadSpeakerVolumeAsync()); // nothing connected

    await h.ConnectAsync(reportedLevel: 0.40f);
    var target = h.Target();
    await h.Output.SetDeviceVolumeFromConsoleAsync(0.30f, target.Generation);
    h.ClearCommands();

    h.Output.CastStatusReadOverrideForTests = () => Task.FromResult<(float, bool)?>((0.31f, true));
    var reading = await h.Output.ReadSpeakerVolumeAsync();

    Assert.NotNull(reading);
    Assert.Equal("Speaker cast-a", reading!.DeviceName);
    Assert.Equal(0.31f, reading.Level!.Value, 3);
    Assert.True(reading.Muted);
    Assert.Equal(0.30f, reading.KnownLevel!.Value, 3);
    Assert.Empty(h.External);
    Assert.Empty(h.Commands);
    Assert.Equal(0.30f, h.Output.KnownSpeakerLevel, 3);
  }

  // --- mute ---

  [Fact]
  public async Task AConsoleMuteWhileCasting_MutesTheSpeaker_AndADeliberateStopUnmutesIt()
  {
    await using var h = NewHarness();
    await h.ConnectAsync(reportedLevel: 0.40f);
    var target = h.Target();
    h.ClearCommands();
    var storeBefore = new Dictionary<string, float>(h.Store.Volumes);

    Assert.True(await h.Output.SetDeviceMuteFromConsoleAsync(true, target.Generation));
    Assert.True(h.Output.IsSpeakerMutedByConsole);
    Assert.True(h.Output.KnownSpeakerMuted);

    // No console follower is attached, so the console's own mute is unknown and the level is sent.
    // (With a muted console it is held instead — see UnderAMutedConsole_… and the follower tests.)
    await h.Output.SetDeviceVolumeFromConsoleAsync(0.25f, target.Generation);

    if (OnTheDeviceModel)
    {
      // That level unmuted the speaker (its reply arrived inside the send), and the console — as
      // far as this output can tell — is not muted: the unmute matches it, so nothing is left to
      // release at the stop. It is not reported as a change made on the speaker either.
      Assert.False(h.Output.IsSpeakerMutedByConsole);
      Assert.False(h.Output.KnownSpeakerMuted);
      Assert.Empty(h.External);
    }

    await h.Output.StopAsync();

    Assert.Equal(OnTheDeviceModel ? new[] { true } : new[] { true, false }, h.MuteSends());
    Assert.Equal(new[] { 0.25f }, h.VolumeSends());
    Assert.False(h.Output.IsSpeakerMutedByConsole);

    // Mute is never written to the per-device store; only the level is.
    Assert.Equal(0.25f, h.Store.Volumes["cast-a"], 3);
    Assert.Equal(storeBefore.Keys, h.Store.Volumes.Keys);
  }

  [Fact]
  public async Task ADeliberateDisconnect_UnmutesASpeakerTheConsoleMuted()
  {
    await using var h = NewHarness();
    await h.ConnectAsync(reportedLevel: 0.40f);
    var target = h.Target();
    h.ClearCommands();

    Assert.True(await h.Output.SetDeviceMuteFromConsoleAsync(true, target.Generation));
    await h.Output.DisconnectAsync();

    Assert.Equal(new[] { true, false }, h.MuteSends());
    Assert.False(h.Output.IsSpeakerMutedByConsole);
  }

  [Fact]
  public async Task ATeardownSendsNoUnmute_WhenTheConsoleNeverMutedTheSpeaker()
  {
    await using var h = NewHarness();
    await h.ConnectAsync(reportedLevel: 0.40f, reportedMuted: true); // muted on the speaker itself
    h.ClearCommands();

    await h.Output.StopAsync();
    await h.Output.DisconnectAsync();

    Assert.Empty(h.MuteSends());
    Assert.DoesNotContain("appstop", h.Kinds()); // the receiver application is left as before AUD-81
  }

  // Pre-merge review H1: the release used to run BEFORE the media stop and never stopped the
  // receiver, so a muted console's audio — the live HttpMp3 stream, or up to 3 s of DirectChannel
  // audio the receiver had buffered — could play out loud after the unmute.
  [Fact]
  public async Task AStop_StopsTheReceiverApplication_BeforeUnmutingASpeakerTheConsoleMuted()
  {
    await using var h = NewHarness();
    await h.ConnectAsync(reportedLevel: 0.40f);
    var target = h.Target();
    h.ClearCommands();

    Assert.True(await h.Output.SetDeviceMuteFromConsoleAsync(true, target.Generation));
    await h.Output.StopAsync();

    Assert.Equal(new[] { "mute", "appstop", "unmute" }, h.Kinds());
    Assert.False(h.Output.IsSpeakerMutedByConsole);
  }

  [Theory]
  [InlineData(false)] // the device still lists our application, or another one is first
  [InlineData(true)]  // the stop threw (socket closed, timed out)
  public async Task WhenTheReceiverApplicationCannotBeConfirmedStopped_TheSpeakerIsLeftMuted(bool throws)
  {
    await using var h = NewHarness();
    await h.ConnectAsync(reportedLevel: 0.40f);
    var target = h.Target();
    h.ClearCommands();
    h.AppStop = throws
      ? () => Task.FromException<bool>(new InvalidOperationException("socket closed"))
      : () => Task.FromResult(false);

    Assert.True(await h.Output.SetDeviceMuteFromConsoleAsync(true, target.Generation));
    await h.Output.StopAsync();

    Assert.Equal(new[] { "mute", "appstop" }, h.Kinds());
    Assert.True(h.Output.IsSpeakerMutedByConsole); // kept, so the disconnect can try again

    // The disconnect retries; once the application is confirmed stopped, the unmute follows it.
    h.AppStop = () => Task.FromResult(true);
    await h.Output.DisconnectAsync();

    Assert.Equal(new[] { "mute", "appstop", "appstop", "unmute" }, h.Kinds());
    Assert.False(h.Output.IsSpeakerMutedByConsole);
  }

  // Hostile review F4. The REAL StopReceiverApplicationAsync (the harness seam removed): this
  // client's ReceiverChannel has never received a status, so nothing is known about what is
  // running. That used to count as "nothing of ours is running" and the unmute was sent.
  // Measured while writing this: SharpCaster's ReceiverChannel.ReceiverStatus is then NOT null —
  // it is a default status whose Applications is null — so a null-status check alone did not
  // catch it (the first version of this fix failed this test).
  [Fact]
  public async Task WithNoReceiverStatusEverReceived_ATeardownLeavesTheConsoleMutedSpeakerMuted()
  {
    await using var h = NewHarness();
    await h.ConnectAsync(reportedLevel: 0.40f);
    var target = h.Target();
    h.ClearCommands();
    h.Output.CastStopApplicationOverrideForTests = null;

    // The premise, as SharpCaster 3.0.0 actually presents "never received": a default status
    // object with no applications list — not a null status.
    var held = h.CurrentClient()!.GetChannel<Sharpcaster.Channels.ReceiverChannel>()!.ReceiverStatus;
    Assert.Null(held?.Applications);

    Assert.True(await h.Output.SetDeviceMuteFromConsoleAsync(true, target.Generation));
    await h.Output.DisconnectAsync();

    Assert.Equal(new[] { "mute" }, h.Kinds());
    Assert.True(h.Output.IsSpeakerMutedByConsole);
  }

  [Fact]
  public async Task ADisconnectWithoutAStop_StopsTheReceiverApplication_BeforeTheUnmute()
  {
    // DisposeAsync and the device-switch path can reach DisconnectAsync while still streaming.
    await using var h = NewHarness();
    await h.ConnectAsync(reportedLevel: 0.40f);
    var target = h.Target();
    h.ClearCommands();

    Assert.True(await h.Output.SetDeviceMuteFromConsoleAsync(true, target.Generation));
    await h.Output.DisconnectAsync();

    Assert.Equal(new[] { "mute", "appstop", "unmute" }, h.Kinds());
  }

  // The after-start push used to re-send the speaker's own level after the start-time mute. Since
  // the AUD-81 follow-up a muted speaker that already holds the level gets no SET_VOLUME at all
  // (box UAT 2026-10-01: a level change unmutes it; re-sending its own level is not needed).
  [Fact]
  public async Task AMutedConsole_MutesTheSpeakerAtStart_AndSendsNoLevelItAlreadyHolds()
  {
    await using var h = NewHarness();
    h.Output.AttachConsoleFollower(() => true, new Microsoft.Extensions.Logging.Abstractions.NullLogger<GoogleCastOutputConsoleVolumeTests>());
    await h.ConnectAsync(reportedLevel: 0.40f, reportedMuted: false);
    h.ClearCommands();

    await h.Output.SyncVolumeAfterStartAsync();

    Assert.Equal(new[] { "mute" }, h.Kinds());
    Assert.True(h.Output.IsSpeakerMutedByConsole);
    Assert.Null(h.Output.HeldConsoleVolume(h.Target().Generation)); // nothing owed at the unmute either
  }

  // The same rule for a speaker muted on its own side under an unmuted console: it already holds
  // the level, so nothing is sent — a SET_VOLUME (and the re-mute it would need) is avoided.
  [Fact]
  public async Task AfterStart_ASpeakerMutedOnItsOwnSide_AtTheSameLevel_GetsNoCommandAtAll()
  {
    await using var h = NewHarness();
    h.Output.AttachConsoleFollower(() => false, NullLogger.Instance);
    await h.ConnectAsync(reportedLevel: 0.40f, reportedMuted: true);
    h.ClearCommands();

    await h.Output.SyncVolumeAfterStartAsync();

    Assert.Empty(h.Commands);
    Assert.True(h.Output.KnownSpeakerMuted);
  }

  // A speaker muted on its own side whose level is owed (the restore on connect failed): the push
  // is unavoidable here (the console is not muted, so nothing will release a held level), and the
  // mute is re-asserted straight after it.
  [Fact]
  public async Task AfterStart_ALevelPushToASpeakerMutedOnItsOwnSide_IsFollowedByAMuteReassert()
  {
    await using var h = NewHarness();
    h.Output.AttachConsoleFollower(() => false, NullLogger.Instance);
    h.Store.Volumes["cast-a"] = 0.60f;
    h.FailNextVolume = new TimeoutException("restore lost");
    await h.ConnectAsync(reportedLevel: 0.40f, reportedMuted: true);
    h.ClearCommands();

    await h.Output.SyncVolumeAfterStartAsync();

    Assert.Equal(new[] { "vol", "mute" }, h.Kinds());
    Assert.Equal(0.60f, h.VolumeSends()[0], 3);
    Assert.False(h.Output.IsSpeakerMutedByConsole);
  }

  // D1 (c). The after-start push has a DIFFERENT level to apply (the AUD-80 restore on connect
  // failed, so the remembered level is still owed) while the console is muted. Sending it would
  // unmute the speaker while our audio streams; it is held, and the console's unmute sends it
  // before the unmute.
  [Fact]
  public async Task UnderAMutedConsole_TheAfterStartLevelIsHeld_NotSent_AndGoesOutBeforeTheUnmute()
  {
    await using var h = NewHarness();
    var consoleMuted = true;
    h.Output.AttachConsoleFollower(() => consoleMuted, NullLogger.Instance);
    h.Store.Volumes["cast-a"] = 0.60f;
    h.FailNextVolume = new TimeoutException("restore lost"); // the restore on connect fails
    await h.ConnectAsync(reportedLevel: 0.40f, reportedMuted: false);
    h.ClearCommands();

    await h.Output.SyncVolumeAfterStartAsync();

    Assert.Equal(new[] { "mute" }, h.Kinds());            // no SET_VOLUME under the muted console
    var target = h.Target();
    Assert.Equal(0.60f, h.Output.HeldConsoleVolume(target.Generation)!.Value, 3);
    Assert.True(h.Output.KnownSpeakerMuted);

    consoleMuted = false;
    var rememberedBefore = h.Store.Remembered.Count;
    var result = await h.Output.SetDeviceMuteFromConsoleWithLevelAsync(false, target.Generation);

    Assert.True(result.Acknowledged);
    Assert.Equal(0.60f, result.LevelBeforeUnmute!.Value, 3);
    Assert.Equal(new[] { "mute", "vol", "unmute" }, h.Kinds());
    Assert.Equal(0.60f, Assert.Single(h.VolumeSends()), 3);

    // Hostile review M1 (on the device model the level unmutes the speaker first): still remembered
    // for AUD-80 once, as the level the speaker now holds, and never reported as a speaker change.
    Assert.Equal(new[] { ("cast-a", 0.60f) }, h.Store.Remembered.Skip(rememberedBefore).ToList());
    Assert.Equal(0.60f, h.Output.KnownSpeakerLevel, 3);
    Assert.Empty(h.External);
    Assert.False(h.Output.IsSpeakerMutedByConsole);
  }

  // Hostile review H1. The console is muted and the speaker is muted on its OWN side, so the
  // start-time mute is skipped (already muted) and the speaker is not marked as the console's. The
  // after-start push has a different level owed (the restore on connect failed). It used to be
  // pushed (step 3): on the device the level unmutes the speaker, the reply was reported as an
  // external unmute, and AudioStateUpdateService UNMUTED THE CONSOLE — by our own command. Now the
  // level is held: nothing is sent, the speaker stays muted, and the console's unmute releases it.
  [Fact]
  public async Task UnderAMutedConsole_ASpeakerMutedOnItsOwnSide_GetsNoLevelAfterStart_AndTheConsoleStaysMuted()
  {
    await using var h = NewHarness();
    var consoleMuted = true;
    h.Output.AttachConsoleFollower(() => consoleMuted, NullLogger.Instance);
    // Mirrors AudioStateUpdateService.OnCastVolumeChanged: a non-initial event's mute is written
    // to the console.
    h.Output.CastVolumeChanged += (_, e) =>
    {
      if (!e.IsInitialSync)
      {
        consoleMuted = e.IsMuted;
      }
    };
    h.Store.Volumes["cast-a"] = 0.60f;
    h.FailNextVolume = new TimeoutException("restore lost"); // the restore on connect fails
    await h.ConnectAsync(reportedLevel: 0.30f, reportedMuted: true);
    Assert.False(h.Output.IsSpeakerMutedByConsole); // muted on its own side: not the console's
    h.ClearCommands();

    await h.Output.SyncVolumeAfterStartAsync();
    await h.Output.LastMuteReassertForTests;

    Assert.Empty(h.Commands);                       // no SET_VOLUME (and so nothing to re-mute)
    Assert.Equal(0.60f, h.Output.HeldConsoleVolume(h.Target().Generation)!.Value, 3);
    Assert.True(consoleMuted);
    Assert.Empty(h.External);
    Assert.True(h.DeviceMuted);
    Assert.True(h.Output.KnownSpeakerMuted);

    // The console's unmute sends the held level first, then the unmute (whoever muted the speaker).
    consoleMuted = false;
    var result = await h.Output.SetDeviceMuteFromConsoleWithLevelAsync(false, h.Target().Generation);
    Assert.True(result.Acknowledged);
    Assert.Equal(new[] { "vol", "unmute" }, h.Kinds());
    Assert.False(h.DeviceMuted);
    Assert.Empty(h.External);
  }

  // The AUD-80 restore on connect, to a speaker the read found MUTED (on its own side here): the
  // SET_VOLUME unmutes it, so the mute is re-asserted straight after it.
  [Fact]
  public async Task TheRestoreOnConnect_ToAMutedSpeaker_IsFollowedByAMuteReassert()
  {
    await using var h = NewHarness();
    h.Store.Volumes["cast-a"] = 0.60f;

    await h.ConnectAsync(reportedLevel: 0.40f, reportedMuted: true);

    Assert.Equal(new[] { "vol", "mute" }, h.Kinds());
    Assert.True(h.Output.KnownSpeakerMuted);
    Assert.False(h.Output.IsSpeakerMutedByConsole); // muted on its own side: not claimed
  }

  /// <summary>
  /// A console-muted speaker with a level push of ours still recent: the console is unmuted while
  /// the push is sent (it was in flight when the console muted), then muted. Returns the console's
  /// mute switch, now true; commands cleared.
  /// </summary>
  private static async Task ConsoleMutedAfterALevelPushAsync(CastConsoleTestHarness h)
  {
    var consoleMuted = false;
    h.Output.AttachConsoleFollower(() => consoleMuted, NullLogger.Instance);
    await h.ConnectAsync(reportedLevel: 0.30f);
    var target = h.Target();
    Assert.True(await h.Output.SetDeviceMuteFromConsoleAsync(true, target.Generation));
    // The device model is off for this push: these tests raise the device's reply to it
    // themselves, at the moment they choose (on the model it would arrive inside the send, while
    // the console still reads unmuted).
    var model = h.LevelCommandUnmutes;
    h.LevelCommandUnmutes = false;
    await h.Output.SetDeviceVolumeFromConsoleAsync(0.45f, target.Generation);
    h.LevelCommandUnmutes = model;
    consoleMuted = true;
    Assert.True(h.Output.IsSpeakerMutedByConsole);
    Assert.Equal(new[] { "mute", "vol" }, h.Kinds());
    h.ClearCommands();
  }

  // D1 (d). The speaker reports itself unmuted, at the level we just pushed, under a muted console:
  // the unmute our level push caused. Not an external change (it would unmute the console); the
  // mute is re-asserted.
  [Fact]
  public async Task AnUnmuteEchoingOurOwnLevelPush_UnderAMutedConsole_IsReassertedAsMuted_NotReported()
  {
    await using var h = NewHarness();
    await ConsoleMutedAfterALevelPushAsync(h);

    h.Time.Advance(TimeSpan.FromSeconds(1)); // inside EchoWindow of the 0.45 push
    h.RaiseStatus(0.45, muted: false);
    await h.Output.LastMuteReassertForTests;

    Assert.Empty(h.External);
    Assert.Equal(new[] { "mute" }, h.Kinds());
    Assert.True(h.Output.KnownSpeakerMuted);
    Assert.True(h.Output.IsSpeakerMutedByConsole);
  }

  // D1 (d), the race: the console is unmuted between the status and the re-assert's turn on the
  // mute drain. The re-assert is then not sent — the console's own unmute is the last word. Driven
  // by the console-state reads, not by timing: the status handler's read sees the console muted,
  // and every later read (the drain's) sees it unmuted.
  [Fact]
  public async Task AMuteReassert_IsNotSent_WhenTheConsoleWasUnmutedBeforeItsTurn()
  {
    await using var h = NewHarness();
    var armed = false;
    var reads = 0;
    h.Output.AttachConsoleFollower(() => armed && Interlocked.Increment(ref reads) == 1, NullLogger.Instance);
    await h.ConnectAsync(reportedLevel: 0.30f);
    var target = h.Target();
    Assert.True(await h.Output.SetDeviceMuteFromConsoleAsync(true, target.Generation));
    h.LevelCommandUnmutes = false; // the device's reply is raised by hand below (see ConsoleMutedAfterALevelPushAsync)
    await h.Output.SetDeviceVolumeFromConsoleAsync(0.45f, target.Generation);
    h.ClearCommands();
    armed = true;

    h.RaiseStatus(0.45, muted: false);
    await h.Output.LastMuteReassertForTests;

    Assert.Equal(2, Volatile.Read(ref reads)); // the status saw "muted", the drain "unmuted"
    Assert.Empty(h.Commands);
    Assert.Empty(h.External);
  }

  // Hostile review L2. A re-assert is sent once and not retried; a failed one used to leave only a
  // Debug-level trace under …Audio.Outputs (held at Warning by LOG-2). It is a Warning on the
  // follower's logger now.
  [Fact]
  public async Task AFailedMuteReassert_IsLoggedAsAWarningOnTheConsoleLog()
  {
    await using var h = NewHarness();
    var log = new RecordingLogger();
    var consoleMuted = false;
    h.Output.AttachConsoleFollower(() => consoleMuted, log);
    await h.ConnectAsync(reportedLevel: 0.30f);
    var target = h.Target();
    Assert.True(await h.Output.SetDeviceMuteFromConsoleAsync(true, target.Generation));
    h.LevelCommandUnmutes = false; // the device's reply is raised by hand below (see ConsoleMutedAfterALevelPushAsync)
    await h.Output.SetDeviceVolumeFromConsoleAsync(0.45f, target.Generation);
    consoleMuted = true;

    h.FailNextMute = new TimeoutException("closed");
    h.RaiseStatus(0.45, muted: false);
    await h.Output.LastMuteReassertForTests;

    Assert.Single(log.Lines(), l => l.Level == LogLevel.Warning && l.Line.Contains("could not be muted again"));
    Assert.DoesNotContain(log.Lines(), l => l.Line.Contains("muted it again"));
    Assert.Empty(h.External);
  }

  // Hostile review L3. The set-up of the test above leaves the speaker unmuted (by our level) yet
  // still marked, and recorded, as muted for the console: the re-assert was dropped. The console's
  // unmute then has nothing to send ("already unmuted") — and used to leave the mark and the record
  // behind, so a later connection finding the speaker muted by its owner would re-arm the mark
  // (F11) and the activation reconcile would unmute the owner's mute.
  [Fact]
  public async Task AConsoleUnmuteSkippedAsAlreadyUnmuted_StillReleasesTheConsoleMute()
  {
    await using var h = NewHarness();
    var armed = false;
    var reads = 0;
    h.Output.AttachConsoleFollower(() => armed && Interlocked.Increment(ref reads) == 1, NullLogger.Instance);
    await h.ConnectAsync(reportedLevel: 0.30f);
    var target = h.Target();
    Assert.True(await h.Output.SetDeviceMuteFromConsoleAsync(true, target.Generation));
    h.LevelCommandUnmutes = false; // the device's reply is raised by hand below (see ConsoleMutedAfterALevelPushAsync)
    await h.Output.SetDeviceVolumeFromConsoleAsync(0.45f, target.Generation);
    armed = true;
    h.RaiseStatus(0.45, muted: false);
    await h.Output.LastMuteReassertForTests;
    Assert.True(h.Output.IsSpeakerMutedByConsole); // the premise: still marked, though unmuted
    Assert.False(h.Output.KnownSpeakerMuted);
    h.ClearCommands();

    await h.Output.SetDeviceMuteFromConsoleWithLevelAsync(false, target.Generation);

    Assert.Empty(h.Commands);                       // nothing to send: already unmuted
    Assert.False(h.Output.IsSpeakerMutedByConsole);

    await h.Output.DisconnectAsync();
    Assert.Empty(h.Commands);                       // nothing of ours to release at the teardown

    // The record is forgotten too: the owner mutes it later, and a reconnect does not claim it.
    await h.ConnectAsync(reportedLevel: 0.45f, reportedMuted: true, streaming: false);
    Assert.False(h.Output.IsSpeakerMutedByConsole);
  }

  // Hostile review M3. The console mute drain has one pending slot, latest wins. A re-assert queued
  // while a console UNMUTE was waiting there used to replace it; the re-assert is then dropped (the
  // console is unmuted by its turn), so the console's unmute — and the held level it carries — was
  // lost. Driven by the mute gate: the re-assert is raised while a console mute holds the drain.
  [Fact]
  public async Task AMuteReassert_NeverReplacesAPendingConsoleUnmute()
  {
    await using var h = NewHarness();
    var consoleMuted = false;
    h.Output.AttachConsoleFollower(() => consoleMuted, NullLogger.Instance);
    await h.ConnectAsync(reportedLevel: 0.30f);
    var target = h.Target();
    await h.Output.SetDeviceVolumeFromConsoleAsync(0.45f, target.Generation); // a recent level push of ours
    h.ClearCommands();

    consoleMuted = true;
    h.MuteGate = CastConsoleTestHarness.NewTcs();
    var mute = h.Output.SetDeviceMuteFromConsoleAsync(true, target.Generation);
    await h.MuteSendEntered.Task;                                    // the console mute holds the drain
    await h.Output.SetDeviceVolumeFromConsoleAsync(0.50f, target.Generation); // held: the console is muted
    Assert.Equal(0.50f, h.Output.HeldConsoleVolume(target.Generation)!.Value, 3);

    consoleMuted = false;
    var unmute = h.Output.SetDeviceMuteFromConsoleWithLevelAsync(false, target.Generation); // pending

    // The speaker reports itself unmuted at our 0.45 while the console mute is still being sent:
    // the level-echo rule re-asserts the mute (a console mute is in flight).
    h.RaiseStatus(0.45, muted: false);

    h.MuteGate.SetResult();
    await mute;
    var result = await unmute;
    await h.Output.LastMuteReassertForTests;

    Assert.True(result.Acknowledged);
    Assert.Equal(0.50f, result.LevelBeforeUnmute!.Value, 3);
    Assert.Equal(new[] { "mute", "vol", "unmute" }, h.Kinds());
    Assert.Null(h.Output.HeldConsoleVolume(target.Generation));
    Assert.False(h.Output.IsSpeakerMutedByConsole);
    Assert.False(h.DeviceMuted);
    Assert.Empty(h.External);
  }

  // Hostile review M2. A console mute while a console level is waiting on SharpCaster's send lock:
  // the SET_MUTE reaches the device first (it mutes), then the SET_VOLUME (it unmutes the speaker
  // again), and the level's reply arrives BEFORE the mute's acknowledgement has been processed — so
  // the speaker is not yet marked muted by the console. That reply used to be reported as an
  // external unmute: the console was unmuted, and the drain then set a stale mark. Driven by the
  // gates, not by timing: the level is released while the mute's reply is still held.
  [Fact]
  public async Task AConsoleMute_RacingALevelOnTheSendLock_EndsWithTheSpeakerMuted_AndTheConsoleMuted()
  {
    await using var h = NewHarness();
    var consoleMuted = false;
    h.Output.AttachConsoleFollower(() => consoleMuted, NullLogger.Instance);
    // Mirrors AudioStateUpdateService.OnCastVolumeChanged: a non-initial event's mute is written
    // to the console.
    h.Output.CastVolumeChanged += (_, e) =>
    {
      if (!e.IsInitialSync)
      {
        consoleMuted = e.IsMuted;
      }
    };
    await h.ConnectAsync(reportedLevel: 0.30f);
    var target = h.Target();
    h.ClearCommands();

    h.VolumeGate = CastConsoleTestHarness.NewTcs();
    var burst = h.Output.SetDeviceVolumeFromConsoleAsync(0.45f, target.Generation);
    await h.VolumeSendEntered.Task;                 // 0.45 waits on the send lock

    consoleMuted = true;                            // the console mutes
    h.MuteGate = CastConsoleTestHarness.NewTcs();
    var mute = h.Output.SetDeviceMuteFromConsoleAsync(true, target.Generation);
    await h.MuteSendEntered.Task;                   // the SET_MUTE reached the device; its reply is held
    Assert.False(h.Output.IsSpeakerMutedByConsole); // not acknowledged yet

    h.VolumeGate.SetResult();                       // the level reaches the device
    await burst;
    if (OnTheDeviceModel)
    {
      Assert.False(h.DeviceMuted);                  // the premise: the level unmuted it
    }

    h.MuteGate.SetResult();
    Assert.True(await mute);
    await h.Output.LastMuteReassertForTests;

    Assert.True(consoleMuted);
    Assert.Empty(h.External);
    Assert.True(h.DeviceMuted);
    Assert.True(h.Output.IsSpeakerMutedByConsole);
    Assert.True(h.Output.KnownSpeakerMuted);
    Assert.Equal(
      OnTheDeviceModel ? new[] { "vol", "mute", "mute" } : new[] { "vol", "mute" },
      h.Kinds());
  }

  // D1 (e). The owner unmutes on the speaker itself, outside any echo window of our level pushes:
  // handled as an external change exactly as before (AUD-5) — reported, mark cleared, nothing sent.
  [Fact]
  public async Task AnOwnerUnmuteOutsideTheEchoWindow_IsStillAnExternalChange()
  {
    await using var h = NewHarness();
    await ConsoleMutedAfterALevelPushAsync(h);

    h.Time.Advance(TimeSpan.FromSeconds(5)); // past EchoWindow of the 0.45 push
    h.RaiseStatus(0.45, muted: false);
    await h.Output.LastMuteReassertForTests;

    var external = Assert.Single(h.External);
    Assert.False(external.IsMuted);
    Assert.Empty(h.Commands);
    Assert.False(h.Output.IsSpeakerMutedByConsole);
  }

  [Fact]
  public async Task StreamingStart_NeverUnmutesASpeaker_AndDoesNotClaimOneMutedOnItsOwnSide()
  {
    // Console not muted, speaker muted on its own side: left alone.
    await using (var h = NewHarness())
    {
      h.Output.AttachConsoleFollower(() => false, new Microsoft.Extensions.Logging.Abstractions.NullLogger<GoogleCastOutputConsoleVolumeTests>());
      await h.ConnectAsync(reportedLevel: 0.40f, reportedMuted: true);
      h.ClearCommands();

      await h.Output.SyncVolumeAfterStartAsync();

      Assert.Empty(h.MuteSends());
      Assert.False(h.Output.IsSpeakerMutedByConsole);
    }

    // Console muted, speaker already muted: nothing sent, and not marked as ours to release.
    await using (var h = NewHarness())
    {
      h.Output.AttachConsoleFollower(() => true, new Microsoft.Extensions.Logging.Abstractions.NullLogger<GoogleCastOutputConsoleVolumeTests>());
      await h.ConnectAsync(reportedLevel: 0.40f, reportedMuted: true);
      h.ClearCommands();

      await h.Output.SyncVolumeAfterStartAsync();
      await h.Output.StopAsync();

      Assert.Empty(h.MuteSends());
      Assert.False(h.Output.IsSpeakerMutedByConsole);
    }
  }

  [Fact]
  public async Task AnExternalMute_IsNotPushedBack_AndItsConfirmationOfOurMuteIsAnEcho()
  {
    await using var h = NewHarness();
    await h.ConnectAsync(reportedLevel: 0.40f);
    var target = h.Target();
    h.ClearCommands();

    // Muted on the speaker: reported, and the console's resulting mute is not sent back.
    h.RaiseStatus(0.40, muted: true);
    Assert.True(Assert.Single(h.External).IsMuted);
    Assert.False(await h.Output.SetDeviceMuteFromConsoleAsync(true, target.Generation));
    Assert.Empty(h.MuteSends());
    Assert.False(h.Output.IsSpeakerMutedByConsole);

    // Console unmute, mute, unmute in quick succession; the device's confirmations arrive late,
    // after the last command: the "muted" one is our own and must not read as a re-mute.
    Assert.True(await h.Output.SetDeviceMuteFromConsoleAsync(false, target.Generation));
    Assert.True(await h.Output.SetDeviceMuteFromConsoleAsync(true, target.Generation));
    Assert.True(await h.Output.SetDeviceMuteFromConsoleAsync(false, target.Generation));
    h.RaiseStatus(0.40, muted: false);
    h.RaiseStatus(0.40, muted: true);
    h.RaiseStatus(0.40, muted: false);
    Assert.Single(h.External);
    Assert.Equal(new[] { false, true, false }, h.MuteSends());
  }

  [Fact]
  public async Task ALostConnection_ClearsTheConsoleMuteMark_WithoutSendingAnUnmute()
  {
    await using var h = NewHarness();
    await h.ConnectAsync(reportedLevel: 0.40f);
    var target = h.Target();
    h.ClearCommands();

    Assert.True(await h.Output.SetDeviceMuteFromConsoleAsync(true, target.Generation));
    h.Output.ReportConnectionLost(target.Generation, "test", null);
    await h.Output.LastConnectionLossHandling;

    Assert.Equal(AudioOutputState.Error, h.Output.State);
    Assert.Equal(new[] { true }, h.MuteSends());
    Assert.False(h.Output.IsSpeakerMutedByConsole);
  }

  // Pre-merge review M2: the echo baseline survived across connections, so after a console-muted
  // speaker was LOST, _lastSetMute was still true; a reconnect whose initial read failed then
  // skipped the start-time mute ("already muted") and streamed unmuted under a muted console.
  [Fact]
  public async Task AReconnectWhoseInitialReadFails_StillMutesTheSpeakerForAMutedConsole()
  {
    await using var h = NewHarness();
    h.Output.AttachConsoleFollower(() => true, NullLogger.Instance);
    await h.ConnectAsync(reportedLevel: 0.40f);
    var target = h.Target();
    Assert.True(await h.Output.SetDeviceMuteFromConsoleAsync(true, target.Generation));

    h.Output.ReportConnectionLost(target.Generation, "test", null);
    await h.Output.LastConnectionLossHandling;
    h.ClearCommands();

    // The same speaker again; its status cannot be read, so its mute state is unknown.
    await h.ConnectAsync(statusRead: () => Task.FromException<(float, bool)?>(new TimeoutException("no answer")));
    Assert.False(h.Output.KnownSpeakerMuted);

    await h.Output.SyncVolumeAfterStartAsync();

    Assert.Equal(new[] { true }, h.MuteSends());
    Assert.True(h.Output.IsSpeakerMutedByConsole);
  }

  // Pre-merge review M3 (a): the start-time mute runs before Streaming, and the follower ignores
  // the console until Streaming, so a mute made in between was lost.
  [Fact]
  public async Task AConsoleMuteMadeWhileTheStreamWasStarting_IsAppliedOnReachingStreaming()
  {
    await using var h = NewHarness();
    var consoleMuted = false;
    h.Output.AttachConsoleFollower(() => consoleMuted, NullLogger.Instance);
    await h.ConnectAsync(reportedLevel: 0.40f, streaming: false);

    await h.Output.SyncVolumeAfterStartAsync(); // the start-time mute: console not muted yet
    consoleMuted = true;                         // muted in the window
    h.ClearCommands();

    h.MarkStreaming();
    await h.Output.OnReachedStreamingAsync();

    Assert.Equal(new[] { true }, h.MuteSends());
    Assert.True(h.Output.IsSpeakerMutedByConsole);
  }

  // Pre-merge review L1/L2: the drain wrote _connectionVolume (and remembered it) unconditionally
  // when its send completed, overwriting a change the speaker reported meanwhile.
  [Fact]
  public async Task AnExternalChangeWhileAPushIsInFlight_IsNotOverwrittenWhenThePushCompletes()
  {
    await using var h = NewHarness();
    await h.ConnectAsync(reportedLevel: 0.40f);
    var target = h.Target();
    h.ClearCommands();

    h.VolumeGate = CastConsoleTestHarness.NewTcs();
    var burst = h.Output.SetDeviceVolumeFromConsoleAsync(0.30f, target.Generation);
    await h.VolumeSendEntered.Task;   // 0.30 in flight
    h.RaiseStatus(0.55);             // changed on the speaker meanwhile
    h.VolumeGate.SetResult();
    var result = await burst;

    Assert.Equal(0.55f, h.Output.KnownSpeakerLevel, 3);
    Assert.Equal(0.55f, h.Store.Volumes["cast-a"], 3);
    Assert.Null(result.AppliedLevel);
  }

  [Fact]
  public async Task APushThatCompletesAfterANewConnection_IsNotRecordedForEitherConnection()
  {
    await using var h = NewHarness();
    await h.ConnectAsync("cast-a", reportedLevel: 0.40f);
    var target = h.Target();

    h.VolumeGate = CastConsoleTestHarness.NewTcs();
    var burst = h.Output.SetDeviceVolumeFromConsoleAsync(0.30f, target.Generation);
    await h.VolumeSendEntered.Task;

    await h.Output.DisconnectAsync();
    await h.ConnectAsync("cast-b", reportedLevel: 0.80f);
    h.VolumeGate.SetResult();
    await burst;

    Assert.Equal(0.80f, h.Output.KnownSpeakerLevel, 3);
    Assert.DoesNotContain(h.Store.Remembered, r => Math.Abs(r.Volume - 0.30f) < 0.001f);
  }

  // Pre-merge review L3: the echo window ran from a send's START, and the sends it covers are
  // bounded at up to 10 s, so a slow send's confirmation could arrive after its entry expired.
  //
  // Hostile re-review M1: the drain's ConsoleCommandTimeout (5 s) runs on this same fake clock
  // (F9), so the in-flight advance below must stay UNDER it. At 8 s the timeout could fire — or
  // not, depending on whether the drain had armed it before the Advance — and an abandoned push's
  // completion could then land after the anti-vacuity advance. 4 s is past EchoWindow (3 s), which
  // is what the test needs, and short of the timeout, so the timeout can never fire here.
  [Fact]
  public async Task TheConfirmationOfASlowPush_IsAnEcho_ForAsLongAsTheSendIsInFlight_AndTheWindowAfter()
  {
    await using var h = NewHarness();
    await h.ConnectAsync(reportedLevel: 0.40f);
    var target = h.Target();

    h.VolumeGate = CastConsoleTestHarness.NewTcs();
    var burst = h.Output.SetDeviceVolumeFromConsoleAsync(0.30f, target.Generation);
    await h.VolumeSendEntered.Task;

    h.RaiseStatus(0.55);                      // a speaker change moves the baseline off 0.30
    h.Time.Advance(TimeSpan.FromSeconds(4));  // in flight 4 s: past EchoWindow, short of the 5 s timeout
    h.RaiseStatus(0.30);                      // the device confirms our push
    Assert.Single(h.External);

    h.VolumeGate.SetResult();
    await burst;

    h.Time.Advance(TimeSpan.FromSeconds(2));  // within EchoWindow of the send's COMPLETION
    h.RaiseStatus(0.55);
    h.RaiseStatus(0.30);
    Assert.Equal(new[] { 0.55f, 0.55f }, h.External.Select(e => e.Volume).ToArray());

    // Anti-vacuity: once the window after completion has passed, 0.30 is a real change.
    h.Time.Advance(TimeSpan.FromSeconds(4));
    h.RaiseStatus(0.55);
    h.RaiseStatus(0.30);
    Assert.Equal(0.30f, h.External[^1].Volume, 3);
  }

  // Hostile review F9. The console command timeout runs on the injected clock. The timeout is
  // armed only after the send has started, so the test waits for its timer to exist on the fake
  // clock (a rendezvous, not a sleep) before advancing past it; the held send then times out
  // deterministically. On the system clock (the bug) no such timer is ever created on the fake
  // clock, and the safety-net bound below fails the test.
  [Fact]
  public async Task AConsolePush_TimesOutOnTheInjectedClock()
  {
    await using var h = NewHarness();
    await h.ConnectAsync(reportedLevel: 0.40f);
    var target = h.Target();

    var timeoutArmed = h.Time.WatchForTimer(TimeSpan.FromSeconds(5)); // ConsoleCommandTimeout
    h.VolumeGate = CastConsoleTestHarness.NewTcs();
    var burst = h.Output.SetDeviceVolumeFromConsoleAsync(0.30f, target.Generation);
    await h.VolumeSendEntered.Task;
    await timeoutArmed.WaitAsync(TimeSpan.FromSeconds(30)); // safety net only; never the gate

    h.Time.Advance(TimeSpan.FromSeconds(6)); // past the 5 s ConsoleCommandTimeout
    h.VolumeGate.SetResult();
    var result = await burst;

    Assert.True(result.Failed);
    Assert.Null(result.AppliedLevel);
  }

  // Hostile re-review M1 (optional half): the two teardown bounds run on the injected clock too.
  // Same rendezvous as above — wait for the timer to exist on the fake clock, then advance past
  // it. On the system clock no such timer is created and the safety-net bound fails the test.
  [Fact]
  public async Task ATeardownReceiverApplicationStop_TimesOutOnTheInjectedClock_AndLeavesTheSpeakerMuted()
  {
    await using var h = NewHarness();
    await h.ConnectAsync(reportedLevel: 0.40f);
    var target = h.Target();
    h.ClearCommands();
    Assert.True(await h.Output.SetDeviceMuteFromConsoleAsync(true, target.Generation));

    var hang = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    h.AppStop = () => hang.Task; // the device never answers the stop
    var timeoutArmed = h.Time.WatchForTimer(TimeSpan.FromSeconds(3)); // TeardownAppStopTimeout
    var stop = h.Output.StopAsync();
    await timeoutArmed.WaitAsync(TimeSpan.FromSeconds(30)); // safety net only; never the gate

    h.Time.Advance(TimeSpan.FromSeconds(4));
    await stop.WaitAsync(TimeSpan.FromSeconds(30));

    Assert.Equal(new[] { "mute", "appstop" }, h.Kinds());
    Assert.True(h.Output.IsSpeakerMutedByConsole); // an unconfirmed stop keeps the mark
    hang.SetResult(true);
  }

  [Fact]
  public async Task ATeardownUnmute_TimesOutOnTheInjectedClock()
  {
    await using var h = NewHarness();
    await h.ConnectAsync(reportedLevel: 0.40f);
    var target = h.Target();
    h.ClearCommands();
    Assert.True(await h.Output.SetDeviceMuteFromConsoleAsync(true, target.Generation));

    h.MuteGate = CastConsoleTestHarness.NewTcs(); // holds the teardown unmute in flight
    var timeoutArmed = h.Time.WatchForTimer(TimeSpan.FromSeconds(2)); // TeardownUnmuteTimeout
    var stop = h.Output.StopAsync();
    await timeoutArmed.WaitAsync(TimeSpan.FromSeconds(30)); // safety net only; never the gate

    h.Time.Advance(TimeSpan.FromSeconds(3));
    await stop.WaitAsync(TimeSpan.FromSeconds(30)); // returns while the unmute is still held

    // Since the AUD-81 follow-up (D2) a timed-out teardown unmute is retried over a fresh
    // connection (the harness's default: speaker muted, nothing running).
    Assert.Equal(new[] { "mute", "appstop", "unmute", "fresh", "fresh-unmute" }, h.Kinds());
    h.MuteGate.SetResult();
  }

  // --- AUD-81 follow-up D2: the teardown unmute when stopping our app closes the connection ---

  /// <summary>
  /// Casting to cast-a with the console muted (the speaker muted for it), and a console logger
  /// attached. Returns the logger; commands cleared.
  /// </summary>
  private static async Task<RecordingLogger> CastingUnderAMutedConsoleAsync(CastConsoleTestHarness h)
  {
    var log = new RecordingLogger();
    h.Output.AttachConsoleFollower(() => true, log);
    await h.ConnectAsync(reportedLevel: 0.30f);
    Assert.True(await h.Output.SetDeviceMuteFromConsoleAsync(true, h.Target().Generation));
    Assert.True(h.Output.IsSpeakerMutedByConsole);
    h.ClearCommands();
    return log;
  }

  /// <summary>
  /// Whether the output still records cast-a as muted for the console: a reconnect whose initial
  /// read shows it muted re-arms the mark only then (F11).
  /// </summary>
  private static async Task<bool> StillRecordedAsMutedForTheConsoleAsync(CastConsoleTestHarness h)
  {
    await h.ConnectAsync(reportedLevel: 0.30f, reportedMuted: true, streaming: false);
    return h.Output.IsSpeakerMutedByConsole;
  }

  // D2 (a). Measured on the box 2026-10-01: stopping our receiver application made the speaker
  // close our connection, and the unmute sent after it timed out — the speaker could stay muted.
  [Fact]
  public async Task WhenTheAppStopClosesTheConnection_TheUnmuteIsSentOverAFreshConnection()
  {
    await using var h = NewHarness();
    var log = await CastingUnderAMutedConsoleAsync(h);
    h.FailNextMute = new TaskCanceledException("Client disconnected before receiving response.");

    await h.Output.StopAsync();

    Assert.Equal(new[] { "appstop", "unmute", "fresh", "fresh-unmute" }, h.Kinds());
    Assert.False(h.Output.IsSpeakerMutedByConsole);
    Assert.Single(log.Lines(), l => l.Level == LogLevel.Information && l.Line.Contains("unmuted over a new connection"));
    Assert.DoesNotContain(log.Lines(), l => l.Line.Contains("could not unmute"));
    Assert.False(await StillRecordedAsMutedForTheConsoleAsync(h)); // the record was cleared
  }

  // D2 (a'): a fresh status that already shows the speaker unmuted sends nothing and clears the record.
  [Fact]
  public async Task AFreshConnectionThatFindsTheSpeakerUnmuted_SendsNothing_AndClearsTheRecord()
  {
    await using var h = NewHarness();
    var log = await CastingUnderAMutedConsoleAsync(h);
    h.FailNextMute = new TimeoutException("closed");
    h.FreshConnect = () => Task.FromResult<Sharpcaster.Models.ChromecastStatus.ChromecastStatus?>(
      CastConsoleTestHarness.FreshStatus(muted: false));

    await h.Output.StopAsync();

    Assert.Equal(new[] { "appstop", "unmute", "fresh" }, h.Kinds());
    Assert.Single(log.Lines(), l => l.Level == LogLevel.Information && l.Line.Contains("already unmuted after closing"));
    Assert.False(await StillRecordedAsMutedForTheConsoleAsync(h));
  }

  // D2 (b). The fresh connection still shows OUR receiver application running: unmuting could
  // release its audio under a muted console (H1), so the speaker is left muted, and said so.
  [Fact]
  public async Task AFreshConnectionThatStillShowsOurApplication_DoesNotUnmute()
  {
    await using var h = NewHarness();
    var log = await CastingUnderAMutedConsoleAsync(h);
    h.FailNextMute = new TimeoutException("closed");
    h.FreshConnect = () => Task.FromResult<Sharpcaster.Models.ChromecastStatus.ChromecastStatus?>(
      CastConsoleTestHarness.FreshStatus(muted: true, runningAppId: h.Output.Options.ApplicationId));

    await h.Output.StopAsync();

    Assert.Equal(new[] { "appstop", "unmute", "fresh" }, h.Kinds());
    Assert.Single(log.Lines(), l => l.Level == LogLevel.Information && l.Line.Contains("still shows our receiver application running"));
    Assert.True(await StillRecordedAsMutedForTheConsoleAsync(h)); // kept: still the console's mute
  }

  // D2 (c). The speaker cannot be reached at all: bounded at 5 s on the injected clock, logged,
  // and the speaker left muted (the record kept for a later connection).
  [Fact]
  public async Task AnUnreachableSpeaker_IsGivenUpOnTheInjectedClock_AndLeftMuted()
  {
    await using var h = NewHarness();
    var log = await CastingUnderAMutedConsoleAsync(h);
    h.FailNextMute = new TimeoutException("closed");
    var never = new TaskCompletionSource<Sharpcaster.Models.ChromecastStatus.ChromecastStatus?>(
      TaskCreationOptions.RunContinuationsAsynchronously);
    h.FreshConnect = () => never.Task;

    var timeoutArmed = h.Time.WatchForTimer(TimeSpan.FromSeconds(5)); // FreshConnectionUnmuteTimeout
    var stop = h.Output.StopAsync();
    await timeoutArmed.WaitAsync(TimeSpan.FromSeconds(30)); // safety net only; never the gate
    h.Time.Advance(TimeSpan.FromSeconds(6));
    await stop.WaitAsync(TimeSpan.FromSeconds(30));

    Assert.Equal(new[] { "appstop", "unmute", "fresh" }, h.Kinds());
    Assert.Single(log.Lines(), l => l.Level == LogLevel.Information && l.Line.Contains("could not be reached over a new connection"));
    Assert.True(await StillRecordedAsMutedForTheConsoleAsync(h));
    never.SetResult(null);
  }

  // D2 (d). A connection LOSS never unmutes — not on the old connection, and not over a new one.
  [Fact]
  public async Task ALostConnection_NeverTriesAnUnmuteOverAFreshConnection()
  {
    await using var h = NewHarness();
    await CastingUnderAMutedConsoleAsync(h);

    h.Output.ReportConnectionLost(h.Target().Generation, "test", null);
    await h.Output.LastConnectionLossHandling;
    await h.Output.StopAsync();
    await h.Output.DisconnectAsync();

    Assert.Empty(h.Commands);
  }

  // Pre-merge review L4: console mutes were not coalesced, so a burst of toggles queued one
  // SET_MUTE each and the "muted by console" mark followed whichever finished last.
  [Fact]
  public async Task ConsoleMutes_AreCoalesced_LatestWins_AndTheMarkFollowsTheLastSent()
  {
    await using var h = NewHarness();
    await h.ConnectAsync(reportedLevel: 0.40f);
    var target = h.Target();
    h.ClearCommands();

    h.MuteGate = CastConsoleTestHarness.NewTcs();
    var first = h.Output.SetDeviceMuteFromConsoleAsync(true, target.Generation);
    await h.MuteSendEntered.Task; // true in flight
    var second = h.Output.SetDeviceMuteFromConsoleAsync(false, target.Generation);
    var third = h.Output.SetDeviceMuteFromConsoleAsync(true, target.Generation);
    var fourth = h.Output.SetDeviceMuteFromConsoleAsync(false, target.Generation);
    h.MuteGate.SetResult();

    Assert.False(await first);  // a later request won the burst
    Assert.False(await third);
    Assert.True(await second);
    Assert.True(await fourth);
    Assert.Equal(new[] { true, false }, h.MuteSends());
    Assert.False(h.Output.IsSpeakerMutedByConsole);
    Assert.False(h.Output.KnownSpeakerMuted);
  }

  // Pre-merge review L5: SharpCaster raises the live read's GET_STATUS response as a
  // ReceiverStatusChanged too, which reached OnReceiverStatusChanged as an EXTERNAL change — a
  // diagnostic read could move master volume.
  [Fact]
  public async Task AStatusArrivingDuringTheLiveRead_IsBaselineOnly_NeverAnExternalChange()
  {
    await using var h = NewHarness();
    await h.ConnectAsync(reportedLevel: 0.40f);

    h.Output.CastStatusReadOverrideForTests = () =>
    {
      h.RaiseStatus(0.55, muted: true);
      return Task.FromResult<(float, bool)?>((0.55f, true));
    };
    var reading = await h.Output.ReadSpeakerVolumeAsync();

    Assert.NotNull(reading);
    Assert.Empty(h.External);

    h.RaiseStatus(0.55, muted: true); // the baseline holds it
    Assert.Empty(h.External);

    h.RaiseStatus(0.20, muted: true); // anti-vacuity: after the read, a change is reported
    Assert.Equal(0.20f, Assert.Single(h.External).Volume, 3);
  }

  // Pre-merge review M1: every reported change makes the speaker's report the known level and
  // drops queued console targets — a mute-only one too — and says whether the LEVEL changed.
  [Fact]
  public async Task AMuteOnlyExternalChange_DropsTheQueuedTarget_AndIsMarkedAsNotALevelChange()
  {
    await using var h = NewHarness();
    await h.ConnectAsync(reportedLevel: 0.40f);
    var target = h.Target();
    h.ClearCommands();

    h.VolumeGate = CastConsoleTestHarness.NewTcs();
    var burst = h.Output.SetDeviceVolumeFromConsoleAsync(0.30f, target.Generation);
    await h.VolumeSendEntered.Task;                                   // 0.30 in flight
    Assert.Same(burst, h.Output.SetDeviceVolumeFromConsoleAsync(0.36f, target.Generation)); // 0.36 queued

    // Muted on the speaker, which reports the level of our in-flight push beside it.
    h.RaiseStatus(0.30, muted: true);

    var e = Assert.Single(h.External);
    Assert.False(e.VolumeChanged);
    Assert.True(e.IsMuted);
    Assert.Equal(0.30f, h.Output.KnownSpeakerLevel, 3); // the speaker's report, not the queued 0.36

    h.VolumeGate.SetResult();
    await burst;
    Assert.Equal(new[] { 0.30f }, h.VolumeSends()); // the queued 0.36 was dropped
  }
}

/// <summary>
/// Every <see cref="GoogleCastOutputConsoleVolumeTests"/> test again, on the device model measured on
/// the box (AUD-81 follow-up): a SET_VOLUME that changes the level unmutes a muted speaker, and the
/// reply status reaches the output before the send completes.
/// </summary>
public class GoogleCastOutputConsoleVolumeTests_OnTheDeviceModel : GoogleCastOutputConsoleVolumeTests
{
  protected override bool OnTheDeviceModel => true;
}
