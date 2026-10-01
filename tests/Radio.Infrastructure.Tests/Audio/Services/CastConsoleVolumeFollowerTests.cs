using Microsoft.Extensions.Logging;
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

  /// <summary>
  /// Whether the harness models the measured device (a level change unmutes a muted speaker —
  /// <see cref="CastConsoleTestHarness.LevelCommandUnmutes"/>). Off here;
  /// <see cref="CastConsoleVolumeFollowerTests_OnTheDeviceModel"/> runs every test with it on.
  /// </summary>
  protected virtual bool OnTheDeviceModel => false;

  private CastConsoleTestHarness NewHarness() => new(OnTheDeviceModel);

  private (SoundFlowMasterMixer Mixer, CastConsoleVolumeFollower Follower) Build(
    CastConsoleTestHarness h, float initialMaster, ILogger<CastConsoleVolumeFollower>? logger = null)
  {
    var mixer = new SoundFlowMasterMixer(NullLogger<SoundFlowMasterMixer>.Instance) { MasterVolume = initialMaster };
    _engine.SetupGet(e => e.ActiveOutputId).Returns(() => _activeOutput);
    var follower = new CastConsoleVolumeFollower(
      logger ?? NullLogger<CastConsoleVolumeFollower>.Instance, mixer, _engine.Object, h.Output);
    return (mixer, follower);
  }

  /// <summary>Records every formatted log line, for asserting what the console log says.</summary>
  private sealed class ListLogger<T> : ILogger<T>
  {
    private readonly object _lock = new();
    private readonly List<string> _lines = new();

    public List<string> Lines()
    {
      lock (_lock)
      {
        return _lines.ToList();
      }
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
      LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
      lock (_lock)
      {
        _lines.Add(formatter(state, exception));
      }
    }
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
    await using var h = NewHarness();
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
    await using var h = NewHarness();
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

  // Hostile review F2. Anchor (0.10, 0.80): the lower segment's slope is 8. Moving down along it is
  // the safe direction; moving back up along it raised the speaker 8 points per one-point detent.
  [Fact]
  public async Task DownThenOneDetentUp_OnASteepLowerSegment_RaisesTheSpeakerAtMostThreeTimesTheStep()
  {
    await using var h = NewHarness();
    await h.ConnectAsync(reportedLevel: 0.80f); // speaker at 80 %
    var (mixer, follower) = Build(h, 0.10f);   // console at 10 %
    using var _ = follower;
    h.ClearCommands();

    mixer.MasterVolume = 0.05f;
    await follower.LastVolumeBurst;
    Assert.Equal(0.40f, Assert.Single(h.VolumeSends()), 3); // 0.80 * 0.05 / 0.10

    mixer.MasterVolume = 0.06f;
    await follower.LastVolumeBurst;
    var sends = h.VolumeSends();
    Assert.Equal(2, sends.Count);
    var rise = sends[1] - sends[0];
    Assert.True(rise > 0f, $"an upward console move must not lower the speaker here (rise {rise})");
    Assert.True(
      rise <= CastConsoleVolumeCurve.MaxUpperSlope * 0.01f + 0.0001f,
      $"one detent up raised the speaker {rise:F4}, more than {CastConsoleVolumeCurve.MaxUpperSlope} times the step");

    // The documented hysteresis: back at the original console level, the speaker is below where
    // it started on this connection.
    mixer.MasterVolume = 0.10f;
    await follower.LastVolumeBurst;
    Assert.True(h.VolumeSends()[^1] < 0.80f);

    // And console 0 is still silent.
    mixer.MasterVolume = 0f;
    await follower.LastVolumeBurst;
    Assert.Equal(0f, h.VolumeSends()[^1], 4);
  }

  [Fact]
  public async Task TheFirstMoveFromASteepAnchor_NeverJumps_InEitherDirection()
  {
    await using var h = NewHarness();
    await h.ConnectAsync(reportedLevel: 0.80f);
    var (mixer, follower) = Build(h, 0.10f);
    using var _ = follower;
    h.ClearCommands();

    // Up first: the upper segment from (0.10, 0.80), slope (1 - 0.80) / (1 - 0.10) ≈ 0.22.
    mixer.MasterVolume = 0.11f;
    await follower.LastVolumeBurst;
    var up = Assert.Single(h.VolumeSends());
    Assert.InRange(up - 0.80f, 0f, CastConsoleVolumeCurve.MaxUpperSlope * 0.01f + 0.0001f);
    Assert.Equal(0.80f + 0.2f / 0.9f * 0.01f, up, 4);
  }

  [Fact]
  public async Task OnALowerSegmentNoSteeperThanTheCap_UpAndDownRetraceTheSameCurve()
  {
    // Anchor (0.50, 0.40): the lower segment's slope is 0.8, under the cap, so it is not
    // re-anchored — going down and back up returns the speaker to exactly where it was.
    await using var h = NewHarness();
    await h.ConnectAsync(reportedLevel: 0.40f);
    var (mixer, follower) = Build(h, 0.50f);
    using var _ = follower;
    h.ClearCommands();

    mixer.MasterVolume = 0.30f;
    await follower.LastVolumeBurst;
    mixer.MasterVolume = 0.50f;
    await follower.LastVolumeBurst;

    var sends = h.VolumeSends();
    Assert.Equal(0.24f, sends[0], 3);
    Assert.Equal(0.40f, sends[^1], 3);
  }

  [Fact]
  public async Task AMasterLevelEqualToTheSpeakers_PushesNothing_AndReanchorsToIdentity()
  {
    await using var h = NewHarness();
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
    await using var h = NewHarness();
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
    await using var h = NewHarness();
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
    await using var h = NewHarness();
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
    await using var h = NewHarness();
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
    await using var h = NewHarness();
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

  // --- AUD-81 follow-up (box UAT 2026-10-01): a level change unmutes the speaker ---

  // D1 (a). Measured on the box: console muted, console volume 30 % → 45 %, and the speaker (a
  // Google Home Mini) came back unmuted under the muted console. No level may be sent while the
  // console is muted.
  [Fact]
  public async Task WhileTheConsoleIsMuted_VolumeMoves_SendNoVolumeCommand_AndTheMuteStays()
  {
    await using var h = NewHarness();
    await h.ConnectAsync(reportedLevel: 0.30f);
    var (mixer, follower) = Build(h, 0.30f); // identity
    using var _ = follower;
    mixer.IsMuted = true;
    await follower.LastMutePush;
    Assert.True(h.Output.IsSpeakerMutedByConsole);
    h.ClearCommands();
    var rememberedBefore = h.Store.Remembered.Count;

    mixer.MasterVolume = 0.40f;
    await follower.LastVolumeBurst;
    mixer.MasterVolume = 0.45f;
    await follower.LastVolumeBurst;

    Assert.Empty(h.Commands);                       // no SET_VOLUME, and no SET_MUTE either
    Assert.True(h.Output.KnownSpeakerMuted);
    Assert.True(h.Output.IsSpeakerMutedByConsole);
    Assert.Equal(0.45f, h.Output.HeldConsoleVolume(h.Target().Generation)!.Value, 3); // the latest wins
    Assert.Equal(0.30f, h.Output.KnownSpeakerLevel, 3); // the speaker does not hold it yet
    Assert.Equal(rememberedBefore, h.Store.Remembered.Count); // not remembered while held
  }

  // D1 (b). On the console's unmute the held level goes FIRST, then the unmute.
  [Fact]
  public async Task TheConsoleUnmute_SendsTheLastHeldLevelFirst_ThenTheUnmute_AndRemembersItOnce()
  {
    await using var h = NewHarness();
    await h.ConnectAsync(reportedLevel: 0.30f);
    var log = new ListLogger<CastConsoleVolumeFollower>();
    var (mixer, follower) = Build(h, 0.30f, log);
    using var _ = follower;
    mixer.IsMuted = true;
    await follower.LastMutePush;
    mixer.MasterVolume = 0.40f;
    await follower.LastVolumeBurst;
    mixer.MasterVolume = 0.45f;
    await follower.LastVolumeBurst;
    h.ClearCommands();
    var rememberedBefore = h.Store.Remembered.Count;

    mixer.IsMuted = false;
    await follower.LastMutePush;

    Assert.Equal(new[] { "vol", "unmute" }, h.Kinds());
    Assert.Equal(0.45f, Assert.Single(h.VolumeSends()), 3);
    Assert.False(h.Output.IsSpeakerMutedByConsole);
    Assert.Null(h.Output.HeldConsoleVolume(h.Target().Generation));
    Assert.Equal(new[] { ("cast-a", 0.45f) }, h.Store.Remembered.Skip(rememberedBefore).ToList());
    var line = Assert.Single(log.Lines(), l => l.StartsWith("Cast: console unmuted", StringComparison.Ordinal));
    Assert.StartsWith("Cast: console unmuted → speaker Speaker cast-a at 45", line);
    Assert.EndsWith(", unmuted", line);

    // Hostile review M1: on the device model the held level unmutes the speaker and its reply
    // arrives before the level's send completes. That reply is our own command's echo — never a
    // change made on the speaker, so nothing is reported or logged as one.
    Assert.Empty(h.External);
    Assert.DoesNotContain(h.OutputLog.Lines(), l => l.Line.Contains("changed externally"));
    Assert.False(h.DeviceMuted);
  }

  // The console moves back to the speaker's own level while muted: nothing is sent for that, so an
  // older held level must not be applied at the unmute.
  [Fact]
  public async Task AHeldLevel_IsDropped_WhenTheConsoleComesBackToTheSpeakersLevel()
  {
    await using var h = NewHarness();
    await h.ConnectAsync(reportedLevel: 0.30f);
    var (mixer, follower) = Build(h, 0.30f);
    using var _ = follower;
    mixer.IsMuted = true;
    await follower.LastMutePush;
    mixer.MasterVolume = 0.45f;
    await follower.LastVolumeBurst;
    mixer.MasterVolume = 0.30f; // the speaker's own level
    await follower.LastVolumeBurst;
    h.ClearCommands();

    mixer.IsMuted = false;
    await follower.LastMutePush;

    Assert.Equal(new[] { "unmute" }, h.Kinds());
  }

  [Fact]
  public async Task AMutedConsole_MutesTheSpeakerWhenStreamingStarts_ThroughTheFollowersHook()
  {
    await using var h = NewHarness();
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
    await using var h = NewHarness();
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
    await using var h = NewHarness();
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
    await using var h = NewHarness();
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
    await using var h = NewHarness();
    await h.ConnectAsync(reportedLevel: 0.40f);
    var (mixer, follower) = Build(h, 0.50f);
    using var _ = follower;
    var masterWrites = ResyncLikeAudioStateUpdateService(h.Output, mixer);
    h.ClearCommands();

    // A first move establishes the non-identity anchor (0.50, 0.40): 0.45 → 0.36.
    mixer.MasterVolume = 0.45f;
    await follower.LastVolumeBurst;
    Assert.Equal(0.36f, Assert.Single(h.VolumeSends()), 3);
    h.ClearCommands();

    h.Time.Advance(TimeSpan.FromSeconds(5)); // past the echo window of that push
    h.RaiseStatus(0.455); // set on the speaker, within 0.01 of master: no master write
    Assert.Equal(0, masterWrites());

    mixer.MasterVolume = 0.46f;
    await follower.LastVolumeBurst;

    // From (0.45, 0.455): 0.455 + (0.545 / 0.55) * 0.01 = 0.4649 — not the stale (0.50, 0.40)
    // curve's 0.368, which would drop the speaker nine points on a one-point console move.
    Assert.Equal(0.4649f, Assert.Single(h.VolumeSends()), 3);
  }

  // --- pre-merge review M3: the start-up window ---

  [Fact]
  public async Task AMuteReachesAStreamingSpeaker_BeforeTheGateRecordsCast_ButAnUnmuteWaitsForIt()
  {
    await using var h = NewHarness();
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
    await using var h = NewHarness();
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

  // --- hostile review F11: a console mute orphaned by a lost connection ---

  /// <summary>
  /// Casting to cast-a, the console mutes it; then the connection is LOST (the speaker stays
  /// muted, by design) and the gate falls back to a local output. Returns with the console still
  /// muted; commands cleared.
  /// </summary>
  private async Task MuteThenLoseAsync(CastConsoleTestHarness h, SoundFlowMasterMixer mixer, CastConsoleVolumeFollower follower)
  {
    mixer.IsMuted = true;
    await follower.LastMutePush;
    Assert.Equal(new[] { true }, h.MuteSends());
    Assert.True(h.Output.IsSpeakerMutedByConsole);

    h.Output.ReportConnectionLost(h.Target().Generation, "test", null);
    await h.Output.LastConnectionLossHandling;
    _activeOutput = "out:Built-in Audio Analog Stereo";
    Assert.False(h.Output.IsSpeakerMutedByConsole);
    h.ClearCommands();
  }

  /// <summary>
  /// What StartAsync and the output gate do once a connection is up: the start-time mute, reaching
  /// Streaming (and its mute re-check), then the gate making Cast the active output.
  /// </summary>
  private async Task StartCastingAsync(CastConsoleTestHarness h, CastConsoleVolumeFollower follower)
  {
    await h.Output.SyncVolumeAfterStartAsync();
    h.MarkStreaming();
    await h.Output.OnReachedStreamingAsync();
    SwitchActiveOutput("google-cast");
    await follower.LastMutePush;
  }

  [Fact]
  public async Task AConsoleMutedSpeaker_LostThenReconnected_IsUnmutedWhenTheConsoleWasUnmutedMeanwhile()
  {
    await using var h = NewHarness();
    await h.ConnectAsync("cast-a", reportedLevel: 0.40f);
    var log = new ListLogger<CastConsoleVolumeFollower>();
    var (mixer, follower) = Build(h, 0.50f, log);
    using var _ = follower;
    ResyncLikeAudioStateUpdateService(h.Output, mixer);
    await MuteThenLoseAsync(h, mixer, follower);

    mixer.IsMuted = false; // unmuted on the local output: there is no speaker to send it to
    await follower.LastMutePush;
    Assert.Empty(h.MuteSends());

    // The same speaker again, still muted. The device's answer also arrives as a status event
    // during the read (F1): it must stay a baseline, never a mute synced into the console.
    await h.ConnectAsync("cast-a", streaming: false, statusRead: () =>
    {
      h.RaiseStatus(0.40, muted: true);
      return Task.FromResult<(float, bool)?>((0.40f, true));
    });
    Assert.Empty(h.External);
    Assert.False(mixer.IsMuted);
    Assert.True(h.Output.IsSpeakerMutedByConsole); // re-armed for the new connection

    await StartCastingAsync(h, follower);

    Assert.Equal(new[] { false }, h.MuteSends());
    Assert.False(h.Output.IsSpeakerMutedByConsole);
    Assert.False(mixer.IsMuted);
    Assert.Contains("Cast: console unmuted → speaker Speaker cast-a unmuted", log.Lines());
  }

  [Fact]
  public async Task AConsoleMutedSpeaker_LostThenReconnected_UnderAStillMutedConsole_StaysMuted_AndATeardownReleasesIt()
  {
    await using var h = NewHarness();
    await h.ConnectAsync("cast-a", reportedLevel: 0.40f);
    var (mixer, follower) = Build(h, 0.50f);
    using var _ = follower;
    ResyncLikeAudioStateUpdateService(h.Output, mixer);
    await MuteThenLoseAsync(h, mixer, follower);

    await h.ConnectAsync("cast-a", reportedLevel: 0.40f, reportedMuted: true, streaming: false);
    await StartCastingAsync(h, follower);

    Assert.Empty(h.MuteSends());                    // already muted: nothing to send
    Assert.True(h.Output.IsSpeakerMutedByConsole);  // but known to be the console's mute
    Assert.True(mixer.IsMuted);
    h.ClearCommands();

    // A deliberate teardown releases it like any console mute (H1): application stop, then unmute.
    await h.Output.StopAsync();
    Assert.Equal(new[] { "appstop", "unmute" }, h.Kinds());
    Assert.False(h.Output.IsSpeakerMutedByConsole);
  }

  [Fact]
  public async Task AReconnectThatFindsTheSpeakerUnmuted_SendsNothing_AndForgetsTheConsoleMute()
  {
    await using var h = NewHarness();
    await h.ConnectAsync("cast-a", reportedLevel: 0.40f);
    var (mixer, follower) = Build(h, 0.50f);
    using var _ = follower;
    ResyncLikeAudioStateUpdateService(h.Output, mixer);
    await MuteThenLoseAsync(h, mixer, follower);
    mixer.IsMuted = false;
    await follower.LastMutePush;

    // Someone unmuted it on the speaker while it was not connected.
    await h.ConnectAsync("cast-a", reportedLevel: 0.40f, reportedMuted: false, streaming: false);
    await StartCastingAsync(h, follower);
    Assert.Empty(h.MuteSends());
    Assert.False(h.Output.IsSpeakerMutedByConsole);

    // Forgotten: lost again, and found muted next time (now by someone else) — not ours to unmute.
    // This half is a GUARD, not a mutation target: it passes without F11 too (with no recall at
    // all nothing re-arms the mark), and pins that the recall, once cleared, stays cleared.
    h.Output.ReportConnectionLost(h.Target().Generation, "test", null);
    await h.Output.LastConnectionLossHandling;
    _activeOutput = "out:Built-in Audio Analog Stereo";
    await h.ConnectAsync("cast-a", reportedLevel: 0.40f, reportedMuted: true, streaming: false);
    await StartCastingAsync(h, follower);

    Assert.Empty(h.MuteSends());
    Assert.False(h.Output.IsSpeakerMutedByConsole);
  }

  // A GUARD, not a mutation target: it passes without F11 by design (with no recall nothing is
  // re-armed anywhere). It pins that the recall is keyed by device, so it never re-arms — and a
  // reconcile never unmutes — a different speaker the owner muted.
  [Fact]
  public async Task AConsoleMuteOrphanedOnOneSpeaker_IsNeverReleasedOnADifferentSpeaker()
  {
    await using var h = NewHarness();
    await h.ConnectAsync("cast-a", reportedLevel: 0.40f);
    var (mixer, follower) = Build(h, 0.50f);
    using var _ = follower;
    ResyncLikeAudioStateUpdateService(h.Output, mixer);
    await MuteThenLoseAsync(h, mixer, follower);
    mixer.IsMuted = false;
    await follower.LastMutePush;

    // A different speaker, muted on its own side.
    await h.ConnectAsync("cast-b", reportedLevel: 0.40f, reportedMuted: true, streaming: false);
    await StartCastingAsync(h, follower);

    Assert.Empty(h.MuteSends());
    Assert.False(h.Output.IsSpeakerMutedByConsole);
  }

  // --- AUD-37 × AUD-81: the reconnect watcher's held connect and F11 ---

  /// <summary>The reconnect watcher's connect (AUD-37): held until the receiver is confirmed free, automatic.</summary>
  private static readonly CastConnectOptions WatcherConnect =
    new() { HoldUntilReceiverConfirmed = true, AutomaticAttempt = true };

  /// <summary>
  /// What the reconnect watcher does to the same speaker after a loss, up to the start: a held
  /// connect (the speaker reports <paramref name="reportedLevel"/> and <paramref name="reportedMuted"/>,
  /// and repeats it as a status while held), then — the receiver found free — the confirmation.
  /// Asserts that nothing reaches the speaker or the console while held, and that F11's recall waits
  /// for the confirmation.
  /// </summary>
  private static async Task WatcherReconnectAsync(
    CastConsoleTestHarness h, SoundFlowMasterMixer mixer, float reportedLevel, bool reportedMuted)
  {
    var consoleMuted = mixer.IsMuted;
    await h.ConnectAsync("cast-a", reportedLevel: reportedLevel, reportedMuted: reportedMuted,
      streaming: false, options: WatcherConnect);

    Assert.True(h.Output.IsHoldingForReceiverConfirmation);
    h.RaiseStatus(reportedLevel, muted: reportedMuted); // a status while the receiver is read
    Assert.Empty(h.Commands);                       // no AUD-80 restore, no mute, no stop, while held
    Assert.Empty(h.External);                       // nothing reached the console
    Assert.Equal(consoleMuted, mixer.IsMuted);
    Assert.False(h.Output.IsSpeakerMutedByConsole); // F11 waits for the confirmation

    h.Output.ConfirmReceiverAvailable();
    Assert.False(h.Output.IsHoldingForReceiverConfirmation);
  }

  // The AUD-37 review HIGH, closed by AUD-81's F11. Mutation-checked: with the recall removed from
  // ConfirmReceiverAvailable, the after-start push re-mutes the speaker as "muted on its own side"
  // and the last assertions fail, with and without the device model.
  [Fact]
  public async Task AConsoleMutedSpeaker_LostThenAutoReconnected_AfterTheConsoleWasUnmuted_IsUnmuted()
  {
    await using var h = NewHarness();
    await h.ConnectAsync("cast-a", reportedLevel: 0.40f); // AUD-80 remembers 0.40 for cast-a
    var (mixer, follower) = Build(h, 0.50f);
    using var _ = follower;
    ResyncLikeAudioStateUpdateService(h.Output, mixer);
    await MuteThenLoseAsync(h, mixer, follower); // no unmute on the loss

    mixer.IsMuted = false; // unmuted while on the local output
    await follower.LastMutePush;
    Assert.Empty(h.Commands);

    // The speaker is still muted for the console, and reports a level other than the remembered one.
    await WatcherReconnectAsync(h, mixer, reportedLevel: 0.30f, reportedMuted: true);
    Assert.True(h.Output.IsSpeakerMutedByConsole); // re-armed at the confirmation

    await StartCastingAsync(h, follower);

    Assert.Equal(new[] { 0.40f }, h.VolumeSends()); // the remembered level, after the start
    Assert.False(h.DeviceMuted);                     // UNMUTED: by the level (device model) or the reconcile
    Assert.DoesNotContain(true, h.MuteSends());      // never muted again
    Assert.False(h.Output.IsSpeakerMutedByConsole);
    Assert.False(mixer.IsMuted);                     // the console stays unmuted
    Assert.Empty(h.External);
  }

  [Fact]
  public async Task AConsoleMutedSpeaker_LostThenAutoReconnected_UnderAStillMutedConsole_StaysMuted()
  {
    await using var h = NewHarness();
    await h.ConnectAsync("cast-a", reportedLevel: 0.40f);
    var (mixer, follower) = Build(h, 0.50f);
    using var _ = follower;
    ResyncLikeAudioStateUpdateService(h.Output, mixer);
    await MuteThenLoseAsync(h, mixer, follower);

    await WatcherReconnectAsync(h, mixer, reportedLevel: 0.30f, reportedMuted: true);
    Assert.True(h.Output.IsSpeakerMutedByConsole);

    await StartCastingAsync(h, follower);

    // No level at all (a level would unmute it on the device model): it is held for the unmute.
    Assert.Empty(h.Commands);
    Assert.True(h.DeviceMuted); // no audible window
    Assert.True(h.Output.IsSpeakerMutedByConsole);
    Assert.True(mixer.IsMuted);
    Assert.Empty(h.External);

    // The console's unmute then sends the held level first, then the unmute.
    mixer.IsMuted = false;
    await follower.LastMutePush;
    Assert.Equal(0.40f, h.VolumeSends().Single());
    Assert.False(h.DeviceMuted);
    Assert.False(mixer.IsMuted);
  }

  [Fact]
  public async Task AConsoleMutedSpeaker_UnmutedElsewhereWhileLost_IsMutedAsTheAutoReconnectStarts()
  {
    await using var h = NewHarness();
    await h.ConnectAsync("cast-a", reportedLevel: 0.40f);
    var (mixer, follower) = Build(h, 0.50f);
    using var _ = follower;
    ResyncLikeAudioStateUpdateService(h.Output, mixer);
    await MuteThenLoseAsync(h, mixer, follower);

    // Someone unmuted it while it was not connected; the console is still muted.
    await WatcherReconnectAsync(h, mixer, reportedLevel: 0.30f, reportedMuted: false);
    Assert.False(h.Output.IsSpeakerMutedByConsole); // the record is forgotten at the confirmation

    await StartCastingAsync(h, follower);

    // The start-time mute comes first, and the level is then held for the console's unmute.
    Assert.Equal(new[] { "mute" }, h.Kinds());
    Assert.True(h.DeviceMuted);
    Assert.True(h.Output.IsSpeakerMutedByConsole);
    Assert.True(mixer.IsMuted);
    Assert.Empty(h.External);
  }

  // Task 5 (AUD-37 × AUD-81): the watcher stands down from a receiver busy with another sender with
  // a plain DisconnectAsync. AUD-81's teardown release (application stop, unmute, fresh-connection
  // unmute) must never touch it — it was never ours to mute — and an automatic attempt logs no
  // Warning or Error. The console-mute record survives, so a later confirmed reconnect still recalls it.
  [Fact]
  public async Task AStandDownFromABusyReceiver_SendsNothing_AndKeepsTheConsoleMuteRecord()
  {
    await using var h = NewHarness();
    await h.ConnectAsync("cast-a", reportedLevel: 0.40f);
    var (mixer, follower) = Build(h, 0.50f);
    using var _ = follower;
    ResyncLikeAudioStateUpdateService(h.Output, mixer);
    await MuteThenLoseAsync(h, mixer, follower);
    mixer.IsMuted = false;
    await follower.LastMutePush;

    await h.ConnectAsync("cast-a", reportedLevel: 0.30f, reportedMuted: true, streaming: false, options: WatcherConnect);
    Assert.False(h.Output.IsSpeakerMutedByConsole);
    var logBefore = h.OutputLog.Lines().Count;

    await h.Output.DisconnectAsync(automaticAttempt: true, CancellationToken.None);

    Assert.Empty(h.Commands); // no "appstop", "unmute", "fresh" or "fresh-unmute"
    Assert.DoesNotContain(h.OutputLog.Lines().Skip(logBefore), l => l.Level >= LogLevel.Warning);
    Assert.Empty(h.External);
    Assert.False(mixer.IsMuted);

    // The receiver is free next time: the record was kept, and the confirmation re-arms it.
    await WatcherReconnectAsync(h, mixer, reportedLevel: 0.30f, reportedMuted: true);
    Assert.True(h.Output.IsSpeakerMutedByConsole);
  }

  [Fact]
  public async Task AfterDispose_MasterChangesAreIgnored()
  {
    await using var h = NewHarness();
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

/// <summary>
/// Every <see cref="CastConsoleVolumeFollowerTests"/> test again, on the device model measured on the
/// box (AUD-81 follow-up): a SET_VOLUME that changes the level unmutes a muted speaker, and the reply
/// status reaches the output before the send completes.
/// </summary>
public class CastConsoleVolumeFollowerTests_OnTheDeviceModel : CastConsoleVolumeFollowerTests
{
  protected override bool OnTheDeviceModel => true;
}
