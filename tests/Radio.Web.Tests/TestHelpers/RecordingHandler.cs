using System.Net;

namespace Radio.Web.Tests.TestHelpers;

/// <summary>
/// Records every request a component makes, and lets a test <em>rendezvous on the observation</em>
/// instead of on elapsed wall-clock time.
///
/// <para>
/// This is the outer layer of the shape <c>BluetoothCaptureWatchdogTests</c> uses one layer in
/// (TEST-4): a component's assertions must synchronize on the component having <em>observed</em>
/// something, never on a <c>Task.Delay</c> racing the component's own timer. For a Blazor panel the
/// only dependency inside a debounce callback is HTTP, so the handler is where the rendezvous
/// belongs. See TEST-7 in <c>docs/BUILDER_QUEUE.md</c>.
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
  /// Deadlock guard, not a timing margin. It bounds how a broken test fails and is never reached on
  /// a passing run — the same role <c>BluetoothCaptureWatchdogTests.GateTimeout</c> plays. Raising
  /// it can never make a failing test pass.
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

    // RunContinuationsAsynchronously is load-bearing, not a default worth copying blindly: this is
    // completed from inside SendAsync, which under FakeTimeProvider runs on the thread that called
    // Advance — the test thread. Without the flag the awaiting test body would resume inline,
    // inside the handler, in the middle of Advance.
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
  /// been <b>recorded</b>. Recording happens before the response is produced, so a caller that
  /// awaits this and then calls <see cref="Count"/> is asserting on state already written.
  /// </summary>
  public Task WaitForAsync(Func<HttpMethod, string, bool> predicate, int count)
  {
    Waiter waiter;
    lock (_sync)
    {
      var already = _requests.Count(r => predicate(r.Method, r.Path));
      if (already >= count)
      {
        return Task.CompletedTask;
      }

      waiter = new Waiter { Match = predicate, Target = count, Seen = already };
      _waiters.Add(waiter);
    }

    return waiter.Signal.Task.WaitAsync(RendezvousTimeout);
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
