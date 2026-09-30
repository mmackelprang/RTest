using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;
using Radio.Infrastructure.Audio.Services;
using Radio.Infrastructure.Audio.Sources.Events;

namespace Radio.Infrastructure.Tests.Audio.Services;

/// <summary>
/// AUD-73: a newer announcement replaces the one speaking; it does not play on top of it.
/// </summary>
/// <remarks>
/// <para>
/// Nothing here races a clock. "The first announcement is speaking" is a signal raised from inside its
/// <c>PlayAsync</c>, and each announcement's end is awaited, not slept for. The one wall-clock value,
/// <see cref="HangGuard"/>, only bounds how long a BROKEN build takes to fail: in the passing case
/// every awaited task has already completed or completes without waiting on time. Under starvation it
/// can only make a correct build slower, never red.
/// </para>
/// <para>
/// Before the fix, <c>SetActiveSource</c> cancelled a token that nothing awaited. Measured on the box
/// on <c>af9bc2b</c>: two requests 1.5 s apart both returned "completed", with two events ducked at
/// once.
/// </para>
/// </remarks>
public class AnnouncementServicePreemptionTests
{
  private static readonly TimeSpan HangGuard = TimeSpan.FromSeconds(30);

  private readonly List<string> _duckLog = [];
  private readonly Mock<IDuckingService> _ducking = new();

  // Set by a test that needs to know, at the instant a later announcement registers its duck,
  // whether the earlier one had already been told to stop.
  private FakeSource? _earlier;
  private bool? _earlierCancelledWhenLaterDucked;

  public AnnouncementServicePreemptionTests()
  {
    _ducking
      .Setup(d => d.StartDuckingAsync(It.IsAny<IEventAudioSource>(), It.IsAny<CancellationToken>()))
      .Returns<IEventAudioSource, CancellationToken>((s, _) =>
      {
        lock (_duckLog) { _duckLog.Add("start " + s.Id); }
        if (_earlier != null && s.Id != _earlier.Object.Id)
        {
          _earlierCancelledWhenLaterDucked = _earlier.PlayToken.IsCancellationRequested;
        }
        return Task.CompletedTask;
      });
    _ducking
      .Setup(d => d.StopDuckingAsync(It.IsAny<IEventAudioSource>(), It.IsAny<CancellationToken>()))
      .Returns<IEventAudioSource, CancellationToken>((s, _) =>
      {
        lock (_duckLog) { _duckLog.Add("stop " + s.Id); }
        return Task.CompletedTask;
      });
  }

  [Fact]
  public async Task ASecondAnnouncementInterruptsTheFirst()
  {
    // MUTATION: make BecomeActive not cancel the replaced CTS (the pre-fix behaviour, in effect) and
    // the first announcement never returns — the HangGuard fires.
    var first = new FakeSource("first", completesOnPlay: null);
    var second = new FakeSource("second", completesOnPlay: PlaybackCompletionReason.EndOfContent);
    var service = CreateService(FactoryReturning(first.Object, second.Object));

    var firstTask = service.AnnounceAsync("the first, still speaking");
    await first.Playing.WaitAsync(HangGuard);

    var secondOutcome = await service.AnnounceAsync("the second").WaitAsync(HangGuard);
    var firstOutcome = await firstTask.WaitAsync(HangGuard);

    Assert.Equal(AnnouncementOutcome.Completed, secondOutcome);
    Assert.Equal(AnnouncementOutcome.Interrupted, firstOutcome);
    // Interrupted is not enough on its own: the first voice must actually be stopped.
    first.Mock.Verify(s => s.StopAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce);
  }

  [Fact]
  public async Task TheDuckIsHeldAcrossTheHandover()
  {
    // AUD-74 must not regress: the second announcement registers its duck BEFORE the first releases
    // its own, so the active-event count never touches zero between the two voices.
    // The ORDER in the log is only as good as the thread timing that produced it, so the decisive
    // check is the state at the instant the second duck registers: the first must not yet have been
    // told to stop. MUTATION: move StartDuckingAsync after BecomeActive — the first's token is then
    // already cancelled when the second ducks, deterministically, and this goes red.
    var first = new FakeSource("first", completesOnPlay: null);
    var second = new FakeSource("second", completesOnPlay: null);
    _earlier = first;
    var service = CreateService(FactoryReturning(first.Object, second.Object));

    var firstTask = service.AnnounceAsync("the first");
    await first.Playing.WaitAsync(HangGuard);

    var secondTask = service.AnnounceAsync("the second");
    await second.Playing.WaitAsync(HangGuard);
    await firstTask.WaitAsync(HangGuard);

    Assert.False(_earlierCancelledWhenLaterDucked, "the second must duck before the first is stopped");
    string[] log;
    lock (_duckLog) { log = [.. _duckLog]; }
    Assert.Equal(["start first", "start second", "stop first"], log);

    await service.StopAsync();
    Assert.Equal(AnnouncementOutcome.Interrupted, await secondTask.WaitAsync(HangGuard));
  }

  [Fact]
  public async Task AnOlderAnnouncementThatFinishesSynthesisingLateDoesNotCutOffANewerOne()
  {
    // "Newer" is the order requests ARRIVED in. The first request's TTS is held until the second
    // is already speaking; when it is released, the first must give way, not take over.
    // MUTATION: drop the `_activeArrival > arrival` check in BecomeActive — the first then plays
    // (Playing fires) and the second is interrupted; red on both counts.
    var releaseFirstSynthesis = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var first = new FakeSource("first", completesOnPlay: PlaybackCompletionReason.EndOfContent);
    var second = new FakeSource("second", completesOnPlay: null);

    var calls = 0;
    var factory = new Mock<ITTSFactory>();
    factory
      .Setup(f => f.CreateAsync(It.IsAny<string>(), It.IsAny<TTSParameters>(), It.IsAny<CancellationToken>()))
      .Returns<string, TTSParameters?, CancellationToken>(async (_, _, _) =>
      {
        if (Interlocked.Increment(ref calls) == 1)
        {
          await releaseFirstSynthesis.Task;
          return first.Object;
        }
        return second.Object;
      });
    var service = CreateService(factory);

    var firstTask = service.AnnounceAsync("a long message, slow to synthesise");
    var secondTask = service.AnnounceAsync("a short one, asked for later");
    await second.Playing.WaitAsync(HangGuard);

    releaseFirstSynthesis.SetResult();
    var firstOutcome = await firstTask.WaitAsync(HangGuard);

    Assert.Equal(AnnouncementOutcome.Interrupted, firstOutcome);
    Assert.False(first.Playing.IsCompleted, "the superseded announcement must never start playing");
    Assert.False(secondTask.IsCompleted, "the newer announcement must still be speaking");
    // Its duck registration is released, so it does not hold the music down after returning.
    lock (_duckLog) { Assert.Contains("stop first", _duckLog); }

    await service.StopAsync();
    Assert.Equal(AnnouncementOutcome.Interrupted, await secondTask.WaitAsync(HangGuard));
  }

  private Mock<ITTSFactory> FactoryReturning(params IEventAudioSource[] sources)
  {
    var queue = new Queue<IEventAudioSource>(sources);
    var factory = new Mock<ITTSFactory>();
    factory
      .Setup(f => f.CreateAsync(It.IsAny<string>(), It.IsAny<TTSParameters>(), It.IsAny<CancellationToken>()))
      .Returns(() =>
      {
        lock (queue) { return Task.FromResult(queue.Dequeue()); }
      });
    return factory;
  }

  private AnnouncementService CreateService(Mock<ITTSFactory> factory)
  {
    var options = new Mock<IOptionsMonitor<FilePlayerOptions>>();
    options.SetupGet(o => o.CurrentValue).Returns(new FilePlayerOptions());
    var fileFactory = new AudioFileEventSourceFactory(
      Mock.Of<ILogger<AudioFileEventSourceFactory>>(),
      Mock.Of<ILogger<AudioFileEventSource>>(),
      options.Object);

    return new AnnouncementService(
      Mock.Of<ILogger<AnnouncementService>>(),
      factory.Object,
      _ducking.Object,
      fileFactory);
  }

  /// <summary>
  /// An event source whose <c>PlayAsync</c> signals <see cref="Playing"/> and then either raises
  /// PlaybackCompleted with the given reason or, for null, keeps "speaking" until stopped. StopAsync
  /// raises UserStopped, as the real sources do.
  /// </summary>
  private sealed class FakeSource
  {
    private readonly TaskCompletionSource _playing = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public FakeSource(string id, PlaybackCompletionReason? completesOnPlay)
    {
      Mock.SetupGet(s => s.Id).Returns(id);
      Mock.SetupGet(s => s.Name).Returns("TTS event " + id);
      Mock
        .Setup(s => s.PlayAsync(It.IsAny<CancellationToken>()))
        .Returns<CancellationToken>(ct =>
        {
          PlayToken = ct;
          _playing.TrySetResult();
          if (completesOnPlay is { } reason)
          {
            Raise(reason);
          }
          return Task.CompletedTask;
        });
      Mock
        .Setup(s => s.StopAsync(It.IsAny<CancellationToken>()))
        .Returns(() =>
        {
          Raise(PlaybackCompletionReason.UserStopped);
          return Task.CompletedTask;
        });
    }

    public Mock<IEventAudioSource> Mock { get; } = new();

    public IEventAudioSource Object => Mock.Object;

    public Task Playing => _playing.Task;

    /// <summary>The token the service passed to PlayAsync — the one a newer announcement cancels.</summary>
    public CancellationToken PlayToken { get; private set; }

    private void Raise(PlaybackCompletionReason reason) =>
      Mock.Raise(s => s.PlaybackCompleted += null, Mock.Object,
        new AudioSourceCompletedEventArgs { SourceId = Mock.Object.Id, Reason = reason });
  }
}
