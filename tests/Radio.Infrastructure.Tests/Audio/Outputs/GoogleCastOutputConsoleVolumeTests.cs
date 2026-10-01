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
}
