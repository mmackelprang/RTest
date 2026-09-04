using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Radio.Web.Models;
using Radio.Web.Services;
using Radio.Web.Services.ApiClients;
using Radio.Web.Services.Hub;
using Radio.Web.Tests.TestHelpers;

namespace Radio.Web.Tests.Services;

/// <summary>
/// AudioStateStore's attended-playback cache and its one-shot seed (ADR-029 D6 §8.1 ⟨A1·4⟩).
/// </summary>
/// <remarks>
/// ⚠ The broadcast half is driven by calling <c>OnHubEventPlaybackChanged</c> directly, which is
/// internal for exactly this reason. A field-like event can only be raised from inside the type that
/// declares it, so a test holding an <see cref="AudioStateHubService"/> cannot make it fire — and
/// driving the store's real handler sets precisely the state a broadcast sets, rather than a subset
/// a future edit could drift from.
///
/// ⚠ The hub service is constructed over <see cref="OfflineHubTransport"/> and never started, so
/// nothing here opens a socket. The store only needs it to subscribe to.
/// </remarks>
public class AudioStateStoreEventPlaybackTests
{
  private static AudioStateStore NewStore() =>
    new(
      NullLogger<AudioStateStore>.Instance,
      new AudioStateHubService(
        NullLogger<AudioStateHubService>.Instance,
        new ConfigurationBuilder().Build(),
        transport: new OfflineHubTransport()));

  private static EventPlaybackSnapshotDto Snapshot(string id = "evp-1", string state = "Playing") =>
    new(id, "Speech", "Message from Jane", state, TimeSpan.FromSeconds(30), TimeSpan.Zero,
      DateTimeOffset.UtcNow, null);

  [Fact]
  public async Task ABroadcastCachesTheSnapshotAndRaisesTheChangeEvent()
  {
    var store = NewStore();
    var raised = 0;
    store.EventPlaybackChanged += () => { raised++; return Task.CompletedTask; };

    await store.OnHubEventPlaybackChanged(Snapshot());

    Assert.NotNull(store.EventPlayback);
    Assert.Equal("evp-1", store.EventPlayback.Id);
    Assert.Equal(1, raised);
  }

  [Fact]
  public async Task TheSeedAppliesWhenNoBroadcastHasArrived()
  {
    // ADR-029 §8.1 ⟨A1·4⟩ makes this a requirement rather than a nicety: broadcasts fire on
    // TRANSITIONS, so a client connecting between two of them would render "nothing is playing"
    // while the room is talking.
    var store = NewStore();
    var api = ApiReturning(Body("evp-seeded", "Playing"));

    await store.EnsureEventPlaybackSeededAsync(api.Service);

    Assert.NotNull(store.EventPlayback);
    Assert.Equal("evp-seeded", store.EventPlayback.Id);
    Assert.Equal(1, api.Calls);
  }

  [Fact]
  public async Task ABroadcastThatLandsWhileTheSeedIsInFlightWINS()
  {
    // ⭐ THE ORDERING GUARD, and the whole reason it exists. A broadcast describes a LATER moment
    // than a response already in flight, so it must survive.
    //
    // This is not hypothetical: it is the ENC-12 boot case (MainLayout.razor:388-397). A deploy
    // restarts both services together, so Radio.API can broadcast while AudioStateHubService.StartAsync
    // is still inside its retry loop — and a seed that applied last-write-wins would overwrite the
    // newer state with the older one at exactly the moment the seed exists to help.
    var store = NewStore();
    var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

    var api = ApiReturning(Body("evp-stale", "Preparing"), onRequest: async () =>
    {
      entered.TrySetResult();
      await released.Task;
    });

    var seeding = store.EnsureEventPlaybackSeededAsync(api.Service);

    // Rendezvous on the request having ENTERED the handler, so the broadcast below is genuinely
    // interleaved with an in-flight seed rather than merely racing one.
    await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
    await store.OnHubEventPlaybackChanged(Snapshot("evp-fresh"));
    released.SetResult();
    await seeding.WaitAsync(TimeSpan.FromSeconds(5));

    Assert.NotNull(store.EventPlayback);
    Assert.Equal("evp-fresh", store.EventPlayback.Id);
  }

  [Fact]
  public async Task TheSeedRunsAtMostOnce()
  {
    // Claimed with Interlocked, so two circuits opening at the same instant produce one GET.
    var store = NewStore();
    var api = ApiReturning(Body("evp-seeded", "Playing"));

    await Task.WhenAll(
      store.EnsureEventPlaybackSeededAsync(api.Service),
      store.EnsureEventPlaybackSeededAsync(api.Service));
    await store.EnsureEventPlaybackSeededAsync(api.Service);

    Assert.Equal(1, api.Calls);
  }

  [Fact]
  public async Task TheSeedNeverThrows()
  {
    // Its callers are a CircuitHandler and, from PR 6, a layout. Neither is worth a blank screen.
    // ⚠ Note this passes for two reasons at once and that is deliberate: EventPlaybackApiService
    // already swallows transport failures and returns null, and the seed guards on top of it. Either
    // alone would satisfy this; the test asserts the OBSERVABLE contract, which is that a circuit
    // opening against a dead API leaves the store empty and quiet.
    var store = NewStore();
    var api = ApiThrowing();

    await store.EnsureEventPlaybackSeededAsync(api);

    Assert.Null(store.EventPlayback);
  }

  // ── helpers ───────────────────────────────────────────────────────────────

  private static string Body(string id, string state) =>
    $$"""
      {
        "id": "{{id}}",
        "kind": "Speech",
        "label": "Message from Jane",
        "state": "{{state}}",
        "duration": "00:00:30",
        "positionAtBroadcast": "00:00:00",
        "broadcastAtUtc": "2026-09-04T18:22:41.117Z",
        "failureReason": null
      }
      """;

  private sealed record Probe(EventPlaybackApiService Service, Func<int> CallCount)
  {
    public int Calls => CallCount();
  }

  private static Probe ApiReturning(string body, Func<Task>? onRequest = null)
  {
    var handler = new StubHandler(body, onRequest);
    var service = new EventPlaybackApiService(
      new HttpClient(handler) { BaseAddress = new Uri(HermeticTestRig.ApiBaseUrl) },
      NullLogger<EventPlaybackApiService>.Instance);
    return new Probe(service, () => handler.Calls);
  }

  private static EventPlaybackApiService ApiThrowing() =>
    new(
      new HttpClient(new ThrowingHandler()) { BaseAddress = new Uri(HermeticTestRig.ApiBaseUrl) },
      NullLogger<EventPlaybackApiService>.Instance);

  private sealed class StubHandler(string body, Func<Task>? onRequest) : HttpMessageHandler
  {
    private int _calls;

    public int Calls => Volatile.Read(ref _calls);

    protected override async Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request, CancellationToken cancellationToken)
    {
      Interlocked.Increment(ref _calls);

      if (onRequest is not null)
      {
        await onRequest();
      }

      return new HttpResponseMessage(HttpStatusCode.OK)
      {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
      };
    }
  }

  private sealed class ThrowingHandler : HttpMessageHandler
  {
    protected override Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request, CancellationToken cancellationToken)
      => throw new HttpRequestException("no route to host");
  }
}
