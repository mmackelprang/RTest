using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Radio.Web.Models;
using Radio.Web.Services;
using Radio.Web.Services.ApiClients;
using Radio.Web.Services.Hub;
using Radio.Web.Tests.TestHelpers;

namespace Radio.Web.Tests.Services;

/// <summary>
/// AttendedPlaybackCircuitHandler — ADR-029 D7 §7.3's last-circuit-closed backstop.
/// </summary>
/// <remarks>
/// ⚠ <c>Circuit</c> has no public constructor, so every call below passes <c>null!</c>. That is not a
/// compromise: the handler must NEVER read its circuit argument. ADR-029 §7.4 deleted the ownership
/// model outright — "there is one audio engine and one set of speakers, so there is one playback and
/// no owner" — so a handler that looked at WHICH circuit closed would be reimplementing the rule §7.3
/// was rewritten to remove. The untestability of Circuit is a useful fence around that.
///
/// ⚠ What these tests CANNOT prove, stated rather than implied: that a singleton CircuitHandler
/// actually receives every circuit's callbacks (plan U2), and that a browser refresh goes 1 → 2 → 1
/// rather than touching zero (U3). Both need a real Blazor host.
///
/// ⚠ U2 is settled and holds. U3 has been settled ON THE BOX AND AGAINST THE CODE: a refresh does
/// NOT go 1 → 2 → 1, it goes 1 → 0 → 1. Every test below asserts the handler's arithmetic, which is
/// correct; none can see that Blazor feeds it the wrong sequence.
/// </remarks>
public class AttendedPlaybackCircuitHandlerTests
{
  [Fact]
  public async Task TwoCircuitsOpenAndOneClosing_StopsNothing()
  {
    // ⭐ The kiosk and a laptop — the case an owner-circuit implementation gets wrong, and the reason
    // ⟨A1·4⟩ rewrote §7.3. Closing the laptop must not silence the panel somebody is watching.
    var rig = await RigWithLivePlaybackAsync();

    await rig.Handler.OnCircuitOpenedAsync(null!, default);
    await rig.Handler.OnCircuitOpenedAsync(null!, default);
    await rig.Handler.OnCircuitClosedAsync(null!, default);

    Assert.Equal(1, rig.Handler.OpenCircuits);
    Assert.Empty(rig.Stops);
  }

  [Fact]
  public async Task TheLastCircuitClosing_StopsALivePlayback()
  {
    var rig = await RigWithLivePlaybackAsync();

    await rig.Handler.OnCircuitOpenedAsync(null!, default);
    await rig.Handler.OnCircuitClosedAsync(null!, default);

    Assert.Equal(0, rig.Handler.OpenCircuits);
    Assert.Equal("evp-live", Assert.Single(rig.Stops));
  }

  [Fact]
  public async Task TheLastCircuitClosing_StopsNothingWhenTheSnapshotIsTerminal()
  {
    // A terminal snapshot is RETAINED in the store exactly as it is on the server, so "nothing is
    // playing" is a state rather than the absence of one. IsLive is what decides, not null-ness.
    var rig = await RigWithPlaybackAsync(state: "Completed");

    await rig.Handler.OnCircuitOpenedAsync(null!, default);
    await rig.Handler.OnCircuitClosedAsync(null!, default);

    Assert.Empty(rig.Stops);
  }

  [Fact]
  public async Task TheLastCircuitClosing_StopsAnUnrecognisedStateAnyway()
  {
    // ⭐ The assertion that makes PHN-1f deployable without a lockstep Radio.Web build, and it has to
    // be written NOW, while "Waiting" really is a value this build has never heard of.
    //
    // IsLive is written as "not one of the terminal three" rather than "one of the live three"
    // precisely so an unknown state counts as LIVE — the safe direction, because an unknown state
    // that is in fact playing must keep its stop control.
    var rig = await RigWithPlaybackAsync(state: "Waiting");

    await rig.Handler.OnCircuitOpenedAsync(null!, default);
    await rig.Handler.OnCircuitClosedAsync(null!, default);

    Assert.Equal("evp-live", Assert.Single(rig.Stops));
  }

  [Fact]
  public async Task AClose_WithoutAnOpen_ResetsTheCountAndWarns()
  {
    // C-52. A count left negative would make the "== 0" test unreachable for the life of the
    // process — a backstop that has silently stopped backstopping. Not reachable today; clamped
    // loudly rather than silently normalised.
    //
    // ⚠ "AndWarns" is asserted, not assumed. An earlier draft injected NullLogger and named the
    // warning in the test's title without ever observing it — a title claiming coverage the body
    // did not have. LOUDLY is the whole point of the clamp: a silent reset would be
    // indistinguishable from the bug it exists to make visible.
    var rig = await RigWithLivePlaybackAsync();

    await rig.Handler.OnCircuitClosedAsync(null!, default);

    Assert.Equal(0, rig.Handler.OpenCircuits);
    Assert.Empty(rig.Stops);
    Assert.Contains(
      rig.Warnings,
      m => m.Contains("no matching open", StringComparison.Ordinal));
  }

  [Fact]
  public async Task ACircuitOpening_SeedsTheStoreOnce()
  {
    // A circuit opening IS ADR-029 §8.1's re-attach moment. The seed is one-shot per process.
    var rig = await RigAsync(seedBody: Body("evp-seeded", "Playing"));

    await rig.Handler.OnCircuitOpenedAsync(null!, default);
    await rig.Handler.OnCircuitOpenedAsync(null!, default);
    await WaitUntilAsync(() => rig.Store.EventPlayback is not null, TimeSpan.FromSeconds(5));

    Assert.Equal("evp-seeded", rig.Store.EventPlayback!.Id);
    Assert.Equal(1, rig.Gets);
  }

  [Fact]
  public async Task ASeedFailureOnOpenIsCaughtAndLogged()
  {
    // Fire-and-forget by design rather than by omission: awaiting it would hold a circuit's start
    // behind an HTTP call to a service that may still be booting, on a deploy that restarts both.
    //
    // ⚠ THIS TEST WAS REWRITTEN BECAUSE IT COULD NOT FAIL. As written it asserted
    // OpenCircuits == 1 and Store.EventPlayback == null, and neither can fail for ANY
    // implementation: the count is incremented unconditionally, and the seed is dispatched with a
    // discard, so deleting BOTH try/catches leaves the exception in an unobserved faulted task that
    // never reaches the caller — the test still passes. Worse, the null assertion was taken before
    // the seed could have completed, so it would have read null against a SUCCEEDING seed too.
    //
    // The containment is observable in exactly one place — the warning SeedAsync's catch emits — so
    // that is what is asserted, and the wait is on the observation rather than on elapsed time
    // (CLAUDE.md § Test Timing). Delete SeedAsync's catch and this times out.
    //
    // ⚠ AND IT IS DRIVEN THROUGH DI, NOT THROUGH A FAILING GET, which is a fact worth having
    // written down: throwOnGet CANNOT reach SeedAsync's catch, because
    // EventPlaybackApiService.GetCurrentAsync catches its own exceptions and answers null, and
    // EnsureEventPlaybackSeededAsync then returns normally. The only way the seed faults is if the
    // scope cannot produce the client — so that is the failure this drives. The first attempt at
    // this rewrite used throwOnGet and went red for exactly that reason.
    var rig = await RigAsync(breakScopeResolution: true);

    await rig.Handler.OnCircuitOpenedAsync(null!, default);
    await WaitUntilAsync(
      () => rig.Warnings.Any(m => m.Contains("Error seeding", StringComparison.Ordinal)),
      TimeSpan.FromSeconds(5));

    Assert.Contains(rig.Warnings, m => m.Contains("Error seeding", StringComparison.Ordinal));

    // And the circuit itself survived: no throw escaped, and the count still moved.
    Assert.Equal(1, rig.Handler.OpenCircuits);
  }

  [Fact]
  public async Task AThrowFromTheOPENPathDoesNotFaultTheCircuitOrStrandTheCount()
  {
    // ⚠ The failure this guards has NO recovery, which is why it earns a test. A throwing
    // CircuitHandler method is fatal to the circuit — and here that circuit is LIVE, not one already
    // closing. Worse than losing the session: the increment has already happened, so no matching
    // OnCircuitClosedAsync ever runs, _openCircuits sits permanently >= 1, and the "remaining != 0"
    // early return means the D30 stop rule SILENTLY NEVER FIRES AGAIN for the life of the process.
    // The negative-count reset guards the other direction; there is no equivalent for this one, so
    // the outer catch is what prevents it.
    //
    // ⚠ Driven through a throwing LOGGER, and the choice is forced rather than cute. Nothing else in
    // OnCircuitOpenedAsync can throw: Interlocked.Increment cannot, and Seed()'s body is an async
    // method whose every exception — DI resolution included — is captured into the discarded Task
    // rather than raised at the call site. ILogger.Log is the one synchronous call that can, and it
    // genuinely does in production: it aggregates and rethrows provider exceptions, so a wedged
    // Serilog sink is a real instance of this.
    //
    // Delete the outer try/catch and this test throws instead of failing an assertion.
    var rig = await RigAsync(throwOnLog: true);

    // 1. The throw does not escape.
    await rig.Handler.OnCircuitOpenedAsync(null!, default);

    // 2. And the count is not stranded: the circuit survived, so its close balances the books.
    Assert.Equal(1, rig.Handler.OpenCircuits);
    await rig.Handler.OnCircuitClosedAsync(null!, default);
    Assert.Equal(0, rig.Handler.OpenCircuits);
  }

  // ── rig ───────────────────────────────────────────────────────────────────

  private sealed class Rig
  {
    public required AttendedPlaybackCircuitHandler Handler { get; init; }

    public required AudioStateStore Store { get; init; }

    public required List<string> Stops { get; init; }

    public required Func<int> GetCount { get; init; }

    public required List<string> Warnings { get; init; }

    public int Gets => GetCount();
  }

  /// <summary>
  /// Records the message of every Warning-or-above line the handler logs.
  /// </summary>
  /// <remarks>
  /// A local type rather than a reference to Radio.Infrastructure.Tests's CapturingLoggerProvider:
  /// that one is internal to a different assembly, and adding an assembly reference to reach a
  /// four-line test double would couple two test projects that share nothing else.
  /// </remarks>
  private sealed class CapturingLogger<T>(List<string> sink, bool throwOnLog = false) : ILogger<T>
  {
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
      LogLevel logLevel, EventId eventId, TState state, Exception? exception,
      Func<TState, Exception?, string> formatter)
    {
      // Stands in for a wedged log provider. ILogger.Log aggregates and rethrows provider
      // exceptions, so this is the shape a failing Serilog sink presents to a caller.
      if (throwOnLog)
      {
        throw new InvalidOperationException("the log sink is wedged");
      }

      if (logLevel >= LogLevel.Warning)
      {
        lock (sink)
        {
          sink.Add(formatter(state, exception));
        }
      }
    }
  }

  private static async Task<Rig> RigWithLivePlaybackAsync() =>
    await RigWithPlaybackAsync("Playing");

  private static async Task<Rig> RigWithPlaybackAsync(string state)
  {
    var rig = await RigAsync();

    // Drive the store through its real broadcast handler, so the cached state is exactly what a
    // broadcast produces.
    await rig.Store.OnHubEventPlaybackChanged(
      new EventPlaybackSnapshotDto("evp-live", "Speech", "Message", state,
        TimeSpan.FromSeconds(30), TimeSpan.Zero, DateTimeOffset.UtcNow, null));

    return rig;
  }

  private static Task<Rig> RigAsync(
    string? seedBody = null,
    bool throwOnGet = false,
    bool breakScopeResolution = false,
    bool throwOnLog = false)
  {
    var stops = new List<string>();
    var warnings = new List<string>();
    var handler = new RouteHandler(stops, seedBody, throwOnGet);

    var store = new AudioStateStore(
      NullLogger<AudioStateStore>.Instance,
      new AudioStateHubService(
        NullLogger<AudioStateHubService>.Instance,
        new ConfigurationBuilder().Build(),
        transport: new OfflineHubTransport()));

    var services = new ServiceCollection();

    // breakScopeResolution leaves EventPlaybackApiService UNREGISTERED, so the handler's
    // GetRequiredService throws inside Seed() - the one thing in OnCircuitOpenedAsync that can
    // realistically throw, and the way AnOpenThatThrowsDoesNotLeaveTheCountOverCounted reaches it.
    if (!breakScopeResolution)
    {
      services.AddSingleton(new EventPlaybackApiService(
        new HttpClient(handler) { BaseAddress = new Uri(HermeticTestRig.ApiBaseUrl) },
        NullLogger<EventPlaybackApiService>.Instance));
    }

    var provider = services.BuildServiceProvider();

    return Task.FromResult(new Rig
    {
      Handler = new AttendedPlaybackCircuitHandler(
        provider.GetRequiredService<IServiceScopeFactory>(),
        store,
        new CapturingLogger<AttendedPlaybackCircuitHandler>(warnings, throwOnLog)),
      Store = store,
      Stops = stops,
      Warnings = warnings,
      GetCount = () => handler.Gets,
    });
  }

  private static string Body(string id, string state) =>
    $$"""
      {
        "id": "{{id}}",
        "kind": "Speech",
        "label": "Message",
        "state": "{{state}}",
        "duration": "00:00:30",
        "positionAtBroadcast": "00:00:00",
        "broadcastAtUtc": "2026-09-04T18:22:41.117Z",
        "failureReason": null
      }
      """;

  /// <summary>
  /// Answers GET /api/audio/events/current from <paramref name="seedBody"/> and records the id of
  /// every DELETE /api/audio/events/{id}.
  /// </summary>
  private sealed class RouteHandler(List<string> stops, string? seedBody, bool throwOnGet)
    : HttpMessageHandler
  {
    private int _gets;

    public int Gets => Volatile.Read(ref _gets);

    protected override Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request, CancellationToken cancellationToken)
    {
      if (request.Method == HttpMethod.Delete)
      {
        var path = request.RequestUri!.AbsolutePath;
        lock (stops)
        {
          stops.Add(Uri.UnescapeDataString(path[(path.LastIndexOf('/') + 1)..]));
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
      }

      Interlocked.Increment(ref _gets);

      if (throwOnGet)
      {
        throw new HttpRequestException("no route to host");
      }

      if (seedBody is null)
      {
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
      }

      return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
      {
        Content = new StringContent(seedBody, System.Text.Encoding.UTF8, "application/json")
      });
    }
  }

  private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
  {
    var deadline = DateTime.UtcNow + timeout;
    while (DateTime.UtcNow < deadline)
    {
      if (condition())
      {
        return;
      }

      await Task.Delay(10);
    }

    Assert.True(condition(), "condition was not met within the timeout");
  }
}
