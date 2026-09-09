using Radio.Infrastructure.Audio.SoundFlow;

namespace Radio.Infrastructure.Tests.Audio.SoundFlow;

/// <summary>
/// Pins the <c>bool</c> contract AUD-2 added to <c>SetVolume</c>, <c>SetGainOffset</c>,
/// <c>SetDuckingMultiplier</c> and <c>ClearDuckingMultiplier</c>: true when a live player or
/// component actually received the value, false when the id matched nothing.
/// </summary>
/// <remarks>
/// ⚠ <b>THE FALSE DIRECTION ALONE WOULD BE A DECORATIVE GATE.</b> Every one of these methods returned
/// <c>void</c> before AUD-2 and every lookup in a device-less service misses, so a file asserting
/// only <c>Assert.False(service.SetGainOffset("no-such-source", …))</c> would pass against an
/// implementation that had hard-coded <c>return false</c> — and against the broken tree, and against
/// almost anything. Each pair below therefore asserts BOTH directions on the SAME call, with the
/// only difference being whether the key matches something registered.
///
/// ⚠ The true direction needs a registered component, which the production path
/// (<c>PlayComponentAsync</c>) cannot supply without a MiniAudio playback device — hence
/// <c>RegisterComponentForTests</c>, the kind-B seam. See its remarks for what that seam does not
/// cover.
/// </remarks>
public class SoundFlowPlaybackServiceVolumeReportingTests
{
  private const float Tolerance = 1e-4f;

  [Fact]
  public void SetGainOffsetReportsWhetherItReachedAnything()
  {
    var service = DeviceLessPlaybackService.Create();
    var component = NewComponent();
    service.RegisterComponentForTests("registered", component, baseVolume: 1.0f);

    Assert.False(service.SetGainOffset("no-such-source", 1.5f));
    Assert.True(service.SetGainOffset("registered", 1.5f));
    AssertVolume(1.5f, component);
  }

  [Fact]
  public void SetDuckingMultiplierReportsWhetherItReachedAnything()
  {
    var service = DeviceLessPlaybackService.Create();
    var component = NewComponent();
    service.RegisterComponentForTests("registered", component, baseVolume: 1.0f);

    Assert.False(service.SetDuckingMultiplier("no-such-source", 0.2f));
    Assert.True(service.SetDuckingMultiplier("registered", 0.2f));
    AssertVolume(0.2f, component);
  }

  [Fact]
  public void ClearDuckingMultiplierReportsWhetherItRestoredAnything()
  {
    // The ducking-ended path reads this to decide whether it may say volumeRestored=True
    // (AudioManager.OnDuckingStateChanged). A false return there means the source may be sitting at
    // its ducked volume, which is why the caller must not assert restoration unconditionally.
    var service = DeviceLessPlaybackService.Create();
    var component = NewComponent();
    service.RegisterComponentForTests("registered", component, baseVolume: 1.0f);
    service.SetDuckingMultiplier("registered", 0.2f);

    Assert.False(service.ClearDuckingMultiplier("no-such-source"));
    Assert.True(service.ClearDuckingMultiplier("registered"));
    AssertVolume(1.0f, component);
  }

  [Fact]
  public void SetVolumeReportsWhetherItReachedAnything()
  {
    var service = DeviceLessPlaybackService.Create();
    var component = NewComponent();
    service.RegisterComponentForTests("registered", component, baseVolume: 1.0f);

    Assert.False(service.SetVolume("no-such-source", 0.5f));
    Assert.True(service.SetVolume("registered", 0.5f));
    AssertVolume(0.5f, component);
  }

  [Fact]
  public void AGainOffsetStoredWhileNothingIsRegisteredAppliesWhenTheSourceStarts()
  {
    // ⭐ THIS IS WHY `applied == false` IS NOT AN ERROR AT THIS LAYER, and the reason AudioManager
    // rather than this class decides whether a miss is news. AudioManager applies the stored gain on
    // every source switch, and FilePlayer does not auto-play — so a switch to it legitimately stores
    // an offset with nothing yet registered. The offset is not lost: registration reads _gainOffsets
    // back. Asserted here as a VALUE (1.5, not 1.0) rather than as "it did not throw", which is what
    // the plan's draft of this test settled for and what would pass against a service that had
    // dropped the write entirely.
    var service = DeviceLessPlaybackService.Create();

    Assert.False(service.SetGainOffset("not-started-yet", 1.5f));

    var component = NewComponent();
    service.RegisterComponentForTests("not-started-yet", component, baseVolume: 1.0f);

    AssertVolume(1.5f, component);
  }

  [Fact]
  public void GetDiagnosticsReportsComponentKeysAndNotOnlyPlayerKeys()
  {
    // ⚠ AUD-2's queue row proposed GetDiagnostics as the way to compare live playback keys against
    // the IAudioSource.Id AudioManager holds. It returned _activePlayers.Keys ONLY, and every source
    // the row is about except FilePlayer registers as a COMPONENT — SDR radio, the three USB
    // sources, Bluetooth and TestTone — so that check came back empty and proved nothing. This pins
    // the half that was missing.
    var service = DeviceLessPlaybackService.Create();
    service.RegisterComponentForTests("Radio-abc", NewComponent());

    var diagnostics = service.GetDiagnostics();

    Assert.Equal(1, diagnostics.ActiveComponents);
    Assert.Contains("Radio-abc", diagnostics.ComponentIds);
    Assert.Empty(diagnostics.PlayerIds);
  }

  private static global::SoundFlow.Abstracts.SoundComponent NewComponent() =>
    new Moq.Mock<global::SoundFlow.Abstracts.SoundComponent>(
      Moq.MockBehavior.Loose, null!, default(global::SoundFlow.Structs.AudioFormat))
    { CallBase = true }.Object;

  private static void AssertVolume(float expected, global::SoundFlow.Abstracts.SoundComponent component) =>
    Assert.True(
      Math.Abs(component.Volume - expected) < Tolerance,
      $"expected Volume {expected:F4}, was {component.Volume:F4}.");
}
