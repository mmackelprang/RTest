using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Radio.Web.Services.Hub;
using Radio.Web.Tests.TestHelpers;

namespace Radio.Web.Tests.Services;

/// <summary>
/// AudioStateHubService's multicast notification contract (queue row `UI-7`): every subscriber runs,
/// every subscriber is awaited to completion, and each one's exception is caught on its own.
/// </summary>
/// <remarks>
/// ⚠⚠ THE ROW THAT FILED THIS CALLED THE DEFECT DORMANT AND IT WAS LIVE. `UI-7` `C-203`:
/// AudioStateStore is NOT this service's only subscriber. Ten production types subscribe — the store,
/// EncoderHudService, and eight rendered components — and the service is registered AddSingleton
/// while the component subscribers register PER CIRCUIT, so two open browsers already puts nine
/// handlers on NowPlayingChanged. Every consequence below was happening on the appliance.
///
/// ⚠ NOT ONE ASSERTION HERE USES A WALL CLOCK — `CLAUDE.md` § *Test Timing*. Every test synchronizes
/// on an OBSERVATION: a subscriber parked on a TaskCompletionSource the test has not yet completed
/// CANNOT proceed, so "the notify task is still incomplete" is a fact about control flow, not about
/// how fast the machine is.
///
/// ⭐ AND THE OBVIOUS ALTERNATIVE IS A TRAP THAT THIS ROW'S OWN PLAN FELL INTO. The plan's §4.1
/// specified the headline test as "3 subscribers, each completing after an `await Task.Yield()`; all
/// 3 recorded by the time the call returns". <c>AudioStateStoreNotifyTests</c> — written by `UI-6`,
/// for the same defect, one directory away — already records why that does not discriminate: on the
/// UNFIXED code the discarded continuations usually complete anyway, so such a test "would have
/// passed against the bug most of the time". The gate pattern below is used instead, and it is the
/// shape `UI-6` proved. A test that passes against the bug it was written for is this row's subject
/// matter, not an acceptable way to close it.
///
/// ⚠ These tests drive the PUBLIC production path, <see cref="AudioStateHubService.NotifySourceChangedAsync"/>
/// — raise site 15 of 15, the local-trigger path, and the only raise on this class reachable without a
/// live SignalR connection. That is deliberate: `C-213` found that the test harness fires hub events
/// with the very multicast-await this row deletes, so a test written through a reflection helper
/// could have reported "all subscribers awaited" against a completely unfixed implementation. Driving
/// the real method cannot lie about that. (The harness was repaired too — see
/// <see cref="HubEventFire"/> — but this file does not depend on the repair.)
///
/// 📌 What these do NOT prove: that the panel works. Every test here drives one service with fake
/// subscribers. That the eight real components still receive their events on a live circuit is a
/// deploy-time check, not a unit one.
/// </remarks>
public class AudioStateHubServiceNotifyTests
{
  private static AudioStateHubService NewHub() =>
    new(
      NullLogger<AudioStateHubService>.Instance,
      new ConfigurationBuilder().Build(),
      transport: new OfflineHubTransport());

  private static AudioStateHubService NewHub(List<(LogLevel Level, string Message)> sink) =>
    new(
      new CapturingLogger<AudioStateHubService>(sink),
      new ConfigurationBuilder().Build(),
      transport: new OfflineHubTransport());

  // --- The headline: every subscriber is AWAITED, not merely started -------------------------

  /// <summary>
  /// THE discriminating test. A subscriber registered FIRST parks on a gate; a fast one registered
  /// LAST returns an already-completed task.
  /// </summary>
  /// <remarks>
  /// Before `UI-7`, <c>await SourceChanged.Invoke()</c> returned only the LAST subscriber's task —
  /// already complete here — so the notify task completed while the first subscriber was still
  /// parked. This is mutation `M1`: revert NotifyAsync to the direct invoke and this assertion is
  /// the one that fails. Nothing depends on elapsed time; the gate cannot open until the test opens
  /// it.
  /// </remarks>
  [Fact]
  public async Task NotifySourceChangedDoesNotCompleteWhileAnEarlierSubscriberIsStillRunning()
  {
    var hub = NewHub();
    var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var slowFinished = false;
    var fastRan = false;

    hub.SourceChanged += async () => { await gate.Task; slowFinished = true; };
    hub.SourceChanged += () => { fastRan = true; return Task.CompletedTask; };

    var notify = hub.NotifySourceChangedAsync();

    Assert.False(notify.IsCompleted);
    Assert.False(slowFinished);
    Assert.False(fastRan); // sequential: the second subscriber has not been reached yet

    gate.SetResult();
    await notify;

    Assert.True(slowFinished);
    Assert.True(fastRan);
  }

  /// <summary>Subscribers run in registration order.</summary>
  [Fact]
  public async Task NotifySourceChangedRunsSubscribersInRegistrationOrder()
  {
    var hub = NewHub();
    var order = new List<int>();

    hub.SourceChanged += () => { order.Add(1); return Task.CompletedTask; };
    hub.SourceChanged += () => { order.Add(2); return Task.CompletedTask; };
    hub.SourceChanged += () => { order.Add(3); return Task.CompletedTask; };

    await hub.NotifySourceChangedAsync();

    Assert.Equal([1, 2, 3], order);
  }

  // --- Starvation: the half that a try/catch around the whole invoke does NOT fix ---------------

  /// <summary>
  /// ⭐ THE MUTATION THAT MATTERS (`M2`). A subscriber throwing SYNCHRONOUSLY — before its first
  /// await — threw out of <c>Delegate.Invoke</c> itself, so every handler registered AFTER it never
  /// ran at all.
  /// </summary>
  /// <remarks>
  /// This is what distinguishes the real fix from the one that looks like a fix. Wrapping the whole
  /// invoke in a single try/catch passes a casual review, stops the exception escaping, and leaves
  /// starvation completely intact — subscriber 3 still never runs. Only catching INSIDE the
  /// invocation-list loop resumes the list. Move the <c>try</c> outside the <c>foreach</c> and this
  /// test fails while the others still pass.
  /// </remarks>
  [Fact]
  public async Task NotifySourceChangedSynchronousThrowDoesNotStarveLaterSubscribers()
  {
    var hub = NewHub();
    var first = false;
    var third = false;

    hub.SourceChanged += () => { first = true; return Task.CompletedTask; };
    hub.SourceChanged += () => throw new InvalidOperationException("synchronous throw");
    hub.SourceChanged += () => { third = true; return Task.CompletedTask; };

    await hub.NotifySourceChangedAsync();

    Assert.True(first);
    Assert.True(third, "a synchronously throwing subscriber must not starve the ones after it");
  }

  /// <summary>A subscriber that throws AFTER an await is caught, logged, and does not escape.</summary>
  /// <remarks>Mutation `M3`: delete the catch and the exception escapes, failing this test.</remarks>
  [Fact]
  public async Task NotifySourceChangedAsynchronousThrowIsCaughtAndLogged()
  {
    var sink = new List<(LogLevel Level, string Message)>();
    var hub = NewHub(sink);
    var third = false;

    hub.SourceChanged += () => Task.CompletedTask;
    hub.SourceChanged += async () => { await Task.Yield(); throw new InvalidOperationException("async throw"); };
    hub.SourceChanged += () => { third = true; return Task.CompletedTask; };

    await hub.NotifySourceChangedAsync();

    Assert.True(third);
    Assert.Contains(sink, e => e.Level == LogLevel.Warning && e.Message.Contains("subscriber"));
  }

  /// <summary>
  /// Each subscriber's exception is isolated: two throwing subscribers produce two warnings and the
  /// survivors still run.
  /// </summary>
  /// <remarks>
  /// ⚠ The count is the point. NONE of the fifteen pre-UI-7 raise sites carried a try/catch — all
  /// fifteen were a bare <c>await X.Invoke(...)</c> inside a SignalR <c>On&lt;…&gt;</c> lambda, so a
  /// throwing subscriber faulted a task nobody held. (An earlier revision of this comment said
  /// "fourteen of the fifteen, and the one that did wrapped the whole invoke". That was false and
  /// pre-merge review caught it: the site that wrapped the whole invoke is
  /// <c>AudioStateStore</c>'s, one directory away, fixed by UI-6. <c>StartAsync</c>'s outer
  /// <c>try</c> encloses the REGISTRATION calls, not the lambda bodies, which run later on a
  /// callback. Exactly the CLAUDE.md § Pre-Merge Review failure mode, in the file that certifies
  /// the fix.)
  /// </remarks>
  [Fact]
  public async Task NotifySourceChangedIsolatesEachSubscribersException()
  {
    var sink = new List<(LogLevel Level, string Message)>();
    var hub = NewHub(sink);
    var survivorRan = false;

    hub.SourceChanged += () => throw new InvalidOperationException("first");
    hub.SourceChanged += () => throw new InvalidOperationException("second");
    hub.SourceChanged += () => { survivorRan = true; return Task.CompletedTask; };

    await hub.NotifySourceChangedAsync();

    Assert.True(survivorRan);
    Assert.Equal(2, sink.Count(e => e.Level == LogLevel.Warning));
  }

  // --- Degenerate cases -------------------------------------------------------------------------

  /// <summary>No subscribers at all is a normal state, not a NullReferenceException.</summary>
  /// <remarks>Remove the null guard in NotifyAsync and this throws.</remarks>
  [Fact]
  public async Task NotifySourceChangedWithNoSubscribersDoesNotThrow()
  {
    var hub = NewHub();

    await hub.NotifySourceChangedAsync();
  }

  /// <summary>One subscriber still completes — the loop is not an N&gt;1 special case.</summary>
  [Fact]
  public async Task NotifySourceChangedWithOneSubscriberStillCompletes()
  {
    var hub = NewHub();
    var ran = false;

    hub.SourceChanged += () => { ran = true; return Task.CompletedTask; };

    await hub.NotifySourceChangedAsync();

    Assert.True(ran);
  }
}
