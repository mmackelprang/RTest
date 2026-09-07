using System.Net;

namespace Radio.Web.Tests.TestHelpers;

/// <summary>
/// Records every request a component makes, and lets a test <em>rendezvous on the observation</em>
/// instead of on elapsed wall-clock time.
///
/// <para>
/// Same motivation as <c>BluetoothCaptureWatchdogTests</c> (TEST-4): a component's assertions must
/// synchronize on the component having <em>observed</em> something, never on a <c>Task.Delay</c>
/// racing the component's own timer. For a Blazor panel the only dependency inside a debounce
/// callback is HTTP, so the handler is where the rendezvous belongs.
/// See TEST-7 in <c>docs/queue/TEST-7.md</c>.
/// </para>
///
/// <para>
/// ⚠ <b>The guarantee here is weaker than TEST-4's, and the difference is worth knowing before you
/// write a test against it.</b> That harness <em>parks</em> the watchdog on entry to every poll and
/// the test grants each one, so the component structurally cannot run ahead. This one only
/// <em>observes</em>: it releases the moment a matching request is recorded, and the component's
/// <c>async void</c> callback carries on — reading its response, and possibly outliving the test
/// method — while the test asserts. That is sufficient for everything asserted here, because every
/// assertion reads either the handler's own request log or a field the component wrote
/// <em>before</em> its first <c>await</c>. It would not be sufficient for an assertion on state the
/// component writes after its HTTP call. See <see cref="WaitForAsync"/>.
/// </para>
///
/// <para>
/// Pair it with a <c>FakeTimeProvider</c>, not instead of one — the two close opposite failure
/// directions. The fake clock makes an <em>extra</em> callback impossible (it cannot advance on its
/// own); this makes a <em>missing</em> one impossible (the test does not proceed until the request
/// is recorded).
/// </para>
/// </summary>
public sealed class RecordingHandler : HttpMessageHandler
{
  /// <summary>
  /// Deadlock guard, not a timing margin — the same role
  /// <c>BluetoothCaptureWatchdogTests.GateTimeout</c> plays. It is only ever reached when the
  /// request the test is waiting for never arrives, which is a failure.
  ///
  /// <para>
  /// Stated that way deliberately, rather than as "raising it can never make a failing test pass".
  /// That would be an overclaim: the debounce callback's continuation can be queued to the thread
  /// pool, so a sufficiently starved runner could in principle push a <em>correct</em> run past the
  /// deadline. Thirty seconds against a callback that completes in single-digit milliseconds makes
  /// that vanishingly unlikely — but "unlikely" is not "impossible", and this repository has a rule
  /// about comments that claim the stronger of the two.
  /// </para>
  /// </summary>
  public static readonly TimeSpan RendezvousTimeout = TimeSpan.FromSeconds(30);

  private readonly object _sync = new();
  private readonly List<(HttpMethod Method, string Path)> _requests = [];
  private readonly List<Waiter> _waiters = [];

  private sealed class Waiter
  {
    public required Func<HttpMethod, string, bool> Match { get; init; }
    public required int Target { get; init; }
    public int Seen { get; set; }

    // RunContinuationsAsynchronously is kept deliberately, but NOT for the reason it is usually
    // given. TrySetResult is called from inside SendAsync, which under FakeTimeProvider runs on the
    // thread that called Advance — the test thread — so the textbook hazard is an awaiting test body
    // resuming inline, inside the handler, mid-Advance. That specific hazard is not reachable here:
    // WaitForAsync hands out `Signal.Task.WaitAsync(...)`, a wrapper task, so the test body is never
    // a direct continuation of this source; and xUnit's default collection parallelization installs
    // a SynchronizationContext that would post the resumption rather than run it inline anyway.
    //
    // The flag stays because it makes that property hold on the harness's own terms instead of
    // resting on two implementation details of code outside this file.
    public TaskCompletionSource Signal { get; } =
      new(TaskCreationOptions.RunContinuationsAsynchronously);
  }

  public static bool IsConfigWrite(HttpMethod method, string path) =>
    method == HttpMethod.Post && path == "/api/configuration/ui.playback";

  public static bool IsVolumeCall(HttpMethod method, string path) =>
    method == HttpMethod.Post && path.StartsWith("/api/audio/volume/", StringComparison.Ordinal);

  public static bool IsSourceGainCall(HttpMethod method, string path) =>
    method == HttpMethod.Post && path.StartsWith("/api/audio/sourcegain/", StringComparison.Ordinal);

  public IReadOnlyList<(HttpMethod Method, string Path)> Requests
  {
    get
    {
      lock (_sync)
      {
        return _requests.ToList();
      }
    }
  }

  public int Count(Func<HttpMethod, string, bool> predicate)
  {
    lock (_sync)
    {
      return _requests.Count(r => predicate(r.Method, r.Path));
    }
  }

  /// <summary>
  /// Completes once <paramref name="count"/> requests matching <paramref name="predicate"/> have
  /// been <b>recorded</b>.
  ///
  /// <para>
  /// Recording happens before the response is produced (see <c>SendAsync</c>), so a caller that
  /// awaits this and then calls <see cref="Count"/> is asserting on <b>this handler's</b> state,
  /// already written. ⚠ <b>It is not a statement about the component's state.</b> The callback that
  /// made the request is still running — it has not yet seen its response — so an assertion on a
  /// field the component writes <em>after</em> the HTTP call would be racy. Every assertion in the
  /// TEST-7 suites reads either this log or a value assigned synchronously before the first
  /// <c>await</c>, which is why they are safe.
  /// </para>
  /// </summary>
  public async Task WaitForAsync(Func<HttpMethod, string, bool> predicate, int count)
  {
    Waiter waiter;
    lock (_sync)
    {
      var already = _requests.Count(r => predicate(r.Method, r.Path));
      if (already >= count)
      {
        return;
      }

      waiter = new Waiter { Match = predicate, Target = count, Seen = already };
      _waiters.Add(waiter);
    }

    try
    {
      await waiter.Signal.Task.WaitAsync(RendezvousTimeout);
    }
    catch (TimeoutException)
    {
      // The whole justification for a deadlock guard is the quality of the failure it produces, and
      // a bare "The operation has timed out." names neither the predicate nor what did arrive. The
      // overwhelmingly likely cause is a route that moved out from under a predicate, and the
      // request that proves it is sitting in _requests — so print it rather than discard it.
      int seen;
      List<(HttpMethod Method, string Path)> recorded;
      lock (_sync)
      {
        seen = waiter.Seen;
        recorded = _requests.ToList();
      }

      var observed = recorded.Count == 0
        ? "    (no requests were made at all)"
        : string.Join(Environment.NewLine, recorded.Select(r => $"    {r.Method} {r.Path}"));

      Assert.Fail(
        $"Timed out after {RendezvousTimeout.TotalSeconds:F0}s waiting for {count} matching " +
        $"request(s); {seen} matched. Requests actually recorded ({recorded.Count}):" +
        Environment.NewLine + observed);
    }
    finally
    {
      // SendAsync removes a waiter once it is satisfied; this covers the timeout path, where
      // nothing else ever would. Remove is a no-op when the waiter is already gone.
      lock (_sync)
      {
        _waiters.Remove(waiter);
      }
    }
  }

  protected override Task<HttpResponseMessage> SendAsync(
    HttpRequestMessage request, CancellationToken cancellationToken)
  {
    var method = request.Method;
    var path = request.RequestUri?.AbsolutePath ?? string.Empty;

    List<Waiter>? ready = null;
    lock (_sync)
    {
      _requests.Add((method, path));

      foreach (var waiter in _waiters)
      {
        if (!waiter.Match(method, path))
        {
          continue;
        }

        if (++waiter.Seen < waiter.Target)
        {
          continue;
        }

        (ready ??= []).Add(waiter);
      }

      if (ready is not null)
      {
        foreach (var waiter in ready)
        {
          _waiters.Remove(waiter);
        }
      }
    }

    // Signalled outside the lock so a continuation can never re-enter SendAsync while it is held.
    if (ready is not null)
    {
      foreach (var waiter in ready)
      {
        waiter.Signal.TrySetResult();
      }
    }

    // "{}" satisfies both the PlaybackStateDto read on the volume call and the dictionary read the
    // configuration client performs before it writes.
    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
    {
      Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json")
    });
  }
}
