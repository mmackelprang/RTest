using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
/// rather than touching zero (U3). Both need a real Blazor host. They are settled on the box.
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
    var rig = await RigWithLivePlaybackAsync();

    await rig.Handler.OnCircuitClosedAsync(null!, default);

    Assert.Equal(0, rig.Handler.OpenCircuits);
    Assert.Empty(rig.Stops);
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
  public async Task ASeedFailureOnOpenDoesNotFaultTheCircuit()
  {
    // Fire-and-forget by design rather than by omission: awaiting it would hold a circuit's start
    // behind an HTTP call to a service that may still be booting, on a deploy that restarts both.
    var rig = await RigAsync(throwOnGet: true);

    await rig.Handler.OnCircuitOpenedAsync(null!, default);

    Assert.Equal(1, rig.Handler.OpenCircuits);
    Assert.Null(rig.Store.EventPlayback);
  }

  // ── rig ───────────────────────────────────────────────────────────────────

  private sealed class Rig
  {
    public required AttendedPlaybackCircuitHandler Handler { get; init; }

    public required AudioStateStore Store { get; init; }

    public required List<string> Stops { get; init; }

    public required Func<int> GetCount { get; init; }

    public int Gets => GetCount();
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

  private static Task<Rig> RigAsync(string? seedBody = null, bool throwOnGet = false)
  {
    var stops = new List<string>();
    var handler = new RouteHandler(stops, seedBody, throwOnGet);

    var store = new AudioStateStore(
      NullLogger<AudioStateStore>.Instance,
      new AudioStateHubService(
        NullLogger<AudioStateHubService>.Instance,
        new ConfigurationBuilder().Build(),
        transport: new OfflineHubTransport()));

    var services = new ServiceCollection();
    services.AddSingleton(new EventPlaybackApiService(
      new HttpClient(handler) { BaseAddress = new Uri(HermeticTestRig.ApiBaseUrl) },
      NullLogger<EventPlaybackApiService>.Instance));

    var provider = services.BuildServiceProvider();

    return Task.FromResult(new Rig
    {
      Handler = new AttendedPlaybackCircuitHandler(
        provider.GetRequiredService<IServiceScopeFactory>(),
        store,
        NullLogger<AttendedPlaybackCircuitHandler>.Instance),
      Store = store,
      Stops = stops,
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
