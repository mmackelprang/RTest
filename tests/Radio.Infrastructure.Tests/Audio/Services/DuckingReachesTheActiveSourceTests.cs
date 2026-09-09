using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Radio.Configuration.Abstractions;
using Radio.Core.Configuration;
using Radio.Core.Events;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Models.Audio;
using Radio.Infrastructure.Audio.Services;
using Radio.Infrastructure.Audio.SoundFlow;

namespace Radio.Infrastructure.Tests.Audio.Services;

/// <summary>
/// Asserts that a duck, a duck release and a gain change actually MOVE the active source's volume —
/// and that they move nothing when the source's playback key and its <c>IAudioSource.Id</c> have
/// diverged, which is <c>AUD-2</c>.
/// </summary>
/// <remarks>
/// ⭐ <b>WHY THESE TESTS ASSERT A VALUE AND NOT AN ABSENCE OF EXCEPTIONS.</b> AUD-2's defect is that
/// <c>_duckingMultipliers[sourceId] = multiplier</c> is an INDEXER: it creates the key it is given
/// and never throws, so writing to a key nothing reads is indistinguishable, at the call site, from
/// writing to the right one. There is no exception, no log, no return value. <b>A test asserting
/// that <c>SetDuckingMultiplier</c> did not throw passes on the broken code</b>, and so does one
/// asserting that the dictionary now contains the key. The only assertion that can tell the two
/// apart is on the EFFECT: the registered component's <c>Volume</c> field, read back after the duck.
///
/// ⚠ <b>Every test in the FIRST section does that; the tests after the "signal a miss now produces"
/// divider assert on the logger instead, because the log IS their subject.</b> Two tests carry an
/// explicit <c>MUTATION:</c> note naming the edit that turns them red — not all of them. Both
/// statements were overclaimed in an earlier revision of this remark, which is the same defect this
/// file exists to catch, so they are stated exactly here.
///
/// ⚠ <b>WHAT THIS FILE DOES NOT PROVE.</b> Registration happens through
/// <c>SoundFlowPlaybackService.RegisterComponentForTests</c> — the kind-B seam AUD-2 added — because
/// the production path needs a MiniAudio playback device. So these tests prove that a key which
/// matches moves a component's Volume and that a key which diverges does not. <b>They prove nothing
/// about whether that component is connected to a speaker</b>, and nothing about whether the real
/// sources now register under the right key: that second half is pinned by
/// <c>Radio.Core.Tests.PlaybackKeyLintTests</c>, which is a source-text lint and was RED on
/// <c>main</c> at <c>2f6d8ef3</c> naming <c>SDRRadioAudioSource.cs:915</c>,
/// <c>USBAudioSourceBase.cs:317</c> and <c>FilePlayerAudioSource.cs:727</c>. <b>Audibility is UAT,
/// and a green run here is not evidence that the radio ducks.</b>
/// </remarks>
public class DuckingReachesTheActiveSourceTests
{
  private const float Tolerance = 1e-4f;

  // --- The effect, asserted ---

  [Fact]
  public async Task ADuckMovesTheActiveSourcesVolume_WhenItIsRegisteredUnderItsOwnId()
  {
    // ⭐ THE GATE. Ducking to 20% must leave the active source's component at 0.2, not at 1.0.
    //
    // MUTATION: change AudioManager.OnDuckingLevelChanged's
    // `SetDuckingMultiplier(_activeSource.Id, multiplier)` to any minted key — which is precisely
    // the AUD-2 defect, injected at the third party rather than at the source — and this goes red
    // with "expected 0.2000, was 1.0000". Measured.
    var h = await CreateAsync();
    await using (h.Manager)
    {
      var component = NewComponent();
      h.Playback.RegisterComponentForTests(h.Source.Object.Id, component, baseVolume: 1.0f);
      AssertVolume(1.0f, component, "the probe must record a write before any assertion below means anything");

      h.Source.Setup(s => s.State).Returns(AudioSourceState.Playing);
      RaiseDuckLevel(h.Ducking, from: 100f, to: 20f, complete: true);

      AssertVolume(0.2f, component, "a duck to 20% must attenuate the active source");
    }
  }

  [Fact]
  public async Task ADuckMovesNOTHING_WhenTheComponentIsRegisteredUnderAMintedKey()
  {
    // ⛔ AUD-2 ITSELF, REPRODUCED AND PINNED. This is the state the tree shipped in for six months:
    // the source registered under `sdr-radio-<guid>` while AudioManager ducked `Radio-<guid>`. The
    // multiplier is still written — the indexer creates the key — and nothing throws, and the
    // component's volume does not move by so much as a float epsilon.
    //
    // ⚠ THIS TEST MUST NOT BE "FIXED" IF IT EVER FAILS. It failing would mean somebody taught one
    // layer to translate into the other's key space, which the row and the plan both forbid: it
    // makes the symptom go away while leaving two key spaces in the tree, and the next source added
    // reintroduces the bug. The answer is one key per source, agreed by both layers.
    var h = await CreateAsync();
    await using (h.Manager)
    {
      var component = NewComponent();
      var mintedKey = $"sdr-radio-{Guid.NewGuid():N}";
      Assert.NotEqual(h.Source.Object.Id, mintedKey);

      h.Playback.RegisterComponentForTests(mintedKey, component, baseVolume: 1.0f);
      AssertVolume(1.0f, component, "the probe must record a write");

      h.Source.Setup(s => s.State).Returns(AudioSourceState.Playing);
      RaiseDuckLevel(h.Ducking, from: 100f, to: 20f, complete: true);

      AssertVolume(1.0f, component,
        "a duck addressed to the source's Id must not reach a component registered under a minted key");
      VerifyWarnings(h.Logger, Times.Once());
    }
  }

  [Fact]
  public async Task ReleasingTheDuckRestoresTheActiveSourcesVolume()
  {
    // The other half of the cycle. A fix that ducked but never released would leave the radio
    // permanently quiet, which is a worse failure than never ducking at all.
    var h = await CreateAsync();
    await using (h.Manager)
    {
      var component = NewComponent();
      h.Playback.RegisterComponentForTests(h.Source.Object.Id, component, baseVolume: 1.0f);

      RaiseDuckLevel(h.Ducking, from: 100f, to: 20f, complete: true);
      AssertVolume(0.2f, component, "precondition: the duck must have landed");

      h.Ducking.Raise(d => d.DuckingStateChanged += null, h.Ducking.Object,
        new DuckingStateChangedEventArgs { IsDucking = false, ActiveEventCount = 0 });

      AssertVolume(1.0f, component, "releasing the duck must restore the active source to full volume");
    }
  }

  [Fact]
  public async Task SetSourceGainMovesTheActiveSourcesVolume_WhenItIsRegisteredUnderItsOwnId()
  {
    // The gain slider is the other user-visible casualty of AUD-2, and it fails the same way.
    // 1.0 base * 1.5 gain * 1.0 duck = 1.5, and MaxGain is 5.0 so nothing clamps.
    var h = await CreateAsync();
    await using (h.Manager)
    {
      var component = NewComponent();
      h.Playback.RegisterComponentForTests(h.Source.Object.Id, component, baseVolume: 1.0f);
      AssertVolume(1.0f, component, "the probe must record a write");

      h.Manager.SetSourceGain(AudioSourceType.Radio, 1.5f);

      AssertVolume(1.5f, component, "the gain slider must move the active source's volume");
    }
  }

  [Fact]
  public async Task DuckAndGainCompose_OnTheSameKey()
  {
    // Effective volume is base * gain * duck, and all three are keyed on the same id. If the fix had
    // repaired only one of the two call paths, this is where the halves would disagree.
    // 1.0 * 1.5 * 0.2 = 0.30.
    var h = await CreateAsync();
    await using (h.Manager)
    {
      var component = NewComponent();
      h.Playback.RegisterComponentForTests(h.Source.Object.Id, component, baseVolume: 1.0f);

      h.Manager.SetSourceGain(AudioSourceType.Radio, 1.5f);
      RaiseDuckLevel(h.Ducking, from: 100f, to: 20f, complete: true);

      AssertVolume(0.30f, component, "gain and ducking must compose on one key");
    }
  }

  // --- The signal a miss now produces (AUD-2's second half) ---

  [Fact]
  public async Task ADuckingMissOnAPlayingSourceWarnsOncePerTransition_NotOncePerFadeStep()
  {
    // ⛔ THE STORM PIN. This handler runs once per fade STEP, and
    // DuckingService.CalculateFadeParameters gives FadeSmooth Math.Max(5, durationMs / 16) steps
    // (DuckingService.cs:494) — 18 for a 300 ms fade, so ~36 calls per attack-and-release cycle.
    // Warning on every step would put dozens of lines into journald per TTS announcement, on a box
    // where log volume correlates with audible audio distortion (CLAUDE.md § Deployment).
    //
    // MUTATION: drop `&& e.TransitionComplete` from OnDuckingLevelChanged and this goes red at 21.
    var h = await CreateAsync();
    await using (h.Manager)
    {
      h.Source.Setup(s => s.State).Returns(AudioSourceState.Playing);

      for (var i = 0; i < 20; i++)
      {
        RaiseDuckLevel(h.Ducking, from: 100f - i, to: 99f - i, complete: false);
      }

      RaiseDuckLevel(h.Ducking, from: 80f, to: 20f, complete: true);

      VerifyWarnings(h.Logger, Times.Once());
    }
  }

  [Fact]
  public async Task ADuckingMissOnANonPlayingSourceDoesNotWarn()
  {
    // ⚠ The other direction, and it matters as much. `applied == false` is the NORMAL case for a
    // source that is not playing — AudioManager stores an offset for a stopped source on purpose,
    // and SoundFlowPlaybackService reads it back at registration time. Warning on it would ship the
    // same overclaim as the line this change removed, only inverted.
    var h = await CreateAsync();
    await using (h.Manager)
    {
      h.Source.Setup(s => s.State).Returns(AudioSourceState.Ready);

      RaiseDuckLevel(h.Ducking, from: 100f, to: 20f, complete: true);

      VerifyWarnings(h.Logger, Times.Never());
    }
  }

  [Fact]
  public async Task SetSourceGainWarnsWhenTheKeyMissesOnAPlayingSource()
  {
    // AUD-2's user-facing symptom at the only layer that can recognise it: the slider moves, the
    // source says Playing, and nothing is registered under its Id.
    var h = await CreateAsync();
    await using (h.Manager)
    {
      h.Source.Setup(s => s.State).Returns(AudioSourceState.Playing);

      h.Manager.SetSourceGain(AudioSourceType.Radio, 1.5f);

      VerifyWarnings(h.Logger, Times.Once());
    }
  }

  [Fact]
  public async Task SetSourceGainDoesNotWarnWhenItActuallyReachedTheSource()
  {
    // The negative control for the test above. Same call, same Playing state — the ONLY difference
    // is that a component is registered under the source's Id. Without this, a warning that fired
    // unconditionally would satisfy the test above and be just as wrong as the line it replaced.
    var h = await CreateAsync();
    await using (h.Manager)
    {
      var component = NewComponent();
      h.Playback.RegisterComponentForTests(h.Source.Object.Id, component, baseVolume: 1.0f);
      h.Source.Setup(s => s.State).Returns(AudioSourceState.Playing);

      h.Manager.SetSourceGain(AudioSourceType.Radio, 1.5f);

      AssertVolume(1.5f, component, "precondition: the gain must have landed");
      VerifyWarnings(h.Logger, Times.Never());
    }
  }

  [Fact]
  public async Task SwitchingToAPlayingSourceWhoseKeyMissesWarns()
  {
    // The third call site: the gain offset AudioManager applies on every source switch. It needs a
    // preference persistence to be reached at all, which the other tests deliberately do without.
    var source = CreateMockPrimarySource(AudioSourceType.Radio, "FM Radio");
    source.Setup(s => s.State).Returns(AudioSourceState.Playing);

    var h = await CreateAsync(withPreferencePersistence: true, source: source);
    await using (h.Manager)
    {
      VerifyWarnings(h.Logger, Times.Once());
    }
  }

  [Fact]
  public async Task DuckingEndedReportsVolumeRestoredRatherThanAssertingIt()
  {
    // The line this pins used to read "Ducking ended: volume restored" unconditionally — printed
    // identically whether the multiplier landed, missed, or had no active source at all. The punch
    // list cited that family of lines as evidence ducking worked end to end. It now carries the
    // result, so the two cases are distinguishable in the file sink.
    var h = await CreateAsync();
    await using (h.Manager)
    {
      h.Ducking.Raise(d => d.DuckingStateChanged += null, h.Ducking.Object,
        new DuckingStateChangedEventArgs { IsDucking = false, ActiveEventCount = 0 });

      VerifyInformationContaining(h.Logger, "volumeRestored=False", Times.Once());

      var component = NewComponent();
      h.Playback.RegisterComponentForTests(h.Source.Object.Id, component, baseVolume: 1.0f);

      h.Ducking.Raise(d => d.DuckingStateChanged += null, h.Ducking.Object,
        new DuckingStateChangedEventArgs { IsDucking = false, ActiveEventCount = 0 });

      VerifyInformationContaining(h.Logger, "volumeRestored=True", Times.Once());
    }
  }

  // --- Harness ---

  private sealed record Harness(
    AudioManager Manager,
    Mock<IDuckingService> Ducking,
    SoundFlowPlaybackService Playback,
    Mock<IPrimaryAudioSource> Source,
    Mock<ILogger<AudioManager>> Logger);

  private static async Task<Harness> CreateAsync(
    bool withPreferencePersistence = false,
    Mock<IPrimaryAudioSource>? source = null)
  {
    var logger = new Mock<ILogger<AudioManager>>();
    var engine = new Mock<IAudioEngine>();
    var mixer = new Mock<IMasterMixer>();
    engine.Setup(e => e.GetMasterMixer()).Returns(mixer.Object);
    engine.Setup(e => e.IsReady).Returns(true);
    mixer.Setup(m => m.GetActiveSources()).Returns(Array.Empty<IAudioSource>());

    var ducking = new Mock<IDuckingService>();
    var playback = DeviceLessPlaybackService.Create();

    AudioPreferencePersistence? persistence = null;
    if (withPreferencePersistence)
    {
      var prefs = new Mock<IOptionsMonitor<AudioPreferences>>();
      prefs.Setup(m => m.CurrentValue).Returns(new AudioPreferences());
      persistence = new AudioPreferencePersistence(
        Mock.Of<ILogger<AudioPreferencePersistence>>(),
        engine.Object,
        prefs.Object,
        Mock.Of<IConfigurationManager>());
    }

    var manager = new AudioManager(
      logger.Object,
      engine.Object,
      Mock.Of<IAudioSourceFactory>(),
      preferencePersistence: persistence,
      playbackService: playback,
      duckingService: ducking.Object);

    source ??= CreateMockPrimarySource(AudioSourceType.Radio, "FM Radio");
    await manager.SwitchSourceAsync(source.Object);

    return new Harness(manager, ducking, playback, source, logger);
  }

  private static Mock<IPrimaryAudioSource> CreateMockPrimarySource(AudioSourceType type, string name)
  {
    var source = new Mock<IPrimaryAudioSource>();
    source.Setup(s => s.Type).Returns(type);
    source.Setup(s => s.Name).Returns(name);
    // The real thing is $"{Type}-{Guid.NewGuid():N}" (AudioSourceBase.cs:28). The SHAPE matters
    // here, not just uniqueness: half these tests turn on this string not being equal to a key of
    // the form "sdr-radio-<guid>".
    source.Setup(s => s.Id).Returns($"{type}-{Guid.NewGuid():N}");
    source.Setup(s => s.Category).Returns(AudioSourceCategory.Primary);
    source.Setup(s => s.State).Returns(AudioSourceState.Ready);
    source.As<IAsyncDisposable>().Setup(d => d.DisposeAsync()).Returns(ValueTask.CompletedTask);
    source.As<IAudioSource>().Setup(s => s.DisposeAsync()).Returns(ValueTask.CompletedTask);
    source.SetupAdd(s => s.StateChanged += It.IsAny<EventHandler<AudioSourceStateChangedEventArgs>>());
    source.SetupRemove(s => s.StateChanged -= It.IsAny<EventHandler<AudioSourceStateChangedEventArgs>>());
    return source;
  }

  /// <summary>
  /// A real <c>SoundComponent</c> whose <c>Volume</c> is the thing under assertion.
  /// </summary>
  /// <remarks>
  /// <c>CallBase = true</c> so the property behaves as SoundFlow implements it rather than being
  /// intercepted by Moq and swallowed. Constructing it with a null engine is safe and is the idiom
  /// already used by <c>BluetoothAudioSourceTests</c>: <c>SoundComponent</c> stores the engine
  /// reference without dereferencing it (design/TESTING.md § Test Seams, rule 1).
  ///
  /// ⚠ Every test that reads this back opens with an assertion that the probe holds the value it was
  /// registered with. If Moq ever starts intercepting the property, that guard fails loudly instead
  /// of every later assertion silently comparing 0 against 0.
  /// </remarks>
  private static global::SoundFlow.Abstracts.SoundComponent NewComponent() =>
    new Mock<global::SoundFlow.Abstracts.SoundComponent>(
      MockBehavior.Loose, null!, default(global::SoundFlow.Structs.AudioFormat))
    { CallBase = true }.Object;

  private static void RaiseDuckLevel(Mock<IDuckingService> ducking, float from, float to, bool complete) =>
    ducking.Raise(d => d.DuckingLevelChanged += null, ducking.Object,
      new DuckingLevelChangedEventArgs
      {
        PreviousLevel = from,
        NewLevel = to,
        TransitionComplete = complete
      });

  private static void AssertVolume(
    float expected, global::SoundFlow.Abstracts.SoundComponent component, string because) =>
    Assert.True(
      Math.Abs(component.Volume - expected) < Tolerance,
      $"{because} — expected {expected:F4}, was {component.Volume:F4}.");

  /// <summary>
  /// ⚠ Counts warnings from the WHOLE AudioManager, not from one line. If AudioManager grows an
  /// unrelated warning on these paths these tests get more brittle, not less correct — tighten the
  /// matcher to the message text rather than raising the expected count.
  /// </summary>
  private static void VerifyWarnings(Mock<ILogger<AudioManager>> logger, Times times) =>
    logger.Verify(
      l => l.Log(
        LogLevel.Warning,
        It.IsAny<EventId>(),
        It.IsAny<It.IsAnyType>(),
        It.IsAny<Exception?>(),
        (Func<It.IsAnyType, Exception?, string>)It.IsAny<object>()),
      times);

  private static void VerifyInformationContaining(
    Mock<ILogger<AudioManager>> logger, string fragment, Times times) =>
    logger.Verify(
      l => l.Log(
        LogLevel.Information,
        It.IsAny<EventId>(),
        It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains(fragment, StringComparison.Ordinal)),
        It.IsAny<Exception?>(),
        (Func<It.IsAnyType, Exception?, string>)It.IsAny<object>()),
      times);
}
