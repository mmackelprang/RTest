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
    var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5000") };
    var pbap = new PbapApiService(http, NullLogger<PbapApiService>.Instance);
    return new ContactResolutionService(pbap, NullLogger<ContactResolutionService>.Instance);
  }

  private static string NameBody(string name) =>
    JsonSerializer.Serialize(new { DisplayName = name, PhoneNumber = "x" }, JsonOptions);

  [Fact]
  public void TryResolve_PrefersAttachedName()
  {
    var svc = Create(new MockHttpHandler(statusCode: HttpStatusCode.NotFound));
    Assert.Equal("Grandpa", svc.TryResolve("9193718044", "Grandpa"));
  }

  [Fact]
  public void PrimeFromContacts_ResolvesLocally_WithoutNetwork()
  {
    var handler = new MockHttpHandler(statusCode: HttpStatusCode.NotFound);
    var svc = Create(handler);
    svc.PrimeFromContacts(new[]
    {
      new MergedContact(null, "Jane Doe", "9193718044", null, "PBAP")
    });

    Assert.Equal("Jane Doe", svc.TryResolve("9193718044"));
    Assert.True(svc.IsResolved("9193718044"));
    Assert.Equal(0, handler.RequestCount);   // never touched the network
  }

  [Fact]
  public async Task ResolveAsync_PrimedNumber_ShortCircuits_NoRequest()
  {
    var handler = new MockHttpHandler(statusCode: HttpStatusCode.NotFound);
    var svc = Create(handler);
    svc.PrimeFromContacts(new[] { new MergedContact(null, "Jane", "9193718044", null, "Manual") });

    Assert.Equal("Jane", await svc.ResolveAsync("9193718044"));
    Assert.Equal(0, handler.RequestCount);
  }

  [Fact]
  public async Task ResolveAsync_PositiveResult_CachedAfterFirstCall()
  {
    var handler = new MockHttpHandler(NameBody("Bob"));
    var svc = Create(handler);

    Assert.Equal("Bob", await svc.ResolveAsync("9193718044"));
    Assert.Equal("Bob", await svc.ResolveAsync("9193718044"));   // cache hit
    Assert.Equal("Bob", svc.TryResolve("9193718044"));           // now synchronous
    Assert.Equal(1, handler.RequestCount);                       // only one request
  }

  [Fact]
  public async Task ResolveAsync_NegativeResult_CachedAfterFirstCall()
  {
    var handler = new MockHttpHandler(statusCode: HttpStatusCode.NotFound);
    var svc = Create(handler);

    Assert.Null(await svc.ResolveAsync("9995551212"));
    Assert.Null(await svc.ResolveAsync("9995551212"));   // negative cached, not retried
    Assert.True(svc.IsResolved("9995551212"));           // confirmed-miss counts as resolved
    Assert.Equal(1, handler.RequestCount);
  }

  [Fact]
  public async Task ResolveAsync_LateSync_WinsOverCachedMiss()
  {
    var handler = new MockHttpHandler(statusCode: HttpStatusCode.NotFound);
    var svc = Create(handler);

    Assert.Null(await svc.ResolveAsync("9193718044"));   // cached negative

    // Contact synced later → the index entry must win over the negative cache.
    svc.PrimeFromContacts(new[] { new MergedContact(null, "Jane", "9193718044", null, "PBAP") });
    Assert.Equal("Jane", svc.TryResolve("9193718044"));
    Assert.Equal("Jane", await svc.ResolveAsync("9193718044"));
  }

  [Fact]
  public async Task ResolveAsync_ConcurrentSameNumber_IssuesSingleRequest()
  {
    // A gated handler holds the first response open so a second call for the same
    // number finds the in-flight task instead of starting its own request.
    var handler = new GatedHttpHandler(NameBody("Bob"));
    var svc = Create(handler);

    var t1 = svc.ResolveAsync("9193718044");
    var t2 = svc.ResolveAsync("9193718044");   // same circuit thread → shares t1's request
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
