using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Radio.Web.Services.ApiClients;
using Radio.Web.Tests.TestHelpers;

namespace Radio.Web.Tests.Services;

/// <summary>
/// EventPlaybackApiService — the READ and STOP halves of /api/audio/events (ADR-029 D1, D6, D7).
/// </summary>
/// <remarks>
/// ⚠ Over a local capturing handler rather than the shared <c>MockHttpHandler</c>. That helper
/// answers every request with one fixed status and body, which cannot express either of the two
/// tests below that need a per-request answer (both refusal codes) or the one that needs to inspect
/// the request URI. Stretching a shared helper for one caller would have been the larger change.
///
/// ⚠ A directly-constructed <c>HttpClient</c> is safe here: <c>NoNetworkHandlerFilter</c> only swaps
/// the primary handler on clients built by <c>IHttpClientFactory</c>, and only while it is still the
/// framework default.
/// </remarks>
public class EventPlaybackApiServiceTests
{
  private const string CurrentPath = "/api/audio/events/current";

  private static EventPlaybackApiService ServiceOver(CapturingHandler handler) =>
    new(
      new HttpClient(handler) { BaseAddress = new Uri(HermeticTestRig.ApiBaseUrl) },
      NullLogger<EventPlaybackApiService>.Instance);

  [Fact]
  public async Task GetCurrent_ReturnsNull_OnNoContent()
  {
    // ⚠ 204 is an ANSWER, not a failure: it means nothing has ever been started since the API booted.
    // The distinction that matters is against a 200 carrying a terminal snapshot, which means
    // something ran and finished — so this must not throw, and must not be logged as an error.
    var handler = new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));

    var result = await ServiceOver(handler).GetCurrentAsync();

    Assert.Null(result);
    Assert.Single(handler.Requests);
  }

  [Fact]
  public async Task GetCurrent_DeserialisesAStringState()
  {
    // The client half of C-47. The API spells both enums as strings on BOTH paths — MVC's
    // JsonStringEnumConverter on GET /current, and an explicit ToString() on the hub broadcast —
    // precisely so this one DTO can be filled from either. A build that modelled State as the
    // Radio.Core enum would bind here and fail on the hub, or vice versa.
    var handler = new CapturingHandler(_ => Json(
      """
      {
        "id": "evp-abc",
        "kind": "RemoteMedia",
        "label": "Voicemail from Jane",
        "state": "Playing",
        "duration": "00:00:29.9000000",
        "positionAtBroadcast": "00:00:00",
        "broadcastAtUtc": "2026-09-04T18:22:41.117Z",
        "failureReason": null
      }
      """));

    var result = await ServiceOver(handler).GetCurrentAsync();

    Assert.NotNull(result);
    Assert.Equal("evp-abc", result.Id);
    Assert.Equal("Playing", result.State);
    Assert.Equal("RemoteMedia", result.Kind);
    Assert.Equal("Voicemail from Jane", result.Label);
    Assert.Equal(TimeSpan.FromSeconds(29.9), result.Duration);
    Assert.Null(result.FailureReason);
    Assert.True(result.IsLive);
  }

  [Fact]
  public async Task GetCurrent_ReturnsNull_WhenTheApiIsUnreachable()
  {
    // Matching every sibling client in this assembly: a transport failure is caught and reported as
    // "nothing to show", never propagated. The callers are a circuit handler and, from PR 6, a
    // layout; neither is worth a blank screen.
    var handler = new CapturingHandler(_ => throw new HttpRequestException("no route to host"));

    var result = await ServiceOver(handler).GetCurrentAsync();

    Assert.Null(result);
  }

  [Fact]
  public async Task Stop_ReturnsTrue_OnNoContent()
  {
    var handler = new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));

    Assert.True(await ServiceOver(handler).StopAsync("evp-abc"));
  }

  [Theory]
  [InlineData(HttpStatusCode.NotFound)]
  [InlineData(HttpStatusCode.Conflict)]
  public async Task Stop_ReturnsFalse_OnNotFoundAndOnConflict(HttpStatusCode code)
  {
    // ⚠ Neither refusal is an error, and neither is logged as one. Both are ordinary answers to
    // "stop this": the playback ended between the caller reading the id and this call landing. On the
    // last-circuit path the id can be minutes old, so that is the COMMON case rather than the
    // exceptional one — which is why the return is a plain false and not an exception.
    var handler = new CapturingHandler(_ => new HttpResponseMessage(code));

    Assert.False(await ServiceOver(handler).StopAsync("evp-abc"));
  }

  [Fact]
  public async Task Stop_EscapesThePlaybackIdIntoThePath()
  {
    // Ids are server-minted evp-<guid> today, so this is defence in depth rather than a live hazard —
    // the same posture the seam already takes toward MediaId. It is here so that a future id scheme
    // carrying a slash or a space cannot silently address a different route.
    var handler = new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));

    await ServiceOver(handler).StopAsync("evp abc/def");

    var uri = Assert.Single(handler.Requests).RequestUri;
    Assert.NotNull(uri);
    Assert.Equal("/api/audio/events/evp%20abc%2Fdef", uri.AbsolutePath);
  }

  private static HttpResponseMessage Json(string body) =>
    new(HttpStatusCode.OK)
    {
      Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

  /// <summary>
  /// Answers from a per-request function and records every request it was given.
  /// </summary>
  private sealed class CapturingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
    : HttpMessageHandler
  {
    public List<HttpRequestMessage> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request, CancellationToken cancellationToken)
    {
      Requests.Add(request);
      return Task.FromResult(respond(request));
    }
  }
}
