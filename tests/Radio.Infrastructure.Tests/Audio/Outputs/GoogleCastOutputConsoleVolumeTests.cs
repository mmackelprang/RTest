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
  [Fact]
  public async Task ABurstOfConsoleChanges_IsCoalesced_LatestWins_AndRememberedOnce()
  {
    await using var h = new CastConsoleTestHarness();
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
    await using var h = new CastConsoleTestHarness();
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

  // Hostile review F3. A speaker that quantises to 1/15 (≈ 0.0667) reports our 0.437 as 0.4667 —
  // 0.03 from it, and 0.033 from the 0.50 pushed after it. Neither is within 0.01 of a single
  // push; both lie between our pushes, which is what now makes them echoes.
  [Fact]
  public async Task ACoarselyQuantisedEcho_BetweenRecentPushes_IsAbsorbed_NotWrittenBack()
  {
    await using var h = new CastConsoleTestHarness();
    await h.ConnectAsync(reportedLevel: 0.40f);
    var target = h.Target();
    var rememberedBefore = h.Store.Remembered.Count;

    await h.Output.SetDeviceVolumeFromConsoleAsync(0.437f, target.Generation);
    await h.Output.SetDeviceVolumeFromConsoleAsync(0.50f, target.Generation);

    h.Time.Advance(TimeSpan.FromSeconds(1));
    h.RaiseStatus(7.0 / 15.0); // 0.4667: the device's rounding of 0.437
    h.RaiseStatus(0.50);       // the second push, confirmed
    h.RaiseStatus(0.44);
    h.RaiseStatus(0.43);

    Assert.Empty(h.External);
    Assert.DoesNotContain(
      h.Store.Remembered.Skip(rememberedBefore),
      r => Math.Abs(r.Volume - 7f / 15f) < 0.001f);
  }

  [Fact]
  public async Task AFineQuantisedEcho_OfASinglePush_IsAbsorbed()
  {
    // Guard, not a mutation target: within 0.01 of the one push, so recognised by both the old
    // single-push rule and the range rule.
    await using var h = new CastConsoleTestHarness();
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
    await using var h = new CastConsoleTestHarness();
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
    await using var h = new CastConsoleTestHarness();

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
    await using var h = new CastConsoleTestHarness();
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
    await using var h = new CastConsoleTestHarness();

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
    await using var h = new CastConsoleTestHarness();
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
    await using var h = new CastConsoleTestHarness();
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
    await using var h = new CastConsoleTestHarness();
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
    await using var h = new CastConsoleTestHarness();
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
    await using var h = new CastConsoleTestHarness();
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
    await using var h = new CastConsoleTestHarness();
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
    await using var h = new CastConsoleTestHarness();
    await h.ConnectAsync(reportedLevel: 0.40f);
    var target = h.Target();
    h.ClearCommands();
    var storeBefore = new Dictionary<string, float>(h.Store.Volumes);

    Assert.True(await h.Output.SetDeviceMuteFromConsoleAsync(true, target.Generation));
    Assert.True(h.Output.IsSpeakerMutedByConsole);
    Assert.True(h.Output.KnownSpeakerMuted);

    // The level still follows the slider while muted.
    await h.Output.SetDeviceVolumeFromConsoleAsync(0.25f, target.Generation);

    await h.Output.StopAsync();

    Assert.Equal(new[] { true, false }, h.MuteSends());
    Assert.Equal(new[] { 0.25f }, h.VolumeSends());
    Assert.False(h.Output.IsSpeakerMutedByConsole);

    // Mute is never written to the per-device store; only the level is.
    Assert.Equal(0.25f, h.Store.Volumes["cast-a"], 3);
    Assert.Equal(storeBefore.Keys, h.Store.Volumes.Keys);
  }

  [Fact]
  public async Task ADeliberateDisconnect_UnmutesASpeakerTheConsoleMuted()
  {
    await using var h = new CastConsoleTestHarness();
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
    await using var h = new CastConsoleTestHarness();
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
    await using var h = new CastConsoleTestHarness();
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
    await using var h = new CastConsoleTestHarness();
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
    await using var h = new CastConsoleTestHarness();
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
    await using var h = new CastConsoleTestHarness();
    await h.ConnectAsync(reportedLevel: 0.40f);
    var target = h.Target();
    h.ClearCommands();

    Assert.True(await h.Output.SetDeviceMuteFromConsoleAsync(true, target.Generation));
    await h.Output.DisconnectAsync();

    Assert.Equal(new[] { "mute", "appstop", "unmute" }, h.Kinds());
  }

  [Fact]
  public async Task AMutedConsole_MutesTheSpeakerBeforeTheAfterStartVolumeSync()
  {
    await using var h = new CastConsoleTestHarness();
    h.Output.AttachConsoleFollower(() => true, new Microsoft.Extensions.Logging.Abstractions.NullLogger<GoogleCastOutputConsoleVolumeTests>());
    await h.ConnectAsync(reportedLevel: 0.40f, reportedMuted: false);
    h.ClearCommands();

    await h.Output.SyncVolumeAfterStartAsync();

    Assert.Equal(("mute", 0f, true), h.Commands[0]);
    Assert.Equal("vol", h.Commands[1].Kind);
    Assert.True(h.Output.IsSpeakerMutedByConsole);
  }

  [Fact]
  public async Task StreamingStart_NeverUnmutesASpeaker_AndDoesNotClaimOneMutedOnItsOwnSide()
  {
    // Console not muted, speaker muted on its own side: left alone.
    await using (var h = new CastConsoleTestHarness())
    {
      h.Output.AttachConsoleFollower(() => false, new Microsoft.Extensions.Logging.Abstractions.NullLogger<GoogleCastOutputConsoleVolumeTests>());
      await h.ConnectAsync(reportedLevel: 0.40f, reportedMuted: true);
      h.ClearCommands();

      await h.Output.SyncVolumeAfterStartAsync();

      Assert.Empty(h.MuteSends());
      Assert.False(h.Output.IsSpeakerMutedByConsole);
    }

    // Console muted, speaker already muted: nothing sent, and not marked as ours to release.
    await using (var h = new CastConsoleTestHarness())
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
    await using var h = new CastConsoleTestHarness();
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
    await using var h = new CastConsoleTestHarness();
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
    await using var h = new CastConsoleTestHarness();
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
    await using var h = new CastConsoleTestHarness();
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
    await using var h = new CastConsoleTestHarness();
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
    await using var h = new CastConsoleTestHarness();
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
    await using var h = new CastConsoleTestHarness();
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
    await using var h = new CastConsoleTestHarness();
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
    await using var h = new CastConsoleTestHarness();
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
    await using var h = new CastConsoleTestHarness();
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

    Assert.Equal(new[] { "mute", "appstop", "unmute" }, h.Kinds());
    h.MuteGate.SetResult();
  }

  // Pre-merge review L4: console mutes were not coalesced, so a burst of toggles queued one
  // SET_MUTE each and the "muted by console" mark followed whichever finished last.
  [Fact]
  public async Task ConsoleMutes_AreCoalesced_LatestWins_AndTheMarkFollowsTheLastSent()
  {
    await using var h = new CastConsoleTestHarness();
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
    await using var h = new CastConsoleTestHarness();
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
    await using var h = new CastConsoleTestHarness();
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
