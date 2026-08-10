using Microsoft.Extensions.Logging;
using Moq;
using Radio.Core.Interfaces.Audio;
using Radio.Infrastructure.Audio.Sources;
using Xunit;

namespace Radio.Infrastructure.Tests.Audio.Sources;

/// <summary>
/// Pins <see cref="AudioSourceBase.StopAsync"/>'s teardown contract.
///
/// The guard used to be <c>State != Playing &amp;&amp; State != Paused → return</c>,
/// which made detaching a source's audio component conditional on a state flag
/// that other code can move independently. Sources attach their sound component
/// from async connect, late-acquire, and stall-recovery paths that are decoupled
/// from the state machine, so a source can be audible while reading Ready — and
/// the old guard then skipped the only code that removes it from the mixer,
/// leaving two sources playing at once after a switch.
///
/// These tests drive the state explicitly rather than reproducing any one
/// source's route into it: the contract is that teardown does not depend on
/// state, so it must hold no matter how the state got there.
/// </summary>
public class AudioSourceBaseStopTests
{
  [Theory]
  [InlineData(AudioSourceState.Ready)]
  [InlineData(AudioSourceState.Stopped)]
  [InlineData(AudioSourceState.Error)]
  [InlineData(AudioSourceState.Initializing)]
  [InlineData(AudioSourceState.Playing)]
  [InlineData(AudioSourceState.Paused)]
  public async Task StopAsync_SourceHoldingAttachedComponent_AlwaysTearsDown(AudioSourceState state)
  {
    var source = new FakeAudioSource();
    source.AttachComponent();
    source.ForceState(state);

    await source.StopAsync();

    Assert.Equal(1, source.StopCoreCallCount);
    Assert.False(source.HasAttachedComponent);
    Assert.Equal(AudioSourceState.Stopped, source.State);
  }

  [Fact]
  public async Task StopAsync_CreatedState_SkipsTeardown()
  {
    // Nothing has been built yet — there is genuinely nothing to detach, and
    // running teardown would be pure overhead.
    var source = new FakeAudioSource();

    await source.StopAsync();

    Assert.Equal(0, source.StopCoreCallCount);
    Assert.Equal(AudioSourceState.Created, source.State);
  }

  [Fact]
  public async Task StopAsync_AfterDispose_Throws()
  {
    // Disposed sources still fail loudly rather than silently running teardown
    // against torn-down state.
    var source = new FakeAudioSource();
    source.AttachComponent();
    source.ForceState(AudioSourceState.Playing);

    await source.DisposeAsync();

    await Assert.ThrowsAsync<ObjectDisposedException>(() => source.StopAsync());
  }

  [Fact]
  public async Task StopAsync_CalledTwice_IsIdempotent()
  {
    // The widened guard means the second call now reaches StopCoreAsync. Every
    // StopCoreAsync implementation is null-guarded, so this must be harmless.
    var source = new FakeAudioSource();
    source.AttachComponent();
    source.ForceState(AudioSourceState.Playing);

    await source.StopAsync();
    await source.StopAsync();

    Assert.Equal(2, source.StopCoreCallCount);
    Assert.False(source.HasAttachedComponent);
    Assert.Equal(AudioSourceState.Stopped, source.State);
  }

  [Fact]
  public async Task StopAsync_ReadyStateSource_LeavesNoStaleComponentForTheNextSource()
  {
    // The switch-away scenario, expressed against the guard itself: a source
    // sitting in Ready while still wired into the mixer must not survive a stop.
    var outgoing = new FakeAudioSource();
    outgoing.AttachComponent();
    outgoing.ForceState(AudioSourceState.Ready);

    await outgoing.StopAsync();

    Assert.False(outgoing.HasAttachedComponent);
  }

  [Fact]
  public async Task StopAsync_StopCoreThrows_PropagatesAndLeavesStateUnchanged()
  {
    // The widened guard runs StopCoreAsync in more states, so it is worth
    // pinning what a throwing teardown does: the exception propagates and the
    // state is NOT advanced to Stopped. Real implementations can throw here —
    // SDRRadioAudioSource's ShutdownAsync and FilePlayerAudioSource's
    // playback-service stop are not individually try/caught.
    var source = new FakeAudioSource { ThrowOnStop = true };
    source.AttachComponent();
    source.ForceState(AudioSourceState.Ready);

    await Assert.ThrowsAsync<InvalidOperationException>(() => source.StopAsync());

    Assert.Equal(AudioSourceState.Ready, source.State);
  }

  /// <summary>
  /// Minimal AudioSourceBase implementation. "HasAttachedComponent" stands in
  /// for the real sources' mixer registration (playbackId + sound generator),
  /// which only StopCoreAsync clears.
  /// </summary>
  private sealed class FakeAudioSource : AudioSourceBase
  {
    public FakeAudioSource()
      : base(new Mock<ILogger>().Object)
    {
    }

    public int StopCoreCallCount { get; private set; }

    public bool HasAttachedComponent { get; private set; }

    public bool ThrowOnStop { get; init; }

    public override string Name => "Fake";

    public override AudioSourceType Type => AudioSourceType.TestTone;

    public override AudioSourceCategory Category => AudioSourceCategory.Primary;

    public override object GetSoundComponent() => new();

    public void AttachComponent() => HasAttachedComponent = true;

    /// <summary>
    /// Moves the state machine directly, mirroring how real sources end up in a
    /// state that does not match what is actually attached.
    /// </summary>
    public void ForceState(AudioSourceState state) => State = state;

    protected override Task PlayCoreAsync(CancellationToken cancellationToken)
    {
      HasAttachedComponent = true;
      return Task.CompletedTask;
    }

    protected override Task StopCoreAsync(CancellationToken cancellationToken)
    {
      StopCoreCallCount++;
      if (ThrowOnStop)
      {
        throw new InvalidOperationException("teardown failed");
      }
      HasAttachedComponent = false;
      return Task.CompletedTask;
    }
  }
}
