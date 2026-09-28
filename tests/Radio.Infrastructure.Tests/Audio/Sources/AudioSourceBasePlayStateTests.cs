using Microsoft.Extensions.Logging;
using Moq;
using Radio.Core.Interfaces.Audio;
using Radio.Infrastructure.Audio.Sources;
using Radio.Infrastructure.Audio.Sources.Events;
using Radio.Infrastructure.Tests.Audio;
using Xunit;

namespace Radio.Infrastructure.Tests.Audio.Sources;

/// <summary>
/// Pins TTS-5: <see cref="AudioSourceBase.PlayAsync"/> must not overwrite a terminal state that
/// <c>PlayCoreAsync</c> reached while it ran — and must still promote to Playing in every case where
/// it did before.
/// </summary>
/// <remarks>
/// MUTATION for every test in the first section: replace
/// <c>PromoteToPlayingUnlessTerminatedSince(versionBeforePlay)</c> with the old
/// <c>State = AudioSourceState.Playing</c> and they go red reading <c>Playing</c>. The second section
/// is the negative control: it stays green under that mutation, and must, because it pins the
/// behaviour the fix is required to keep.
/// </remarks>
public class AudioSourceBasePlayStateTests
{
  // --- A terminal state reached during PlayCoreAsync is kept ---

  [Fact]
  public async Task TtsWhosePlaybackFailsBeforeItsFirstAwaitStaysInError()
  {
    // ⭐ THE ROW'S OWN INSTANCE, reproduced through the real classes. A device-less playback service
    // returns false from PlayStreamAsync without awaiting, so the monitor task — started inside
    // PlayCoreAsync — sets Error and raises PlaybackCompleted(Error) before PlayCoreAsync returns.
    // The base class then used to assign Playing over it.
    var tts = new TTSEventSource(
      "hello", new TTSParameters(), new MemoryStream(new byte[1000]), TimeSpan.FromSeconds(1),
      Mock.Of<ILogger<TTSEventSource>>(), DeviceLessPlaybackService.Create());
    PlaybackCompletionReason? reason = null;
    tts.PlaybackCompleted += (_, e) => reason = e.Reason;

    await tts.PlayAsync();

    Assert.Equal(PlaybackCompletionReason.Error, reason);
    Assert.Equal(AudioSourceState.Error, tts.State);
  }

  [Fact]
  public async Task TtsThatFinishesBeforePlayCoreAsyncReturnsStaysStopped()
  {
    // The same shape reached through the other background task: with no playback service a
    // zero-length utterance "plays" by Task.Delay(0), which completes synchronously and sets Stopped.
    var tts = new TTSEventSource(
      "hello", new TTSParameters(), new MemoryStream(new byte[1000]), TimeSpan.Zero,
      Mock.Of<ILogger<TTSEventSource>>());

    await tts.PlayAsync();

    Assert.Equal(AudioSourceState.Stopped, tts.State);
  }

  [Fact]
  public async Task AudioFileEventWhosePlaybackFailsBeforeItsFirstAwaitStaysInError()
  {
    // The sibling event source has the identical shape; the fix is in the base, so it covers both.
    var source = new AudioFileEventSource(
      "chime", new MemoryStream(new byte[1000]), TimeSpan.FromSeconds(1),
      Mock.Of<ILogger<AudioFileEventSource>>(), DeviceLessPlaybackService.Create());

    await source.PlayAsync();

    Assert.Equal(AudioSourceState.Error, source.State);
  }

  [Theory]
  [InlineData(AudioSourceState.Error)]
  [InlineData(AudioSourceState.Stopped)]
  public async Task ATerminalStateSetInsidePlayCoreIsNotOverwritten(AudioSourceState terminal)
  {
    var source = new ScriptedSource(core: s => s.ForceState(terminal));
    source.ForceState(AudioSourceState.Ready);

    await source.PlayAsync();

    Assert.Equal(terminal, source.State);
  }

  // --- Negative control: every case that promoted before still promotes ---

  [Fact]
  public async Task ASourceReplayedFromStoppedStillBecomesPlaying()
  {
    // Why the fix compares a VERSION rather than reading the state: a source that was Stopped
    // BEFORE PlayAsync, and whose PlayCoreAsync leaves the state alone, must still become Playing.
    // A naive "don't promote from Stopped" would break every replay.
    var source = new ScriptedSource(core: _ => { });
    source.ForceState(AudioSourceState.Stopped);

    await source.PlayAsync();

    Assert.Equal(AudioSourceState.Playing, source.State);
  }

  [Fact]
  public async Task ANonTerminalStateSetInsidePlayCoreIsStillOverwrittenAsBefore()
  {
    // Deliberately unchanged behaviour: only Stopped/Error/Disposed are protected.
    var source = new ScriptedSource(core: s => s.ForceState(AudioSourceState.Ready));
    source.ForceState(AudioSourceState.Ready);

    await source.PlayAsync();

    Assert.Equal(AudioSourceState.Playing, source.State);
  }

  [Fact]
  public async Task ATerminalStateReachedAndLeftInsidePlayCoreStillPromotes()
  {
    // Error then Ready inside PlayCoreAsync (a retry that recovered): the state PlayCoreAsync LEFT is
    // non-terminal, so the source is promoted. The version moved; the terminal state did not stick.
    var source = new ScriptedSource(core: s =>
    {
      s.ForceState(AudioSourceState.Error);
      s.ForceState(AudioSourceState.Ready);
    });
    source.ForceState(AudioSourceState.Ready);

    await source.PlayAsync();

    Assert.Equal(AudioSourceState.Playing, source.State);
  }

  [Fact]
  public async Task PromotionRaisesStateChangedExactlyOnce()
  {
    var source = new ScriptedSource(core: _ => { });
    source.ForceState(AudioSourceState.Ready);
    var raised = new List<AudioSourceState>();
    source.StateChanged += (_, e) => raised.Add(e.NewState);

    await source.PlayAsync();

    Assert.Equal(new[] { AudioSourceState.Playing }, raised);
  }

  private sealed class ScriptedSource : AudioSourceBase
  {
    private readonly Action<ScriptedSource> _core;

    public ScriptedSource(Action<ScriptedSource> core) : base(Mock.Of<ILogger>())
    {
      _core = core;
    }

    public override string Name => "Scripted";

    public override AudioSourceType Type => AudioSourceType.TestTone;

    public override AudioSourceCategory Category => AudioSourceCategory.Primary;

    public override object GetSoundComponent() => new();

    public void ForceState(AudioSourceState state) => State = state;

    protected override Task PlayCoreAsync(CancellationToken cancellationToken)
    {
      _core(this);
      return Task.CompletedTask;
    }

    protected override Task StopCoreAsync(CancellationToken cancellationToken) => Task.CompletedTask;
  }
}
