using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Radio.Core.Configuration;
using Radio.Core.Interfaces;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Interfaces.Input;
using Radio.Core.Models.Audio;
using Radio.Infrastructure.Audio.Services;
using Radio.Infrastructure.Platform.Input;

namespace Radio.Infrastructure.Tests.Platform.Input;

/// <summary>
/// Covers <see cref="RotaryEncoderActionRouter"/>'s HUD publishing, the ENC-4b unmute-on-turn rule,
/// and the press/release split that ENC-4 introduces.
///
/// <para>
/// It also <b>pins the encoder-index to handler mapping</b>. The cabinet reads
/// VOLUME / SOURCE / PRESETS / TUNING while the router still maps 0=Volume, 1=Tuning, 2=Source,
/// 3=Visualization. That mismatch is deliberate — the remap belongs with ENC-5 and ENC-7, which
/// introduce the handlers it would point at — and pinning it here makes a later change a decision
/// somebody had to make rather than something that drifted.
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
    public bool IsSleeping { get; set; }
    public int EnterSleepCalls { get; private set; }
    public int WakeCalls { get; private set; }

    public Task EnterSleepAsync()
    {
      EnterSleepCalls++;
      return Task.CompletedTask;
    }

    public Task WakeAsync(string wakeSource = "unknown")
    {
      WakeCalls++;
      return Task.CompletedTask;
    }
  }

  /// <summary>
  /// Only the three members the router touches carry behaviour: <see cref="MasterVolume"/>,
  /// <see cref="IsMuted"/> and <see cref="ActiveSource"/>. <see cref="MuteWrites"/> exists so a test
  /// can tell "left alone" apart from "written back to the value it already had".
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
    public readonly FakeTimeProvider Time = new();
    public readonly RotaryEncoderActionRouter Router;

    public Harness()
    {
      Router = new RotaryEncoderActionRouter(
        NullLogger<RotaryEncoderActionRouter>.Instance,
        Encoders,
        () => Audio,
        new VisualizationModeService(NullLogger<VisualizationModeService>.Instance),
        new StaticOptionsMonitor<RotaryEncoderOptions>(new RotaryEncoderOptions()),
        Hud,
        Sleep,
        Time);
    }

    public IReadOnlyList<EncoderHudEventArgs> Cards(EncoderHudPhase phase) =>
      Hud.Published.Where(c => c.Phase == phase).ToList();

    public void Dispose() => Router.Dispose();
  }

  // --- The mapping, pinned -----------------------------------------------------------------

  [Fact]
  public void EncoderIndexZero_IsVolume_UnderBothTheOldAndTheNewPhysicalOrder()
  {
    // The one index the deliberate ENC-5/ENC-7 mismatch does not touch, and the only one with a
    // safety hazard behind it.
    Assert.Equal(0, RotaryEncoderConfigDefaults.VolumeEncoderIndex);
  }

  [Fact]
  public void TurnOnEachIndex_PublishesThePreEnc5HandlerLabels()
  {
    using var h = new Harness();

    h.Encoders.RaiseTurn(0, 1);
    h.Encoders.RaiseTurn(1, 1);
    h.Encoders.RaiseTurn(2, 1);
    h.Encoders.RaiseTurn(3, 1);

    // 0=Volume, 1=Tuning (TRACK here, because the fake's ActiveSource is not an IRadioControl),
    // 2=Source, 3=Visualization. Pinned so ENC-5/ENC-7's remap has to be deliberate.
    Assert.Equal(
      new[] { "VOLUME", "TRACK", "SOURCE", "VISUALIZER" },
      h.Cards(EncoderHudPhase.Value).Select(c => c.Label).ToArray());

    // Geometry keys off the encoder index, not off the handler, so the card is in the right
    // quarter even while indices 1-3 say the wrong word.
    Assert.Equal(new[] { 0, 1, 2, 3 }, h.Cards(EncoderHudPhase.Value).Select(c => c.EncoderIndex).ToArray());
  }

  // --- Task 5: every handler publishes -------------------------------------------------------

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

    h.Encoders.RaiseTurn(1, 1);

    var card = Assert.Single(h.Hud.Published);
    Assert.Equal("TRACK", card.Label);
    Assert.Equal("no track control on this source", card.SecondaryText);
  }

  [Fact]
  public void SourceTurn_PublishesTheSelectionWithoutSwitchingToIt()
  {
    using var h = new Harness();

    h.Encoders.RaiseTurn(2, 1);

    var card = Assert.Single(h.Hud.Published);
    Assert.Equal("SOURCE", card.Label);
    // Turning previews; the press commits. The card says so, and nothing was switched.
    Assert.Empty(h.Audio.GetOrCreateCalls);
  }

  // --- Task 6: ENC-4b ------------------------------------------------------------------------

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

  // --- Task 7: press/release and the long press ----------------------------------------------

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

  [Fact]
  public void SelectorLongPress_DoesNothing()
  {
    using var h = new Harness();

    h.Encoders.RaiseButton(2, true);
    h.Time.Advance(TimeSpan.FromMilliseconds(1000));
    h.Encoders.RaiseButton(2, false);

    // Encoder 2 has no long action wired, so nothing commits and no ring is promised.
    Assert.Empty(h.Audio.GetOrCreateCalls);
    Assert.Empty(h.Cards(EncoderHudPhase.HoldStart));
  }

  [Fact]
  public void HoldStart_IsPublishedForVolumeOnly()
  {
    using var h = new Harness();

    for (int index = 0; index < 4; index++)
    {
      h.Encoders.RaiseButton(index, true);
      h.Time.Advance(TimeSpan.FromMilliseconds(100));
      h.Encoders.RaiseButton(index, false);
    }

    var holdStarts = h.Cards(EncoderHudPhase.HoldStart);
    Assert.Equal(0, Assert.Single(holdStarts).EncoderIndex);
    Assert.Equal("HOLD FOR STANDBY", holdStarts[0].Label);
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
    Assert.Empty(h.Hud.Published);
  }
}
