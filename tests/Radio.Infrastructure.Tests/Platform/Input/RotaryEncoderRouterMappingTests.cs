using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Radio.Core.Configuration;
using Radio.Core.Interfaces;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Interfaces.Input;
using Radio.Core.Models.Audio;
using Radio.Infrastructure.Platform.Input;

namespace Radio.Infrastructure.Tests.Platform.Input;

/// <summary>
/// Covers <see cref="RotaryEncoderActionRouter"/>'s HUD publishing, the ENC-4b unmute-on-turn rule,
/// and the press/release split that ENC-4 introduced.
///
/// <para>
/// It also <b>pins the encoder-index to handler mapping</b>, which ENC-7 brought to its final
/// state: 0=Volume, 1=Source, 2=Presets, 3=Tuning, matching the cabinet's
/// VOLUME / SOURCE / PRESETS / TUNING engraving on every knob. Pinning it here makes the next change
/// a decision somebody had to make rather than something that drifted.
/// </para>
/// </summary>
public class RotaryEncoderRouterMappingTests
{
  // --- Fakes -------------------------------------------------------------------------------

  private sealed class FakeEncoderService : IRotaryEncoderService
  {
    public bool IsConnected { get; set; } = true;
    public RotaryEncoderConfigStatus ConfigStatus { get; set; } = RotaryEncoderConfigStatus.Configured;

    public event EventHandler<EncoderTurnedEventArgs>? EncoderTurned;
    public event EventHandler<EncoderButtonEventArgs>? ButtonPressed;
    public event EventHandler<EncoderConnectionEventArgs>? ConnectionChanged;
#pragma warning disable CS0067 // Nothing in these tests raises a config-tier change.
    public event EventHandler<EncoderConfigStatusEventArgs>? ConfigStatusChanged;
#pragma warning restore CS0067

    public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public void Dispose() { }

    public void RaiseTurn(int index, int delta) =>
      EncoderTurned?.Invoke(this, new EncoderTurnedEventArgs { EncoderIndex = index, Delta = delta });

    public void RaiseButton(int index, bool isPressed) =>
      ButtonPressed?.Invoke(this, new EncoderButtonEventArgs { EncoderIndex = index, IsPressed = isPressed });

    public void RaiseConnection(bool connected) =>
      ConnectionChanged?.Invoke(this, new EncoderConnectionEventArgs { IsConnected = connected });
  }

  /// <summary>Records what the router published, without coalescing, so assertions see every card.</summary>
  private sealed class RecordingHudSink : IEncoderFeedbackSink
  {
    public readonly List<EncoderHudEventArgs> Published = [];

    public event EventHandler<EncoderHudEventArgs>? Feedback;

    public void Publish(EncoderHudEventArgs update)
    {
      Published.Add(update);
      Feedback?.Invoke(this, update);
    }
  }

  private sealed class FakeSleepService : ISleepService
  {
    private int _wakeClaimed;

    public bool IsSleeping { get; set; }
    public bool IsSleepScreenVisible { get; set; }
    public int EnterSleepCalls { get; private set; }
    public int WakeCalls { get; private set; }
    public int ClaimAttempts { get; private set; }

    /// <summary>
    /// Mirrors the shipped derivation in <c>SleepService</c>, claim latch included, so a router test
    /// exercises the same three-way decision the box does rather than a simplified one.
    /// </summary>
    public ConsoleWakeState WakeState
    {
      get
      {
        if (Volatile.Read(ref _wakeClaimed) == 1)
        {
          return ConsoleWakeState.Awake;
        }

        if (IsSleeping)
        {
          return ConsoleWakeState.Standby;
        }

        return IsSleepScreenVisible ? ConsoleWakeState.Ambient : ConsoleWakeState.Awake;
      }
    }

    public Task EnterSleepAsync()
    {
      EnterSleepCalls++;
      Interlocked.Exchange(ref _wakeClaimed, 0);
      return Task.CompletedTask;
    }

    public Task WakeAsync(string wakeSource = "unknown")
    {
      WakeCalls++;
      return Task.CompletedTask;
    }

    public void SetSleepScreenVisible(bool visible)
    {
      // Edge-triggered, mirroring the shipped SleepService. If these two ever disagree, every gate
      // test above is exercising a policy the box does not run.
      bool changed = IsSleepScreenVisible != visible;
      IsSleepScreenVisible = visible;
      if (changed)
      {
        Interlocked.Exchange(ref _wakeClaimed, 0);
      }
    }

    public bool TryClaimWake()
    {
      ClaimAttempts++;
      if (WakeState == ConsoleWakeState.Awake)
      {
        return false;
      }

      return Interlocked.CompareExchange(ref _wakeClaimed, 1, 0) == 0;
    }
  }

  /// <summary>
  /// Only the members the router and the source selector touch carry behaviour:
  /// <see cref="MasterVolume"/>, <see cref="IsMuted"/>, <see cref="ActiveSource"/> and the source
  /// cache. <see cref="MuteWrites"/> exists so a test can tell "left alone" apart from "written
  /// back to the value it already had".
  /// </summary>
  private sealed class FakeAudioManager : IAudioManager
  {
    private float _masterVolume = 0.5f;
    private bool _isMuted;

    public int MuteWrites { get; private set; }
    public List<AudioSourceType> GetOrCreateCalls { get; } = [];

    public float MasterVolume
    {
      get => _masterVolume;
      set => _masterVolume = value;
    }

    public bool IsMuted
    {
      get => _isMuted;
      set
      {
        MuteWrites++;
        _isMuted = value;
      }
    }

    public float Balance { get; set; }
    public IAudioSource? ActiveSource { get; set; }

    public IAudioEngine Engine =>
      throw new NotSupportedException("The router never touches the engine.");

    public float GetSourceGain(AudioSourceType sourceType) => 1f;
    public void SetSourceGain(AudioSourceType sourceType, float gain) { }
    public Dictionary<string, float> GetAllSourceGains() => [];

    public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task SwitchSourceAsync(IAudioSource source, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<IAudioSource?> GetOrCreateSourceAsync(
      AudioSourceType sourceType,
      bool switchToSource = true,
      CancellationToken cancellationToken = default)
    {
      GetOrCreateCalls.Add(sourceType);
      return Task.FromResult<IAudioSource?>(null);
    }

    public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public IAudioSource? GetCachedSource(AudioSourceType sourceType) => null;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
  }

  private sealed class StaticOptionsMonitor<T> : IOptionsMonitor<T>
  {
    private readonly T _value;
    public StaticOptionsMonitor(T value) { _value = value; }
    public T CurrentValue => _value;
    public T Get(string? name) => _value;
    public IDisposable OnChange(Action<T, string?> listener) => new NullDisposable();

    private sealed class NullDisposable : IDisposable { public void Dispose() { } }
  }

  private sealed class Harness : IDisposable
  {
    public readonly FakeEncoderService Encoders = new();
    public readonly RecordingHudSink Hud = new();
    public readonly FakeSleepService Sleep = new();
    public readonly FakeAudioManager Audio = new();
    public readonly FakeBandMemory BandMemory = new();
    public readonly FakeTimeProvider Time = new();
    public readonly PresetBankScope Presets = new();
    public readonly SourceSelectorService Selector;
    public readonly PresetSelectorService PresetSelector;
    public readonly RotaryEncoderActionRouter Router;

    public Harness()
    {
      Selector = new SourceSelectorService(
        NullLogger<SourceSelectorService>.Instance,
        () => Audio,
        () => BandMemory,
        Hud,
        Time);

      // The bank is EMPTY by default, so a turn on index 2 publishes exactly one card: the
      // overlay's own preview. The background reload that follows finds nothing to change and
      // therefore publishes nothing, which is what keeps the Assert.Single assertions here sound.
      PresetSelector = new PresetSelectorService(
        NullLogger<PresetSelectorService>.Instance,
        Presets.Factory,
        () => Audio,
        () => BandMemory,
        Hud,
        Time);

      Router = new RotaryEncoderActionRouter(
        NullLogger<RotaryEncoderActionRouter>.Instance,
        Encoders,
        () => Audio,
        new StaticOptionsMonitor<RotaryEncoderOptions>(new RotaryEncoderOptions()),
        Hud,
        Selector,
        PresetSelector,
        Sleep,
        Time);
    }

    /// <summary>Puts one saved station in the bank the PRESETS knob reads.</summary>
    public void SeedPreset(string name, RadioBand band, double hertz) =>
      Presets.Bank.Seed(name, band, hertz);

    /// <summary>Makes a tuner the active source, so the tuning handler takes its radio branch.</summary>
    public FakeRadioSource WithActiveRadio()
    {
      var radio = new FakeRadioSource();
      Audio.ActiveSource = radio;
      return radio;
    }

    public IReadOnlyList<EncoderHudEventArgs> Cards(EncoderHudPhase phase) =>
      Hud.Published.Where(c => c.Phase == phase).ToList();

    public void Dispose()
    {
      Router.Dispose();
      Selector.Dispose();
      PresetSelector.Dispose();
      Presets.Dispose();
    }
  }

  // --- The mapping, pinned -----------------------------------------------------------------

  [Fact]
  public void EncoderIndexZero_IsVolume_UnderBothTheOldAndTheNewPhysicalOrder()
  {
    // The one index ENC-5's remap does not touch, and the only one with a safety hazard behind it.
    Assert.Equal(0, RotaryEncoderConfigDefaults.VolumeEncoderIndex);
  }

  /// <summary>
  /// Pins the final index-to-handler mapping: 0 = VOLUME, 1 = SOURCE, 2 = PRESETS, 3 = TUNING.
  ///
  /// <para>
  /// This matches the escutcheon (D2) and the configuration ENC-11 pushes to the device. A red
  /// assertion here means the router and the cabinet have diverged, and on the volume row it means
  /// they have diverged on the knob with a safety hazard behind it.
  /// </para>
  /// </summary>
  [Theory]
  [InlineData(0, "VOLUME")]
  [InlineData(1, "SOURCE")]
  [InlineData(2, "PRESETS")]
  [InlineData(3, "TUNING")]
  public void EncoderTurn_PublishesACardLabelledForThatKnob(int index, string expectedLabel)
  {
    using var h = new Harness();
    // A tuner is active so index 3 takes its radio branch; without one it would publish TRACK,
    // which is the subject of its own test below.
    h.WithActiveRadio();

    h.Encoders.RaiseTurn(index, 1);

    var card = Assert.Single(h.Hud.Published);
    Assert.Equal(index, card.EncoderIndex);
    Assert.Equal(expectedLabel, card.Label);
  }

  [Fact]
  public void SourceTurn_PublishesInItsOwnQuarter_NotTheOldSourceIndex()
  {
    // Regression guard for the hard-coded HUD index the remap had to remove: before ENC-5 the
    // source handler published a literal 2, so after the remap the card would have appeared beside
    // the PRESETS knob instead of the SOURCE one.
    using var h = new Harness();

    h.Encoders.RaiseTurn(1, 1);

    Assert.Equal(1, Assert.Single(h.Hud.Published).EncoderIndex);
  }

  [Fact]
  public void TuningTurn_PublishesInItsOwnQuarter()
  {
    using var h = new Harness();
    h.WithActiveRadio();

    h.Encoders.RaiseTurn(3, 1);

    Assert.Equal(3, Assert.Single(h.Hud.Published).EncoderIndex);
  }

  // --- Every handler publishes ---------------------------------------------------------------

  [Fact]
  public void VolumeTurn_PublishesAHudCardForItsOwnQuarter()
  {
    using var h = new Harness();

    h.Encoders.RaiseTurn(0, 1);

    var card = Assert.Single(h.Hud.Published);
    Assert.Equal(0, card.EncoderIndex);
    Assert.Equal("VOLUME", card.Label);
    Assert.NotNull(card.VolumePercent);
  }

  [Fact]
  public void TuningTurnOnANonRadioSource_PublishesACardThatSaysWhatItDidNotDo()
  {
    using var h = new Harness();

    h.Encoders.RaiseTurn(3, 1);

    var card = Assert.Single(h.Hud.Published);
    Assert.Equal("TRACK", card.Label);
    Assert.Equal("no track control on this source", card.SecondaryText);
  }

  [Fact]
  public void SourceTurn_PublishesTheSelectionWithoutSwitchingToIt()
  {
    using var h = new Harness();

    h.Encoders.RaiseTurn(1, 1);

    var card = Assert.Single(h.Hud.Published);
    Assert.Equal("SOURCE", card.Label);
    Assert.Equal(EncoderHudPhase.SelectorPreview, card.Phase);
    // Turning previews; the press commits. Nothing was switched.
    Assert.Empty(h.Audio.GetOrCreateCalls);
  }

  [Fact]
  public void SourceTurn_DoesNotSwitchAnySource()
  {
    // The preview-not-commit rule at the router level. Spinning the list must never tear down and
    // stand up an audio source per detent.
    using var h = new Harness();

    for (int i = 0; i < 6; i++)
    {
      h.Encoders.RaiseTurn(1, 1);
    }

    Assert.Empty(h.Audio.GetOrCreateCalls);
  }

  [Fact]
  public void SourcePress_WithOverlayClosed_OpensWithoutSwitching()
  {
    using var h = new Harness();

    h.Encoders.RaiseButton(1, true);
    h.Time.Advance(TimeSpan.FromMilliseconds(200));
    h.Encoders.RaiseButton(1, false);

    var card = Assert.Single(h.Hud.Published);
    Assert.Equal(EncoderHudPhase.SelectorPreview, card.Phase);
    Assert.Empty(h.Audio.GetOrCreateCalls);
  }

  [Fact]
  public void SourceLongPress_DoesNothing()
  {
    // SOURCE has no long action (handoff §4.4), so it must not publish a HoldStart either: a ring
    // that fills and does nothing is a promise the code does not keep.
    using var h = new Harness();

    h.Encoders.RaiseButton(1, true);
    h.Time.Advance(TimeSpan.FromMilliseconds(1000));
    h.Encoders.RaiseButton(1, false);

    Assert.Empty(h.Cards(EncoderHudPhase.HoldStart));
    Assert.Empty(h.Audio.GetOrCreateCalls);
  }

  // --- ENC-4b --------------------------------------------------------------------------------

  [Fact]
  public void VolumeTurnWhileMuted_Unmutes()
  {
    using var h = new Harness();
    h.Audio.IsMuted = true;

    h.Encoders.RaiseTurn(0, 1);

    Assert.False(h.Audio.IsMuted);
  }

  [Fact]
  public void VolumeTurnWhileMuted_AlsoAppliesTheDelta()
  {
    using var h = new Harness();
    h.Audio.IsMuted = true;
    float before = h.Audio.MasterVolume;

    h.Encoders.RaiseTurn(0, 1);

    // An unmute-only implementation would need a second detent to move anything.
    Assert.True(h.Audio.MasterVolume > before);
  }

  [Fact]
  public void VolumeTurnWhileMuted_PublishesAnUnmutedCard()
  {
    using var h = new Harness();
    h.Audio.IsMuted = true;

    h.Encoders.RaiseTurn(0, 1);

    Assert.False(Assert.Single(h.Hud.Published).IsMuted);
  }

  [Fact]
  public void VolumeTurnWhileNotMuted_DoesNotTouchMute()
  {
    using var h = new Harness();
    int writesBefore = h.Audio.MuteWrites;

    h.Encoders.RaiseTurn(0, 1);

    Assert.Equal(writesBefore, h.Audio.MuteWrites);
  }

  // --- Press/release and the long press ------------------------------------------------------

  [Fact]
  public void VolumeShortPress_TogglesMute()
  {
    using var h = new Harness();

    h.Encoders.RaiseButton(0, true);
    h.Time.Advance(TimeSpan.FromMilliseconds(200));
    h.Encoders.RaiseButton(0, false);

    Assert.True(h.Audio.IsMuted);
  }

  [Fact]
  public void VolumeShortPress_PublishesTheMuteStateItJustProduced()
  {
    // Regression guard. The card published on a mute toggle must carry the state AFTER the toggle.
    // It previously carried the state before it, because the HoldCancelled event that publishes the
    // card was raised ahead of the short action that changes it - so the HUD asserted the opposite
    // of the truth for the card's full lifetime, on the one knob with a safety hazard behind it.
    using var h = new Harness();
    Assert.False(h.Audio.IsMuted);

    h.Encoders.RaiseButton(0, true);
    h.Time.Advance(TimeSpan.FromMilliseconds(200));
    h.Encoders.RaiseButton(0, false);

    Assert.True(h.Audio.IsMuted);
    EncoderHudEventArgs last = h.Hud.Published[^1];
    Assert.True(last.IsMuted);

    // ...and back again, so the assertion is about tracking the state rather than a fixed value.
    h.Encoders.RaiseButton(0, true);
    h.Time.Advance(TimeSpan.FromMilliseconds(200));
    h.Encoders.RaiseButton(0, false);

    Assert.False(h.Audio.IsMuted);
    Assert.False(h.Hud.Published[^1].IsMuted);
  }

  [Fact]
  public void VolumeShortPress_DoesNotFireOnThePressEdge()
  {
    using var h = new Harness();

    h.Encoders.RaiseButton(0, true);
    h.Time.Advance(TimeSpan.FromMilliseconds(200));

    // Firing mute on press would fire it on the way into every hold-for-standby.
    Assert.False(h.Audio.IsMuted);
  }

  [Fact]
  public void VolumeLongPress_EntersStandby()
  {
    using var h = new Harness();

    h.Encoders.RaiseButton(0, true);
    h.Time.Advance(TimeSpan.FromMilliseconds(600));

    Assert.Equal(1, h.Sleep.EnterSleepCalls);
  }

  [Fact]
  public void VolumeLongPress_ThenRelease_DoesNotAlsoToggleMute()
  {
    using var h = new Harness();

    h.Encoders.RaiseButton(0, true);
    h.Time.Advance(TimeSpan.FromMilliseconds(600));
    h.Encoders.RaiseButton(0, false);

    Assert.Equal(1, h.Sleep.EnterSleepCalls);
    // A console that entered standby and then unmuted itself on the way out is audible and
    // confusing; the release after a fired long press is deliberately inert.
    Assert.False(h.Audio.IsMuted);
    Assert.Equal(0, h.Audio.MuteWrites);
  }

  /// <summary>
  /// Pins SOURCE (index 1): the selector knob that still has no long action, so it commits nothing
  /// and promises nothing. Kept under its original name because it moved knobs rather than
  /// disappearing - it pinned index 2 until ENC-7 put the save gesture there.
  /// </summary>
  [Fact]
  public void SelectorLongPress_DoesNothing()
  {
    using var h = new Harness();

    // Moved from index 2 to index 1 by ENC-7: index 2 now saves a preset, and SOURCE is the
    // selector knob that still has no long action at all.
    h.Encoders.RaiseButton(1, true);
    h.Time.Advance(TimeSpan.FromMilliseconds(1000));
    h.Encoders.RaiseButton(1, false);

    // Nothing commits and no ring is promised.
    Assert.Empty(h.Audio.GetOrCreateCalls);
    Assert.Empty(h.Cards(EncoderHudPhase.HoldStart));
  }

  [Fact]
  public void HoldStart_IsPublishedForVolumeAndPresetsOnly()
  {
    using var h = new Harness();

    for (int index = 0; index < 4; index++)
    {
      h.Encoders.RaiseButton(index, true);
      h.Time.Advance(TimeSpan.FromMilliseconds(100));
      h.Encoders.RaiseButton(index, false);
    }

    // Exactly two knobs have a long action, so exactly two draw a ring. A third would promise an
    // action nothing performs.
    var holdStarts = h.Cards(EncoderHudPhase.HoldStart);
    Assert.Equal(2, holdStarts.Count);
    Assert.Equal(0, holdStarts[0].EncoderIndex);
    Assert.Equal("HOLD FOR STANDBY", holdStarts[0].Label);
    Assert.Equal(2, holdStarts[1].EncoderIndex);
    Assert.Equal("HOLD TO SAVE", holdStarts[1].Label);
    Assert.DoesNotContain(holdStarts, c => c.EncoderIndex is 1 or 3);
  }

  [Fact]
  public void TuningLongPress_StillDoesNothing()
  {
    using var h = new Harness();
    h.WithActiveRadio();

    h.Encoders.RaiseButton(3, true);
    h.Time.Advance(TimeSpan.FromMilliseconds(1000));

    Assert.Empty(h.Cards(EncoderHudPhase.HoldStart));
    Assert.Equal(0, h.Sleep.EnterSleepCalls);
  }

  [Fact]
  public void WakeConsumesThePressEdge_AndTheReleaseDoesNotFireTheShortAction()
  {
    using var h = new Harness();
    h.Sleep.IsSleeping = true;

    h.Encoders.RaiseButton(0, true);
    // Waking is a state change on the API side; the fake stays "sleeping" so this only asserts the
    // router's own behaviour on the edge pair.
    h.Time.Advance(TimeSpan.FromMilliseconds(200));
    h.Encoders.RaiseButton(0, false);

    Assert.Equal(1, h.Sleep.WakeCalls);
    Assert.False(h.Audio.IsMuted);
    Assert.Equal(0, h.Audio.MuteWrites);
    // ENC-6 inverts the last assertion of this fact deliberately. It used to be
    // Assert.Empty(h.Hud.Published) - written when a consumed input produced nothing at all. A
    // consumed input now answers "where am I" without changing anything (handoff 8.3), so the card
    // is the deliverable and MuteWrites above is what carries the "no action fired" half.
    var card = Assert.Single(h.Hud.Published);
    Assert.Equal(0, card.EncoderIndex);
    Assert.Equal("VOLUME", card.Label);
    Assert.Equal(EncoderHudPhase.Value, card.Phase);
  }

  // --- ENC-7: the PRESETS knob -----------------------------------------------------------------

  [Fact]
  public void PresetsTurn_PublishesInItsOwnQuarter()
  {
    using var h = new Harness();

    h.Encoders.RaiseTurn(2, 1);

    Assert.Equal(2, Assert.Single(h.Hud.Published).EncoderIndex);
  }

  [Fact]
  public void PresetsTurn_PlaysNothing()
  {
    // Handoff 4.4: a turn moves a highlight and nothing plays. Seeded and spun, because the hazard
    // is a recall on the way past a row rather than on the first detent of an empty list.
    using var h = new Harness();
    h.SeedPreset("KEXP", RadioBand.FM, 90_300_000);
    h.SeedPreset("KUOW", RadioBand.FM, 94_900_000);
    var radio = h.WithActiveRadio();

    for (int i = 0; i < 6; i++)
    {
      h.Encoders.RaiseTurn(2, 1);
    }

    Assert.Empty(h.Audio.GetOrCreateCalls);
    Assert.Empty(radio.FrequenciesSet);
    Assert.Empty(radio.BandsSet);
  }

  [Fact]
  public void PresetsLongPress_PublishesAHoldStartRing()
  {
    // ENC-4 suppressed the ring on this knob because a ring elsewhere would promise an action
    // nothing performs. Index 2 now performs one, so the ring has to draw.
    using var h = new Harness();

    h.Encoders.RaiseButton(2, true);
    h.Time.Advance(TimeSpan.FromMilliseconds(600));

    var card = Assert.Single(h.Cards(EncoderHudPhase.HoldStart));
    Assert.Equal(2, card.EncoderIndex);
    Assert.Equal("HOLD TO SAVE", card.Label);

    // The other half of the design, and the half that would regress silently: a completed hold on
    // this knob publishes NO HoldCommit. The save's own SelectorNotice is what collapses the ring,
    // and a label-only HoldCommit card would replace the overlay to do the same job worse.
    Assert.Empty(h.Cards(EncoderHudPhase.HoldCommit));
  }

  [Fact]
  public void PresetsPress_WithTheOverlayAlreadyOpen_PublishesNoHoldCard()
  {
    // Regression guard for the PRESS-DOWN edge. EncoderLongPressGesture raises HoldStarted on
    // press-down, not when the ring reaches its draw threshold, and a hold phase is not a selector
    // phase - so an unguarded publish here would swap the open list for a label-only card on every
    // press, for as long as the finger is down. That is this row's primary interaction (turn to
    // preview, press to recall) broken on every press.
    using var h = new Harness();
    h.SeedPreset("KEXP", RadioBand.FM, 90_300_000);
    h.Encoders.RaiseTurn(2, 1);
    Assert.True(h.PresetSelector.IsOpen);

    h.Encoders.RaiseButton(2, isPressed: true);

    Assert.Empty(h.Cards(EncoderHudPhase.HoldStart));
    // The list is still what is on screen.
    EncoderHudEventArgs last = h.Hud.Published[^1];
    Assert.Equal(EncoderHudPhase.SelectorPreview, last.Phase);
    Assert.NotEmpty(last.Rows!);
  }

  [Fact]
  public void PresetsShortPress_DoesNotPublishAHoldCancelCardOverTheOverlay()
  {
    // Regression guard. EncoderLongPressGesture raises ShortPress BEFORE HoldCancelled, so a
    // sub-threshold press opens the overlay and the cancel edge arrives afterwards. If index 2
    // published a hold phase on that edge, a label-only card with no rows would replace the
    // overlay the press had just opened.
    using var h = new Harness();

    h.Encoders.RaiseButton(2, true);
    h.Time.Advance(TimeSpan.FromMilliseconds(200));
    h.Encoders.RaiseButton(2, false);

    Assert.Empty(h.Cards(EncoderHudPhase.HoldCancel));
    Assert.Equal(EncoderHudPhase.SelectorPreview, h.Hud.Published[^1].Phase);
  }

  /// <summary>
  /// The index-2 turn description has to contain the word "preset", and this is the only place
  /// that says so.
  ///
  /// <para>
  /// <c>SystemConfigPage.DescribesItsCabinetRole</c> decides whether a knob agrees with its
  /// engraving by keyword-matching the turn description — there is no handler identity on the
  /// wire — and feeds the "does not match the cabinet" banner. A reword that drops the keyword
  /// relights that banner on a knob that is correct, with nothing else failing.
  /// </para>
  /// </summary>
  [Fact]
  public void PresetsMappingDescription_SatisfiesTheSettingsPageCabinetCheck()
  {
    using var h = new Harness();

    Assert.Contains("preset", h.Router.Mapping[2].TurnDescription, StringComparison.OrdinalIgnoreCase);
  }

  /// <summary>
  /// The visualiser is no longer an input to the router, and re-adding it should be a deliberate
  /// act rather than a merge artefact.
  ///
  /// <para>
  /// <c>VisualizationModeService</c> no longer exists — <c>ENC-9</c> deleted it, together with the
  /// SignalR broadcast and the browser subscription behind it, once <c>ENC-7</c> removed its only
  /// writer. This test therefore asserts against a type <i>name</i> rather than a type, and that is
  /// deliberate: a string still works after the class is gone, and re-introducing a visualiser
  /// dependency on the router should be a deliberate act rather than a merge artefact.
  /// </para>
  ///
  /// <para>
  /// The <b>capability</b> is unaffected and always was: the on-screen six-segment picker mutates
  /// <c>VisualizerPanel</c>'s own private enum and never went through that service.
  /// </para>
  /// </summary>
  [Fact]
  public void Router_NoLongerDependsOnVisualizationModeService()
  {
    var parameters = typeof(RotaryEncoderActionRouter)
      .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
      .SelectMany(c => c.GetParameters())
      .Select(p => p.ParameterType.Name);

    Assert.DoesNotContain("VisualizationModeService", parameters);
  }

  // --- ENC-0 teardown --------------------------------------------------------------------------

  [Fact]
  public void EncoderDisconnect_DismissesAnOpenSelectorWithoutCommitting()
  {
    using var h = new Harness();
    h.Encoders.RaiseTurn(1, 1);
    Assert.True(h.Selector.IsOpen);

    h.Encoders.RaiseConnection(connected: false);

    Assert.False(h.Selector.IsOpen);
    Assert.Empty(h.Audio.GetOrCreateCalls);
  }

  [Fact]
  public void EncoderDisconnect_AlsoDismissesThePresetsOverlay()
  {
    // Half a teardown is worse than none: only one of the two selector knobs would recover.
    using var h = new Harness();
    h.Encoders.RaiseTurn(2, 1);
    Assert.True(h.PresetSelector.IsOpen);

    h.Encoders.RaiseConnection(connected: false);

    Assert.False(h.PresetSelector.IsOpen);
    Assert.Empty(h.Audio.GetOrCreateCalls);
  }

  // --- The sleep gate (ENC-6, handoff 8.3) --------------------------------------------------

  [Fact]
  public void Ambient_VolumeTurn_ActsInPlace()
  {
    // Rule 2. The lit clock is the one state where a knob still changes the machine, and it is the
    // knob whose readout the sleep screen was already built to host.
    using var h = new Harness();
    h.Sleep.SetSleepScreenVisible(true);

    h.Encoders.RaiseTurn(0, 1);

    Assert.Equal(0, h.Sleep.WakeCalls);
    Assert.True(h.Audio.MasterVolume > 0.5f);
  }

  [Fact]
  public void Ambient_SourceTurn_IsConsumedAndWakes()
  {
    // Renamed from the plan's Ambient_TuningTurn_IsConsumedAndWakes: index 1 was TUNING when the
    // plan was written and is SOURCE since ENC-5 merged. The index and the assertions are the
    // plan's; only the name moved, so it says what it actually exercises.
    using var h = new Harness();
    h.Sleep.SetSleepScreenVisible(true);
    h.Audio.ActiveSource = null;

    h.Encoders.RaiseTurn(1, 1);

    Assert.Equal(1, h.Sleep.WakeCalls);
  }

  [Fact]
  public void Ambient_ASecondTurnDuringTheWake_Acts()
  {
    // The latch, from the router's side. The browser has not left /sleep yet, so IsSleepScreenVisible
    // is still true - but the claim is spent, so the second detent must reach its handler.
    using var h = new Harness();
    h.Sleep.SetSleepScreenVisible(true);

    h.Encoders.RaiseTurn(1, 1);
    h.Encoders.RaiseTurn(1, 1);
    h.Encoders.RaiseTurn(1, 1);

    Assert.Equal(1, h.Sleep.WakeCalls);
    Assert.Equal(ConsoleWakeState.Awake, h.Sleep.WakeState);
  }

  [Fact]
  public void Standby_ATurn_DoesNotResumeAudio()
  {
    // D22, verbatim: "a turn is what a passing sleeve does; a press is what a person does."
    using var h = new Harness();
    h.Sleep.IsSleeping = true;
    h.Sleep.SetSleepScreenVisible(true);

    h.Encoders.RaiseTurn(0, 1);
    h.Encoders.RaiseTurn(1, 1);
    h.Encoders.RaiseTurn(2, 1);
    h.Encoders.RaiseTurn(3, 1);

    Assert.Equal(0, h.Sleep.WakeCalls);
    Assert.Equal(0.5f, h.Audio.MasterVolume);
    Assert.Equal(0, h.Audio.MuteWrites);
  }

  [Fact]
  public void Standby_APress_Resumes()
  {
    using var h = new Harness();
    h.Sleep.IsSleeping = true;
    h.Sleep.SetSleepScreenVisible(true);

    h.Encoders.RaiseButton(2, isPressed: true);

    Assert.Equal(1, h.Sleep.WakeCalls);
  }

  [Fact]
  public void Standby_APressAfterATurn_StillResumes()
  {
    // The turn must not have burned the claim - otherwise a sleeve brushing a knob would leave the
    // console unable to be turned on by the press that follows it.
    using var h = new Harness();
    h.Sleep.IsSleeping = true;
    h.Sleep.SetSleepScreenVisible(true);

    h.Encoders.RaiseTurn(3, 1);
    h.Encoders.RaiseButton(0, isPressed: true);

    Assert.Equal(1, h.Sleep.WakeCalls);
  }

  [Fact]
  public void Ambient_VolumeLongPress_StillEntersStandby()
  {
    // 8.3's Ambient column keeps encoder 0 fully live, hold included. This is the one path from the
    // clock into Standby that does not involve the topbar.
    using var h = new Harness();
    h.Sleep.SetSleepScreenVisible(true);

    h.Encoders.RaiseButton(0, isPressed: true);
    h.Time.Advance(TimeSpan.FromMilliseconds(EncoderInteractionTimings.LongPressThresholdMs));

    Assert.Equal(1, h.Sleep.EnterSleepCalls);
    Assert.Equal(0, h.Sleep.WakeCalls);
  }

  [Fact]
  public void Awake_NothingIsConsumed()
  {
    using var h = new Harness();

    h.Encoders.RaiseTurn(0, 1);

    Assert.Equal(0, h.Sleep.WakeCalls);
    Assert.Equal(0, h.Sleep.ClaimAttempts);
  }

  // --- A consumed input still says where you are (ENC-6, handoff 8.3) ------------------------

  [Fact]
  public void Standby_AConsumedTurn_PublishesThatKnobsCurrentValueWithoutChangingIt()
  {
    using var h = new Harness();
    h.Sleep.IsSleeping = true;
    h.Sleep.SetSleepScreenVisible(true);
    h.Audio.MasterVolume = 0.62f;

    h.Encoders.RaiseTurn(0, 1);

    var card = Assert.Single(h.Hud.Published);
    Assert.Equal(0, card.EncoderIndex);
    Assert.Equal("VOLUME", card.Label);
    Assert.Equal(62, card.VolumePercent);
    Assert.Equal(0.62f, h.Audio.MasterVolume);
  }

  [Fact]
  public void Ambient_AConsumedTurn_PublishesOnItsOwnBand()
  {
    // The card has to appear beside the knob that was turned, not beside the knob that woke the
    // console - the geometry keys off the index the event arrived on.
    using var h = new Harness();
    h.Sleep.SetSleepScreenVisible(true);
    h.Audio.ActiveSource = null;

    h.Encoders.RaiseTurn(3, 1);

    var card = Assert.Single(h.Hud.Published);
    Assert.Equal(3, card.EncoderIndex);
  }

  [Fact]
  public void ConsumedTurnsInStandby_KeepAnswering_TheyDoNotFallSilentAfterTheFirst()
  {
    // D22 makes a turn in Standby permanently consumed, so "spend one and stop rendering" would
    // leave three knobs looking broken for the whole standby.
    using var h = new Harness();
    h.Sleep.IsSleeping = true;
    h.Sleep.SetSleepScreenVisible(true);

    h.Encoders.RaiseTurn(0, 1);
    h.Encoders.RaiseTurn(0, 1);
    h.Encoders.RaiseTurn(0, 1);

    Assert.Equal(3, h.Hud.Published.Count);
    Assert.Equal(0, h.Sleep.WakeCalls);
  }

  [Theory]
  [InlineData(0, "VOLUME")]
  [InlineData(1, "SOURCE")]
  [InlineData(2, "PRESETS")]
  [InlineData(3, "TUNING")]
  public void TheFourDispatchArraysAgreeInOrder(int index, string expectedLabel)
  {
    // The ENC-5 / ENC-7 remap reorders _turnHandlers and _pressHandlers. _currentValuePublishers is
    // the fourth array beside them; a remap that reorders three of four is the exact failure this
    // pins - it would put a TUNING readout on the SOURCE band with nothing else disagreeing.
    //
    // The LABEL is what pins the order, not the index. Every PublishCurrent* method takes the index
    // as a parameter and threads it straight into the card, so EncoderIndex reads correctly no
    // matter which publisher ran - asserting only that would pass against a fully shuffled array
    // and catch nothing but a length mismatch. The expectations below are deliberately the same
    // four labels EncoderTurn_PublishesACardLabelledForThatKnob pins for the acting path, because
    // the consumed readout answering a different word than the knob's own handler is the defect.
    using var h = new Harness();

    Assert.Equal(FrontPanelGeometry.EncoderCount, h.Router.Mapping.Count);

    // A tuner is active so index 3 takes its radio branch; without one it publishes TRACK, which is
    // the no-radio fallback rather than this knob's identity.
    h.WithActiveRadio();
    h.Sleep.IsSleeping = true;
    h.Sleep.SetSleepScreenVisible(true);

    h.Encoders.RaiseTurn(index, 1);

    var card = Assert.Single(h.Hud.Published);
    Assert.Equal(index, card.EncoderIndex);
    Assert.Equal(expectedLabel, card.Label);
  }
}
