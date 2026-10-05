using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Radio.Web.Models;
using Radio.Web.Services;
using Radio.Web.Services.ApiClients;
using Radio.Web.Tests.TestHelpers;

namespace Radio.Web.Tests.Services;

/// <summary>
/// Tests the Messages-feed contact-name resolution service (Task #6): the local
/// index seeded from the merged contact set (zero network), the async fallback,
/// and the caching + in-flight dedupe that keep the feed from hammering the API.
/// </summary>
public class ContactResolutionServiceTests
{
  private static readonly JsonSerializerOptions JsonOptions = new()
  {
    PropertyNameCaseInsensitive = true
  };

  private static ContactResolutionService Create(HttpMessageHandler handler)
  {
    var http = new HttpClient(handler) { BaseAddress = new Uri(HermeticTestRig.ApiBaseUrl) };
    var pbap = new PbapApiService(http, NullLogger<PbapApiService>.Instance);
    return new ContactResolutionService(pbap, NullLogger<ContactResolutionService>.Instance);
  }

  private static string NameBody(string name) =>
    JsonSerializer.Serialize(new { DisplayName = name, PhoneNumber = "x" }, JsonOptions);

  [Fact]
  public void TryResolve_PrefersAttachedName()
  {
    var svc = Create(new MockHttpHandler(statusCode: HttpStatusCode.NotFound));
    Assert.Equal("Grandpa", svc.TryResolve("9195550142", "Grandpa"));
  }

  [Fact]
  public void PrimeFromContacts_ResolvesLocally_WithoutNetwork()
  {
    var handler = new MockHttpHandler(statusCode: HttpStatusCode.NotFound);
    var svc = Create(handler);
    svc.PrimeFromContacts(new[]
    {
      new MergedContact(null, "Jane Doe", "9195550142", null, "PBAP")
    });

    Assert.Equal("Jane Doe", svc.TryResolve("9195550142"));
    Assert.True(svc.IsResolved("9195550142"));
    Assert.Equal(0, handler.RequestCount);   // never touched the network
  }

  [Fact]
  public async Task ResolveAsync_PrimedNumber_ShortCircuits_NoRequest()
  {
    var handler = new MockHttpHandler(statusCode: HttpStatusCode.NotFound);
    var svc = Create(handler);
    svc.PrimeFromContacts(new[] { new MergedContact(null, "Jane", "9195550142", null, "Manual") });

    Assert.Equal("Jane", await svc.ResolveAsync("9195550142"));
    Assert.Equal(0, handler.RequestCount);
  }

  [Fact]
  public async Task ResolveAsync_PositiveResult_CachedAfterFirstCall()
  {
    var handler = new MockHttpHandler(NameBody("Bob"));
    var svc = Create(handler);

    Assert.Equal("Bob", await svc.ResolveAsync("9195550142"));
    Assert.Equal("Bob", await svc.ResolveAsync("9195550142"));   // cache hit
    Assert.Equal("Bob", svc.TryResolve("9195550142"));           // now synchronous
    Assert.Equal(1, handler.RequestCount);                       // only one request
  }

  [Fact]
  public async Task ResolveAsync_NotFound_CachedAfterFirstCall()
  {
    var handler = new MockHttpHandler(statusCode: HttpStatusCode.NotFound);
    var svc = Create(handler);

    Assert.Null(await svc.ResolveAsync("9995551212"));
    Assert.Null(await svc.ResolveAsync("9995551212"));   // definitive miss cached, not retried
    Assert.True(svc.IsResolved("9995551212"));           // confirmed-miss counts as resolved
    Assert.Equal(1, handler.RequestCount);
  }

  [Fact]
  public async Task ResolveAsync_TransientFailure_NotCached_AndRetried()
  {
    // A 5xx must NOT poison the number: it stays unresolved so the next poll retries.
    // Otherwise a single backend hiccup on a long-lived kiosk circuit would drop the
    // contact's name to the raw number for the whole session.
    var handler = new MockHttpHandler(statusCode: HttpStatusCode.InternalServerError);
    var svc = Create(handler);

    Assert.Null(await svc.ResolveAsync("9195550142"));
    Assert.False(svc.IsResolved("9195550142"));   // not cached → eligible for retry
    Assert.Null(await svc.ResolveAsync("9195550142"));
    Assert.Equal(2, handler.RequestCount);        // retried, not served from cache
  }

  [Fact]
  public async Task ResolveAsync_LateSync_WinsOverCachedMiss()
  {
    var handler = new MockHttpHandler(statusCode: HttpStatusCode.NotFound);
    var svc = Create(handler);

    Assert.Null(await svc.ResolveAsync("9195550142"));   // cached negative

    // Contact synced later → the index entry must win over the negative cache.
    svc.PrimeFromContacts(new[] { new MergedContact(null, "Jane", "9195550142", null, "PBAP") });
    Assert.Equal("Jane", svc.TryResolve("9195550142"));
    Assert.Equal("Jane", await svc.ResolveAsync("9195550142"));
  }

  // ── PHN-14 ────────────────────────────────────────────────────────

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public void PrimeFromContacts_TheSyncedPhoneBookWinsOverARotaryPhoneContact_WhicheverSortsFirst(bool manualFirst)
  {
    var svc = Create(new MockHttpHandler(statusCode: HttpStatusCode.NotFound));
    var manual = new MergedContact("1", "Aaron (RotaryPhone)", "+1 919-555-0142", null, "Manual");
    var pbap = new MergedContact(null, "Zelda (synced)", "9195550142", null, "PBAP");

    svc.PrimeFromContacts(manualFirst ? new[] { manual, pbap } : new[] { pbap, manual });

    Assert.Equal("Zelda (synced)", svc.TryResolve("19195550142"));
  }

  [Fact]
  public void PrimeFromContacts_ARotaryPhoneContactStillAnswers_WhenNoSyncedOneHasTheNumber()
  {
    var svc = Create(new MockHttpHandler(statusCode: HttpStatusCode.NotFound));
    svc.PrimeFromContacts(new[]
    {
      new MergedContact("1", "Aaron (RotaryPhone)", "+1 919-555-0142", null, "Manual"),
      new MergedContact(null, "Someone else", "5550001111", null, "PBAP"),
    });

    Assert.Equal("Aaron (RotaryPhone)", svc.TryResolve("9195550142"));
  }

  [Fact]
  public async Task LookupForCallAsync_IgnoresTheIndexAndTheCache_AndReportsTheTier()
  {
    var handler = new ScriptedPhoneHandler();   // PbapName null → 404, which ResolveAsync caches as a miss
    var svc = Create(handler);
    svc.PrimeFromContacts(new[] { new MergedContact("1", "Index name", "9195550142", null, "Manual") });
    await svc.ResolveAsync("5550001111");

    handler.PbapName = "Synced local entry";
    handler.PbapExact = false;
    var forIndexed = await svc.LookupForCallAsync("9195550142");
    var forCachedMiss = await svc.LookupForCallAsync("5550001111");

    Assert.Equal(("Synced local entry", false), forIndexed);
    Assert.Equal(("Synced local entry", false), forCachedMiss);
  }

  [Fact]
  public async Task LookupForCallAsync_NoMatch_IsNull()
  {
    var svc = Create(new ScriptedPhoneHandler());

    Assert.Equal(((string?)null, false), await svc.LookupForCallAsync("9195550142"));
  }

  [Fact]
  public async Task ResolveAsync_ConcurrentSameNumber_IssuesSingleRequest()
  {
    // A gated handler holds the first response open so a second call for the same
    // number finds the in-flight task instead of starting its own request.
    var handler = new GatedHttpHandler(NameBody("Bob"));
    var svc = Create(handler);

    var t1 = svc.ResolveAsync("9195550142");
    var t2 = svc.ResolveAsync("9195550142");   // same circuit thread → shares t1's request
    handler.Release();
    var names = await Task.WhenAll(t1, t2);

    Assert.All(names, n => Assert.Equal("Bob", n));
    Assert.Equal(1, handler.RequestCount);
  }

  /// <summary>Handler that blocks its response until <see cref="Release"/> so the
  /// in-flight dedupe can be exercised deterministically.</summary>
  private sealed class GatedHttpHandler : HttpMessageHandler
  {
    private readonly string _body;
    private readonly TaskCompletionSource _gate =
      new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _requestCount;
    public int RequestCount => _requestCount;

    public GatedHttpHandler(string body) => _body = body;
    public void Release() => _gate.TrySetResult();

    protected override async Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request, CancellationToken cancellationToken)
    {
      Interlocked.Increment(ref _requestCount);
      await _gate.Task;
      return new HttpResponseMessage(HttpStatusCode.OK)
      {
        Content = new StringContent(_body, Encoding.UTF8, "application/json")
      };
    }
  }
}
