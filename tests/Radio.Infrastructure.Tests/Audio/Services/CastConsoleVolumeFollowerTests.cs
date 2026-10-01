using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Radio.Core.Interfaces.Audio;
using Radio.Infrastructure.Audio.Outputs;
using Radio.Infrastructure.Audio.Services;
using Radio.Infrastructure.Audio.SoundFlow;
using Radio.Infrastructure.Tests.Audio.Outputs;
using Xunit;

namespace Radio.Infrastructure.Tests.Audio.Services;

/// <summary>
/// AUD-81: the console's master volume and mute drive a connected Cast speaker — only while Cast
/// is the active output and streaming, without jumps, and without an echo loop.
/// </summary>
/// <remarks>
/// A real <see cref="SoundFlowMasterMixer"/> and a real <see cref="GoogleCastOutput"/> (through
/// <see cref="CastConsoleTestHarness"/>). <see cref="ResyncLikeAudioStateUpdateService"/> stands in
/// for <c>AudioStateUpdateService.OnCastVolumeChanged</c>, which lives in Radio.API: on a non-initial
/// event it writes master volume and mute from the speaker. No assertion races a clock — every
/// burst is awaited through <c>LastVolumeBurst</c>, in-flight pushes are held by a gate, and the
/// echo window runs on <c>FakeTimeProvider</c>.
/// </remarks>
public class CastConsoleVolumeFollowerTests
{
  private string? _activeOutput = "google-cast";
  private readonly Mock<IAudioEngine> _engine = new();

  private (SoundFlowMasterMixer Mixer, CastConsoleVolumeFollower Follower) Build(
    CastConsoleTestHarness h, float initialMaster)
  {
    var mixer = new SoundFlowMasterMixer(NullLogger<SoundFlowMasterMixer>.Instance) { MasterVolume = initialMaster };
    _engine.SetupGet(e => e.ActiveOutputId).Returns(() => _activeOutput);
    var follower = new CastConsoleVolumeFollower(
      NullLogger<CastConsoleVolumeFollower>.Instance, mixer, _engine.Object, h.Output);
    return (mixer, follower);
  }

  /// <summary>What the output gate does after a switch: record it, then raise the event.</summary>
  private void SwitchActiveOutput(string outputId)
  {
    _activeOutput = outputId;
    _engine.Raise(e => e.ActiveOutputChanged += null, _engine.Object, outputId);
  }

  /// <summary>
  /// What AudioStateUpdateService.OnCastVolumeChanged does with an external speaker change: the
  /// level only when the speaker's level changed (and differs by more than 0.01), the mute always.
  /// Returns a counter of the master-volume writes it made.
  /// </summary>
  private static Func<int> ResyncLikeAudioStateUpdateService(GoogleCastOutput output, SoundFlowMasterMixer mixer)
  {
    var writes = 0;
    output.CastVolumeChanged += (_, e) =>
    {
      if (e.IsInitialSync)
      {
        return;
      }

      if (e.VolumeChanged && Math.Abs(mixer.MasterVolume - e.Volume) > 0.01f)
      {
        Interlocked.Increment(ref writes);
        mixer.MasterVolume = e.Volume;
      }

      mixer.IsMuted = e.IsMuted;
    };
    return () => Volatile.Read(ref writes);
  }

  [Fact]
  public async Task NothingIsPushed_UnlessCastIsTheActiveOutput_AndStreaming()
  {
    await using var h = new CastConsoleTestHarness();
    await h.ConnectAsync(reportedLevel: 0.50f, streaming: false);
    var (mixer, follower) = Build(h, 0.50f);
    using var _ = follower;
    h.ClearCommands();

    // Cast active but not streaming.
    mixer.MasterVolume = 0.70f;
    await follower.LastVolumeBurst;
    Assert.Empty(h.Commands);

    // Streaming but a local output is active (startup restore, switching, AUD-84 fallback, or
    // the gate not yet having recorded google-cast). The volume and an UNmute are not pushed.
    // (Pre-merge review M3 changed one thing here: a MUTE now is — the safe direction — so this
    // no longer asserts that nothing at all is sent; AMuteReachesAStreamingSpeaker… covers it.)
    h.MarkStreaming();
    _activeOutput = "out:Built-in Audio Analog Stereo";
    mixer.MasterVolume = 0.30f;
    mixer.IsMuted = true;
    await follower.LastMutePush;
    mixer.IsMuted = false;
    await follower.LastVolumeBurst;
    await follower.LastMutePush;
    Assert.Empty(h.VolumeSends());
    Assert.Equal(new[] { true }, h.MuteSends());

    // Both: the console now drives the speaker.
    _activeOutput = "google-cast";
    mixer.MasterVolume = 0.40f;
    await follower.LastVolumeBurst;
    Assert.Single(h.VolumeSends());
  }

  [Fact]
  public async Task TheFirstMove_ContinuesFromTheSpeakersLevel_NotTheConsoles()
  {
    await using var h = new CastConsoleTestHarness();
    await h.ConnectAsync(reportedLevel: 0.40f); // speaker at 40 %
    var (mixer, follower) = Build(h, 0.50f);   // console at 50 %
    using var _ = follower;
    h.ClearCommands();

    mixer.MasterVolume = 0.45f;
    await follower.LastVolumeBurst;

    // Anchor (0.50, 0.40): 0.40 * 0.45 / 0.50 = 0.36 — one step down, not a jump to 45 %.
    Assert.Equal(0.36f, Assert.Single(h.VolumeSends()), 3);
    Assert.Equal(0.36f, h.Store.Volumes["cast-a"], 3);
  }

  [Fact]
  public async Task AMasterLevelEqualToTheSpeakers_PushesNothing_AndReanchorsToIdentity()
  {
    await using var h = new CastConsoleTestHarness();
    await h.ConnectAsync(reportedLevel: 0.40f);
    var (mixer, follower) = Build(h, 0.50f);
    using var _ = follower;
    h.ClearCommands();

    mixer.MasterVolume = 0.40f; // equal to the speaker's 40 %
    await follower.LastVolumeBurst;
    Assert.Empty(h.VolumeSends());

    // Re-anchored at (0.40, 0.40): the identity, not the original (0.50, 0.40) curve, whose
    // upper segment would give 0.40 + 0.60 * 0.10 / 0.50 = 0.52.
    mixer.MasterVolume = 0.60f;
    await follower.LastVolumeBurst;
    Assert.Equal(0.60f, Assert.Single(h.VolumeSends()), 3);
  }

  [Fact]
  public async Task OneEncoderDetent_FromTheIdentity_StillReachesTheSpeaker()
  {
    // VolumeStepPercent defaults to 1, so one detent is one point. A 0.01 "already there"
    // tolerance would swallow it; SameLevelTolerance must not.
    await using var h = new CastConsoleTestHarness();
    await h.ConnectAsync(reportedLevel: 0.50f);
    var (mixer, follower) = Build(h, 0.50f);
    using var _ = follower;
    h.ClearCommands();

    mixer.MasterVolume = 0.51f;
    await follower.LastVolumeBurst;
    mixer.MasterVolume = 0.52f;
    await follower.LastVolumeBurst;

    Assert.Equal(new[] { 0.51f, 0.52f }, h.VolumeSends());
  }

  [Fact]
  public async Task AnExternalSpeakerChange_ResyncsMaster_AndIsNotPushedBack()
  {
    await using var h = new CastConsoleTestHarness();
    await h.ConnectAsync(reportedLevel: 0.40f);
    var (mixer, follower) = Build(h, 0.50f);
    using var _ = follower;
    var masterWrites = ResyncLikeAudioStateUpdateService(h.Output, mixer);
    h.ClearCommands();

    h.RaiseStatus(0.55); // changed in Google Home
    await follower.LastVolumeBurst;

    Assert.Equal(0.55f, mixer.MasterVolume, 3);
    Assert.Equal(1, masterWrites());
    Assert.Empty(h.VolumeSends()); // no echo back to the speaker

    // And the curve is now the identity from there.
    mixer.MasterVolume = 0.65f;
    await follower.LastVolumeBurst;
    Assert.Equal(0.65f, Assert.Single(h.VolumeSends()), 3);
    Assert.Equal(1, masterWrites());
  }

  [Fact]
  public async Task ADraggedSlider_WithLateOutOfOrderConfirmations_NeverRewritesMaster_OrLoops()
  {
    await using var h = new CastConsoleTestHarness();
    await h.ConnectAsync(reportedLevel: 0.50f);
    var (mixer, follower) = Build(h, 0.50f); // identity: speaker and console both at 50 %
    using var _ = follower;
    var masterWrites = ResyncLikeAudioStateUpdateService(h.Output, mixer);
    h.ClearCommands();
    var rememberedBefore = h.Store.Remembered.Count;

    // First tick goes out and is held in flight.
    h.VolumeGate = CastConsoleTestHarness.NewTcs();
    mixer.MasterVolume = 0.52f;
    await h.VolumeSendEntered.Task;

    // The drag continues while it is in flight; each tick replaces the pending target.
    mixer.MasterVolume = 0.54f;
    mixer.MasterVolume = 0.56f;
    mixer.MasterVolume = 0.58f;

    h.VolumeGate.SetResult();
    await follower.LastVolumeBurst;

    var sends = h.VolumeSends();
    Assert.Equal(new[] { 0.52f, 0.58f }, sends); // coalesced: in-flight + latest

    // The device confirms both, late and in the wrong order, with an unchanged status between.
    h.Time.Advance(TimeSpan.FromMilliseconds(800));
    h.RaiseStatus(0.58);
    h.RaiseStatus(0.52);
    h.RaiseStatus(0.52);
    h.RaiseStatus(0.58);
    await follower.LastVolumeBurst;

    Assert.Empty(h.External);
    Assert.Equal(0, masterWrites());
    Assert.Equal(0.58f, mixer.MasterVolume, 3);
    Assert.Equal(2, h.VolumeSends().Count); // nothing re-sent by the confirmations

    // Remembered once, with the final level.
    Assert.Equal(new[] { ("cast-a", 0.58f) }, h.Store.Remembered.Skip(rememberedBefore).ToList());
  }

  [Fact]
  public async Task ANewConnection_ReanchorsToItsOwnSpeakerLevel()
  {
    await using var h = new CastConsoleTestHarness();
    await h.ConnectAsync("cast-a", reportedLevel: 0.40f);
    var (mixer, follower) = Build(h, 0.50f);
    using var _ = follower;

    mixer.MasterVolume = 0.45f; // anchored on A at (0.50, 0.40)
    await follower.LastVolumeBurst;

    await h.Output.DisconnectAsync();
    await h.ConnectAsync("cast-b", reportedLevel: 0.80f);
    h.ClearCommands();

    mixer.MasterVolume = 0.44f;
    await follower.LastVolumeBurst;

    // Anchored on B at (0.45, 0.80): 0.80 * 0.44 / 0.45, not A's curve (0.352).
    Assert.Equal(0.80f * 0.44f / 0.45f, Assert.Single(h.VolumeSends()), 3);
  }

  [Fact]
  public async Task TheConsoleMute_DrivesTheSpeaker_WhileCasting_AndAnExternalMuteIsNotPushedBack()
  {
    await using var h = new CastConsoleTestHarness();
    await h.ConnectAsync(reportedLevel: 0.40f);
    var (mixer, follower) = Build(h, 0.50f);
    using var _ = follower;
    ResyncLikeAudioStateUpdateService(h.Output, mixer);
    h.ClearCommands();

    mixer.IsMuted = true;
    await follower.LastMutePush;
    mixer.IsMuted = false;
    await follower.LastMutePush;
    Assert.Equal(new[] { true, false }, h.MuteSends());

    // Muted on the speaker itself: the console follows (the re-sync), and nothing goes back.
    h.Time.Advance(TimeSpan.FromSeconds(5)); // past the echo window of our own mutes
    h.RaiseStatus(0.40, muted: true);
    await follower.LastMutePush;
    Assert.True(mixer.IsMuted);
    Assert.Equal(new[] { true, false }, h.MuteSends());
  }

  [Fact]
  public async Task AMutedConsole_MutesTheSpeakerWhenStreamingStarts_ThroughTheFollowersHook()
  {
    await using var h = new CastConsoleTestHarness();
    await h.ConnectAsync(reportedLevel: 0.40f, streaming: false);
    var (mixer, follower) = Build(h, 0.50f);
    using var _ = follower;
    mixer.IsMuted = true; // muted before casting started: not pushed (not streaming yet)
    await follower.LastMutePush;
    h.ClearCommands();

    await h.Output.SyncVolumeAfterStartAsync();

    Assert.Equal(new[] { true }, h.MuteSends());
    Assert.Equal("mute", h.Commands[0].Kind);
  }

  // --- pre-merge review M1: loop safety from an explicit marker, not float equality ---

  // A console move landing between GoogleCastOutput clearing its queued target and raising
  // CastVolumeChanged makes KnownSpeakerLevel that new target, not the speaker's level — so the
  // re-synced master write no longer "equals the speaker", and before the marker the follower
  // mapped the speaker's own level through the curve and pushed it back.
  [Fact]
  public async Task TheMasterWriteOfASpeakerChange_IsNeverPushedBack_EvenWhenTheKnownLevelHasMovedOn()
  {
    await using var h = new CastConsoleTestHarness();
    await h.ConnectAsync(reportedLevel: 0.40f);
    var (mixer, follower) = Build(h, 0.50f); // anchor (0.50, 0.40): not the identity
    using var _ = follower;
    var target = h.Target();

    h.VolumeGate = CastConsoleTestHarness.NewTcs();
    var concurrent = Task.CompletedTask;
    var moved = false;
    h.Output.CastVolumeChanged += (_, e) =>
    {
      if (!e.IsInitialSync && !moved)
      {
        moved = true;
        concurrent = h.Output.SetDeviceVolumeFromConsoleAsync(0.33f, target.Generation);
      }
    };
    var masterWrites = ResyncLikeAudioStateUpdateService(h.Output, mixer);
    h.ClearCommands();

    h.RaiseStatus(0.55); // changed on the speaker

    Assert.Equal(1, masterWrites());
    Assert.Equal(0.55f, mixer.MasterVolume, 3);

    h.VolumeGate.SetResult();
    await concurrent;
    await follower.LastVolumeBurst;
    Assert.Equal(new[] { 0.33f }, h.VolumeSends()); // only the concurrent move; nothing mapped from 0.55
  }

  // A speaker-side mute while a non-identity burst is queued: the event's level is just what the
  // speaker reported beside the mute. Copying it into master moved the console's volume, and the
  // follower then mapped it through the anchor and pushed (a speaker-side mute halved the volume).
  [Fact]
  public async Task AMuteOnlySpeakerChange_AfterNonIdentityPushes_PushesNothing_AndLeavesMasterAlone()
  {
    await using var h = new CastConsoleTestHarness();
    await h.ConnectAsync(reportedLevel: 0.40f);
    var (mixer, follower) = Build(h, 0.50f);
    using var _ = follower;
    var masterWrites = ResyncLikeAudioStateUpdateService(h.Output, mixer);
    h.ClearCommands();

    h.VolumeGate = CastConsoleTestHarness.NewTcs();
    mixer.MasterVolume = 0.45f;                  // → 0.36, held in flight
    await h.VolumeSendEntered.Task;
    mixer.MasterVolume = 0.47f;                  // → 0.376, queued

    h.RaiseStatus(0.36, muted: true);            // muted on the speaker, reporting our in-flight level

    h.VolumeGate.SetResult();
    await follower.LastVolumeBurst;

    Assert.True(mixer.IsMuted);                  // the mute reached the console
    Assert.Equal(0.47f, mixer.MasterVolume, 3);  // its level did not
    Assert.Equal(0, masterWrites());
    Assert.Equal(0.36f, Assert.Single(h.VolumeSends()), 3); // the queued target dropped; nothing pushed back
    Assert.Empty(h.MuteSends());
  }

  // A device that quantises (reports 0.46 for a pushed 0.4567) confirms our push within the echo
  // filter's 0.01 but outside the follower's 0.001 same-level check. Reported beside a mute, that
  // used to become a master write that the follower mapped and pushed.
  [Fact]
  public async Task AQuantisedReportOfOurPush_BesideASpeakerMute_PushesNothing()
  {
    await using var h = new CastConsoleTestHarness();
    await h.ConnectAsync(reportedLevel: 0.40f);
    var (mixer, follower) = Build(h, 0.50f); // anchor (0.50, 0.40); upper slope 1.2
    using var _ = follower;
    ResyncLikeAudioStateUpdateService(h.Output, mixer);
    h.ClearCommands();

    mixer.MasterVolume = 0.5473f;            // → 0.4 + 1.2 * 0.0473 = 0.45676
    await follower.LastVolumeBurst;
    var pushed = Assert.Single(h.VolumeSends());
    Assert.Equal(0.4568f, pushed, 3);

    h.RaiseStatus(0.46, muted: true);        // the device's quantised confirmation, plus a mute

    await follower.LastVolumeBurst;
    await follower.LastMutePush;
    Assert.Single(h.VolumeSends());
    Assert.Equal(0.5473f, mixer.MasterVolume, 4);
    Assert.True(mixer.IsMuted);
  }

  [Fact]
  public async Task ASpeakerChangeTooSmallToMoveMaster_StillReanchorsTheCurve()
  {
    await using var h = new CastConsoleTestHarness();
    await h.ConnectAsync(reportedLevel: 0.40f);
    var (mixer, follower) = Build(h, 0.50f); // anchor would be (0.50, 0.40)
    using var _ = follower;
    var masterWrites = ResyncLikeAudioStateUpdateService(h.Output, mixer);
    h.ClearCommands();

    h.RaiseStatus(0.505); // within 0.01 of master: no master write
    Assert.Equal(0, masterWrites());

    mixer.MasterVolume = 0.51f;
    await follower.LastVolumeBurst;

    // From (0.50, 0.505): 0.505 + (0.495 / 0.5) * 0.01 = 0.5149 — not (0.50, 0.40)'s 0.412.
    Assert.Equal(0.5149f, Assert.Single(h.VolumeSends()), 3);
  }

  // --- pre-merge review M3: the start-up window ---

  [Fact]
  public async Task AMuteReachesAStreamingSpeaker_BeforeTheGateRecordsCast_ButAnUnmuteWaitsForIt()
  {
    await using var h = new CastConsoleTestHarness();
    _activeOutput = "out:Built-in Audio Analog Stereo"; // the gate records google-cast only later
    await h.ConnectAsync(reportedLevel: 0.40f);         // streaming
    var (mixer, follower) = Build(h, 0.50f);
    using var _ = follower;
    h.ClearCommands();

    mixer.IsMuted = true;
    await follower.LastMutePush;
    Assert.Equal(new[] { true }, h.MuteSends());
    Assert.True(h.Output.IsSpeakerMutedByConsole);

    mixer.IsMuted = false; // not pushed yet: Cast is not the active output
    await follower.LastMutePush;
    Assert.Equal(new[] { true }, h.MuteSends());

    // The gate makes Cast active: the speaker this application muted is unmuted to match.
    SwitchActiveOutput("google-cast");
    await follower.LastMutePush;
    Assert.Equal(new[] { true, false }, h.MuteSends());
    Assert.False(h.Output.IsSpeakerMutedByConsole);
  }

  [Fact]
  public async Task AConsoleMuteMadeBeforeStreaming_IsReconciledWhenTheGateMakesCastActive()
  {
    await using var h = new CastConsoleTestHarness();
    _activeOutput = "out:Built-in Audio Analog Stereo";
    await h.ConnectAsync(reportedLevel: 0.40f, streaming: false);
    var (mixer, follower) = Build(h, 0.50f);
    using var _ = follower;

    mixer.IsMuted = true; // not streaming yet: nothing to drive
    await follower.LastMutePush;
    h.ClearCommands();

    h.MarkStreaming();
    SwitchActiveOutput("google-cast");
    await follower.LastMutePush;

    Assert.Equal(new[] { true }, h.MuteSends());

    // A second activation with nothing to reconcile sends nothing.
    SwitchActiveOutput("google-cast");
    await follower.LastMutePush;
    Assert.Equal(new[] { true }, h.MuteSends());
  }

  [Fact]
  public async Task AfterDispose_MasterChangesAreIgnored()
  {
    await using var h = new CastConsoleTestHarness();
    await h.ConnectAsync(reportedLevel: 0.40f);
    var (mixer, follower) = Build(h, 0.50f);
    follower.Dispose();
    h.ClearCommands();

    mixer.MasterVolume = 0.9f;
    mixer.IsMuted = true;
    await follower.LastVolumeBurst;
    await follower.LastMutePush;

    Assert.Empty(h.Commands);
  }
}
