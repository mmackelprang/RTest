using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Radio.Web.Models;
using Radio.Web.Services;
using Radio.Web.Services.Hub;
using Radio.Web.Tests.TestHelpers;

namespace Radio.Web.Tests.Services;

/// <summary>
/// AudioStateStore's multicast notification contract (queue row `UI-6`): every subscriber runs, every
/// subscriber is awaited to completion, and each one's exception is caught on its own.
/// </summary>
/// <remarks>
/// ⚠ NOT ONE ASSERTION HERE USES A WALL CLOCK, and that is deliberate — `CLAUDE.md` § *Test Timing*
/// forbids racing production code's progress against a `Task.Delay` in the test. Every test below
/// synchronizes on an OBSERVATION instead:
///
/// - the "is it awaited" tests read <c>Task.IsCompleted</c> on the notify task at a point where its
///   value is decided by control flow rather than by timing. A subscriber parked on a
///   TaskCompletionSource the test has not yet completed CANNOT proceed, so "the notify task is still
///   incomplete" is a fact about the code, not about how fast the machine is;
/// - the "did it run" tests assert on flags written before the handler returns, after awaiting the
///   notify task to completion.
///
/// The obvious alternative — three handlers that <c>await Task.Yield()</c> and a check that all three
/// recorded — is exactly the race this comment exists to rule out: on the unfixed code the discarded
/// continuations usually complete anyway, so it would have passed against the bug most of the time.
///
/// ⚠ The broadcasts are driven by calling the store's own <c>OnHub*</c> handlers, which are internal
/// for that reason (Radio.Web.csproj declares InternalsVisibleTo("Radio.Web.Tests")) — a field-like
/// event cannot be raised from outside the type that declares it. The hub service is constructed over
/// <see cref="OfflineHubTransport"/> and never started, so nothing here opens a socket.
/// </remarks>
public class AudioStateStoreNotifyTests
{
  private static AudioStateStore NewStore() =>
    new(
      NullLogger<AudioStateStore>.Instance,
      new AudioStateHubService(
        NullLogger<AudioStateHubService>.Instance,
        new ConfigurationBuilder().Build(),
        transport: new OfflineHubTransport()));

  private static AudioStateStore NewStore(List<(LogLevel Level, string Message)> sink) =>
    new(
      new CapturingLogger<AudioStateStore>(sink),
      new AudioStateHubService(
        NullLogger<AudioStateHubService>.Instance,
        new ConfigurationBuilder().Build(),
        transport: new OfflineHubTransport()));

  private static VolumeDto Volume(float volume = 0.5f) => new(volume, false);

  private static RadioStateDto RadioState(double frequency = 98.5) =>
    new(frequency, "FM", 0.2, null, false, null, 0, null, false, null, null);

  // --- The headline: every subscriber is AWAITED, not merely started -------------------------

  /// <summary>
  /// THE discriminating test for the parameterless overload. A subscriber registered FIRST parks on a
  /// gate; a fast one registered LAST returns a completed task.
  /// </summary>
  /// <remarks>
  /// Before `UI-6`, <c>await handler.Invoke()</c> returned only the LAST subscriber's task — which is
  /// already complete here — so the notify task completed while the first subscriber was still parked.
  /// This test asserts the opposite, and it is the assertion that fails if the GetInvocationList loop
  /// is reverted. Nothing here depends on elapsed time: the gate cannot open until the test opens it.
  /// </remarks>
  [Fact]
  public async Task NotifyDoesNotCompleteWhileAnEarlierSubscriberIsStillRunning()
  {
    var store = NewStore();
    var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var slowFinished = false;
    var fastRan = false;

    store.VolumeChanged += async () => { await gate.Task; slowFinished = true; };
    store.VolumeChanged += () => { fastRan = true; return Task.CompletedTask; };

    var notify = store.OnHubVolumeChanged(Volume());

    Assert.False(notify.IsCompleted);
    Assert.False(slowFinished);
    Assert.False(fastRan); // sequential: the second subscriber has not been reached yet

    gate.SetResult();
    await notify;

    Assert.True(slowFinished);
    Assert.True(fastRan);
  }

  /// <summary>The same discriminator for the payload-carrying overload, via RadioStateChanged.</summary>
  [Fact]
  public async Task NotifyWithPayloadDoesNotCompleteWhileAnEarlierSubscriberIsStillRunning()
  {
    var store = NewStore();
    var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var slowFinished = false;

    store.RadioStateChanged += async _ => { await gate.Task; slowFinished = true; };
    store.RadioStateChanged += _ => Task.CompletedTask;

    var notify = store.OnHubRadioStateChanged(RadioState());

    Assert.False(notify.IsCompleted);

    gate.SetResult();
    await notify;

    Assert.True(slowFinished);
  }

  /// <summary>And for SleepStateChanged, the site that had no try/catch at all.</summary>
  [Fact]
  public async Task NotifySleepStateDoesNotCompleteWhileAnEarlierSubscriberIsStillRunning()
  {
    var store = NewStore();
    var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var slowFinished = false;

    store.SleepStateChanged += async _ => { await gate.Task; slowFinished = true; };
    store.SleepStateChanged += _ => Task.CompletedTask;

    var notify = store.OnHubSleepStateChanged(true);

    Assert.False(notify.IsCompleted);

    gate.SetResult();
    await notify;

    Assert.True(slowFinished);
  }

  // --- The sharper half: a synchronous throw must not starve the rest of the list -------------

  /// <summary>
  /// A subscriber that throws BEFORE its first await used to propagate straight out of
  /// <c>Delegate.Invoke</c>, so every handler registered after it never ran. That is starvation, not a
  /// lost log line, and it is the half of `UI-6` that costs behaviour rather than diagnostics.
  /// </summary>
  [Fact]
  public async Task ASubscriberThrowingSynchronouslyDoesNotStarveTheOnesAfterIt()
  {
    var store = NewStore();
    var laterRan = false;

    store.VolumeChanged += () => throw new InvalidOperationException("synchronous boom");
    store.VolumeChanged += () => { laterRan = true; return Task.CompletedTask; };

    await store.OnHubVolumeChanged(Volume());

    Assert.True(laterRan);
  }

  /// <summary>
  /// The OTHER half of the defect — the one the row leads with. A subscriber that faults
  /// ASYNCHRONOUSLY, after its first await, faulted a Task nobody held, so its exception reached no
  /// log at all.
  /// </summary>
  /// <remarks>
  /// ⚠ THE LOG ASSERTION IS THE DISCRIMINATOR HERE, and an earlier draft of this test got that wrong
  /// in a way worth recording. It asserted only that the LATER subscriber still ran, under the name
  /// "...DoesNotStarveTheOnesAfterIt" — but an asynchronous fault never starved anything: the old
  /// <c>Invoke</c> had already moved on to the next subscriber by the time this one faulted, so that
  /// assertion held against the BUG too. It passed the mutation check for the wrong reason, and its
  /// name and summary claimed a property it did not test. Starvation is the SYNCHRONOUS case, which
  /// <see cref="ASubscriberThrowingSynchronouslyDoesNotStarveTheOnesAfterIt"/> covers.
  ///
  /// What actually distinguishes fixed from unfixed for an async fault is whether anyone was holding
  /// the Task when it faulted — i.e. whether the exception was logged. That is asserted below, and it
  /// is what makes this test fail against the unfixed code.
  /// </remarks>
  [Fact]
  public async Task AnAsynchronousFaultIsLoggedRatherThanDiscardedUnobserved()
  {
    var sink = new List<(LogLevel Level, string Message)>();
    var store = NewStore(sink);
    var laterRan = false;

    store.VolumeChanged += async () =>
    {
      await Task.Yield();
      throw new InvalidOperationException("asynchronous boom");
    };
    store.VolumeChanged += () => { laterRan = true; return Task.CompletedTask; };

    await store.OnHubVolumeChanged(Volume());

    Assert.True(laterRan);
    Assert.Equal(1, sink.Count(e => e.Level == LogLevel.Warning));
  }

  [Fact]
  public async Task ASleepSubscriberThrowingSynchronouslyDoesNotStarveTheOnesAfterIt()
  {
    var store = NewStore();
    var laterRan = false;

    store.SleepStateChanged += _ => throw new InvalidOperationException("synchronous boom");
    store.SleepStateChanged += _ => { laterRan = true; return Task.CompletedTask; };

    await store.OnHubSleepStateChanged(true);

    Assert.True(laterRan);
  }

  /// <summary>
  /// SleepStateChanged had NO try/catch before `UI-6`, so a throwing subscriber propagated out of the
  /// handler into the hub's dispatch. It must now be contained like every other event on this store.
  /// </summary>
  [Fact]
  public async Task ASleepSubscriberThrowingDoesNotPropagateToTheCaller()
  {
    var store = NewStore();

    store.SleepStateChanged += _ => throw new InvalidOperationException("synchronous boom");

    var thrown = await Record.ExceptionAsync(() => store.OnHubSleepStateChanged(true));

    Assert.Null(thrown);
  }

  // --- Each exception is caught individually, and every subscriber still runs -----------------

  /// <summary>
  /// The full property in one test: with three throwing subscribers interleaved with three good ones,
  /// all six run and all three exceptions are logged. Before `UI-6` the first synchronous throw ended
  /// the list and exactly one warning could ever be logged per raise.
  /// </summary>
  [Fact]
  public async Task EverySubscriberRunsAndEveryExceptionIsLoggedSeparately()
  {
    var sink = new List<(LogLevel Level, string Message)>();
    var store = NewStore(sink);
    var ran = new List<int>();

    for (var i = 0; i < 6; i++)
    {
      var index = i;
      store.VolumeChanged += () =>
      {
        ran.Add(index);
        if (index % 2 == 0)
        {
          throw new InvalidOperationException($"boom {index}");
        }

        return Task.CompletedTask;
      };
    }

    await store.OnHubVolumeChanged(Volume());

    Assert.Equal(new[] { 0, 1, 2, 3, 4, 5 }, ran);
    Assert.Equal(3, sink.Count(e => e.Level == LogLevel.Warning));
  }

  /// <summary>
  /// Registration order is preserved. Asserted because the GetInvocationList loop is what makes order
  /// observable at all — before it, only the last subscriber's completion was ever awaited.
  /// </summary>
  [Fact]
  public async Task SubscribersRunInRegistrationOrder()
  {
    var store = NewStore();
    var order = new List<string>();

    store.VolumeChanged += async () => { await Task.Yield(); order.Add("first"); };
    store.VolumeChanged += async () => { await Task.Yield(); order.Add("second"); };
    store.VolumeChanged += async () => { await Task.Yield(); order.Add("third"); };

    await store.OnHubVolumeChanged(Volume());

    Assert.Equal(new[] { "first", "second", "third" }, order);
  }

  /// <summary>An event with no subscribers is a no-op rather than a null dereference.</summary>
  [Fact]
  public async Task AnEventWithNoSubscribersIsANoOp()
  {
    var store = NewStore();

    await store.OnHubVolumeChanged(Volume());
    await store.OnHubRadioStateChanged(RadioState());
    await store.OnHubSleepStateChanged(true);

    Assert.Equal(0.5f, store.Volume);
  }

  /// <summary>
  /// The cached state a broadcast writes is still written when a subscriber throws — the catch is
  /// around the notification, not around the assignment.
  /// </summary>
  [Fact]
  public async Task CachedStateIsStillWrittenWhenASubscriberThrows()
  {
    var store = NewStore();
    store.RadioStateChanged += _ => throw new InvalidOperationException("boom");

    await store.OnHubRadioStateChanged(RadioState(101.1));

    Assert.NotNull(store.RadioState);
    Assert.Equal(101.1, store.RadioState!.Frequency);
  }
}
