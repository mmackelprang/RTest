using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Interfaces.Input;
using Radio.Core.Models.Audio;
using Radio.Infrastructure.Audio.Services;
using Radio.Infrastructure.Platform.Input;

namespace Radio.Infrastructure.Tests.Platform.Input;

/// <summary>
/// Covers ENC-8 Task 3: the router publishes the table it dispatches through.
///
/// <para>
/// The Settings page renders <see cref="RotaryEncoderActionRouter.Mapping"/>. Before this it
/// rendered hand-typed HTML mirroring a <c>switch</c>, which drifted from the code once already.
/// These tests exist to keep the array and the dispatch the same thing rather than two things that
/// happen to agree today.
/// </para>
/// </summary>
public class RotaryEncoderMappingTableTests
{
  private sealed class FakeRotaryEncoderService : IRotaryEncoderService
  {
    public bool IsConnected { get; set; } = true;
    public RotaryEncoderConfigStatus ConfigStatus { get; set; } = RotaryEncoderConfigStatus.Configured;

    public event EventHandler<EncoderTurnedEventArgs>? EncoderTurned;
    public event EventHandler<EncoderButtonEventArgs>? ButtonPressed;
#pragma warning disable CS0067 // Nothing in these tests raises a connection or config-tier change.
    public event EventHandler<EncoderConnectionEventArgs>? ConnectionChanged;
    public event EventHandler<EncoderConfigStatusEventArgs>? ConfigStatusChanged;
#pragma warning restore CS0067

    public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public void Dispose() { }

    public void RaiseTurn(int encoderIndex, int delta) =>
      EncoderTurned?.Invoke(this, new EncoderTurnedEventArgs { EncoderIndex = encoderIndex, Delta = delta });

    public void RaiseButton(int encoderIndex, bool isPressed) =>
      ButtonPressed?.Invoke(this, new EncoderButtonEventArgs { EncoderIndex = encoderIndex, IsPressed = isPressed });
  }

  private sealed class NullHudSink : IEncoderFeedbackSink
  {
#pragma warning disable CS0067 // Nothing subscribes in these tests.
    public event EventHandler<EncoderHudEventArgs>? Feedback;
#pragma warning restore CS0067

    public void Publish(EncoderHudEventArgs update) { }
  }

  /// <summary>Only the members the volume handler touches carry behaviour.</summary>
  private sealed class StubAudioManager : IAudioManager
  {
    public float MasterVolume { get; set; } = 0.5f;
    public bool IsMuted { get; set; }
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
      CancellationToken cancellationToken = default) => Task.FromResult<IAudioSource?>(null);

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

  private static RotaryEncoderActionRouter BuildRouter(
    FakeRotaryEncoderService? encoder = null,
    VisualizationModeService? viz = null) =>
    new(
      NullLogger<RotaryEncoderActionRouter>.Instance,
      encoder ?? new FakeRotaryEncoderService(),
      () => new StubAudioManager(),
      viz ?? new VisualizationModeService(NullLogger<VisualizationModeService>.Instance),
      new StaticOptionsMonitor<RotaryEncoderOptions>(new RotaryEncoderOptions()),
      new NullHudSink(),
      // ENC-5. The router owns the SOURCE overlay now; these tests never turn encoder 1, so a
      // selector wired to stubs is enough to construct one.
      new SourceSelectorService(
        NullLogger<SourceSelectorService>.Instance,
        () => new StubAudioManager(),
        () => new StubBandMemory(),
        new NullHudSink()));

  /// <summary>Nothing here commits a band, so the memory only has to exist.</summary>
  private sealed class StubBandMemory : IRadioBandMemory
  {
    public Task<Frequency?> GetAsync(RadioBand band, CancellationToken cancellationToken = default) =>
      Task.FromResult<Frequency?>(null);

    public Task SetAsync(RadioBand band, Frequency frequency, CancellationToken cancellationToken = default) =>
      Task.CompletedTask;
  }

  [Fact]
  public void Mapping_CoversEveryEncoderExactlyOnce_InIndexOrder()
  {
    // The API projects this array positionally; a gap or a duplicate would silently mislabel a knob.
    using var router = BuildRouter();
    Assert.Equal(RotaryEncoderDeviceConfig.EncoderCount, router.Mapping.Count);
    for (int i = 0; i < router.Mapping.Count; i++)
    {
      Assert.Equal(i, router.Mapping[i].EncoderIndex);
    }
  }

  [Fact]
  public void Mapping_DescribesEveryKnob_WithNoPlaceholderText()
  {
    using var router = BuildRouter();
    Assert.All(router.Mapping, m =>
    {
      Assert.False(string.IsNullOrWhiteSpace(m.TurnDescription));
      Assert.False(string.IsNullOrWhiteSpace(m.PressDescription));
      Assert.DoesNotContain("TODO", m.TurnDescription, StringComparison.OrdinalIgnoreCase);
      Assert.DoesNotContain("TODO", m.PressDescription, StringComparison.OrdinalIgnoreCase);
    });
  }

  [Fact]
  public void TurningAnEncoderDispatchesThroughTheSameTableTheUiRenders()
  {
    // The point of the table. If dispatch ever stops going through Mapping, this fails.
    var encoder = new FakeRotaryEncoderService();
    var viz = new VisualizationModeService(NullLogger<VisualizationModeService>.Instance);
    int vizModeCycleCount = 0;
    viz.ModeChanged += (_, _) => vizModeCycleCount++;
    using var router = BuildRouter(encoder, viz);

    // ENC-5 moved the visualiser from index 3 to index 2 (index 3 is now TUNING). The knob under
    // test is whichever one the visualiser is on, because it is the handler with an observable
    // side effect that needs no hardware.
    encoder.RaiseTurn(encoderIndex: 2, delta: 1);

    Assert.Equal("Cycle visualization mode", router.Mapping[2].TurnDescription);
    Assert.Equal(1, vizModeCycleCount);   // the handler the table points at actually ran
  }

  [Fact]
  public void PressingAnEncoderDispatchesThroughTheSameTableTheUiRenders()
  {
    var encoder = new FakeRotaryEncoderService();
    var viz = new VisualizationModeService(NullLogger<VisualizationModeService>.Instance);
    bool enabledBefore = viz.IsEnabled;
    using var router = BuildRouter(encoder, viz);

    // Short press = press edge then release edge; the gesture fires the short action on release.
    encoder.RaiseButton(2, isPressed: true);
    encoder.RaiseButton(2, isPressed: false);

    Assert.Equal("Visualization on / off", router.Mapping[2].PressDescription);
    Assert.NotEqual(enabledBefore, viz.IsEnabled);
  }
}
