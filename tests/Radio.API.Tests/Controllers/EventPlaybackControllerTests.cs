using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Radio.API.Tests.TestSupport;

namespace Radio.API.Tests.Controllers;

/// <summary>
/// The /api/audio/events route family, built so the tests can fail.
/// </summary>
/// <remarks>
/// ⚠ The hazard this file is written against is next door.
/// NotificationsControllerTests.Announce_WithValidMessage_ReturnsOk asserts a success status against
/// a host where TTS cannot possibly work, and it passes because AnnounceAsync swallows every
/// exception internally — so a green test there proves the route is mapped and nothing else.
///
/// Every fact below therefore asserts an outcome a dead or half-wired surface could not produce. An
/// unmapped route gives 404, a broken DTO gives a generic 400 with no named reason, an unresolvable
/// dependency gives 500 — each of those fails these rather than passing them.
///
/// ⚠ No test here asserts a playback reaches Completed. Nothing in the test host produces audio:
/// CustomWebApplicationFactory removes every IHostedService, so AddSoundFlowAudio's hardware
/// initialisation never runs, and AudioFileEventSource has a SILENT PlaybackLoopAsync fallback that
/// reports a clean completion having produced no sound at all. "It completed" is the least
/// trustworthy possible evidence here. That a voicemail is audible is a box check and it is PR 6's.
/// </remarks>
public class EventPlaybackControllerTests : IClassFixture<CustomWebApplicationFactory<Program>>
{
  private readonly CustomWebApplicationFactory<Program> _factory;

  public EventPlaybackControllerTests(CustomWebApplicationFactory<Program> factory)
    => _factory = factory;

  private static async Task<JsonElement> BodyOf(HttpResponseMessage response) =>
    await response.Content.ReadFromJsonAsync<JsonElement>();

  [Fact]
  public async Task Post_RemoteMedia_Returns409_WhenGvMediaIsDisabled()
  {
    // A 409 with reason "Disabled" can only come from the controller's own catch — an unmapped route
    // gives 404, a broken DTO gives a generic 400, an unresolvable dependency gives 500. It also
    // proves the whole Radio.API container still builds with AddEventPlayback in it.
    //
    // ⚠ THE FLAG IS SET HERE RATHER THAN INHERITED, and that changed in PHN-2. This test used to say
    // "GvMedia:Enabled ships false" and lean on the shipped default; PHN-2 flipped that default to
    // true to turn Feature A on, so leaning on it now returns 202 and the pin evaporates. Setting the
    // flag the test is ABOUT is also the honest form — the test is named for a disabled gate, so the
    // gate should be disabled by the test rather than by a value in a file it never mentions.
    //
    // ⚠ Its own host, for the same reason. A 202 on the SHARED fixture host would leave a live
    // playback behind and break Get_Current_Returns204_WhenNothingHasBeenStarted, which is exactly
    // what happened when the default flipped.
    var client = _factory.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) =>
      c.AddInMemoryCollection(new Dictionary<string, string?>
      {
        ["GvMedia:Enabled"] = "false"
      }))).CreateClient();

    var response = await client.PostAsJsonAsync("/api/audio/events", new
    {
      kind = "RemoteMedia", mediaKind = "GvVoicemail", mediaId = "vm-abc123", durationSeconds = 12
    });

    Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    Assert.Equal("Disabled", (await BodyOf(response)).GetProperty("reason").GetString());
  }

  [Theory]
  [InlineData("https://evil.example/payload.mp3", "MediaIdLooksLikeUrl")]
  [InlineData("http:evil.example", "MediaIdHasIllegalCharacter")]
  [InlineData("../../etc/shadow", "MediaIdHasPathSeparator")]
  public async Task Post_AUrlBearingMediaId_Returns400_WithTheNamedReason(string mediaId, string reason)
  {
    // The SSRF pin, at the wire rather than in a unit test. "http:evil.example" is the case PR 1's
    // review found: under RFC 3986 §4.2 a scheme-bearing relative reference resolves as ABSOLUTE, so
    // a deny-list passes it. This asserts the allow-list is what the ROUTE reaches — and it must be
    // a 400 rather than the 409 the disabled gate would give, because validation runs first.
    var response = await _factory.CreateClient().PostAsJsonAsync("/api/audio/events", new
    {
      kind = "RemoteMedia", mediaKind = "GvVoicemail", mediaId, durationSeconds = 12
    });

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    Assert.Equal(reason, (await BodyOf(response)).GetProperty("reason").GetString());
  }

  [Fact]
  public async Task Post_AVoiceIdCarryingASpace_Returns400_VoiceIdHasIllegalCharacter()
  {
    // The VoiceId allow-list at the wire. A caller-supplied voice id ends up inside a synthesis
    // request body, and this seam refuses to hand a structurally-interesting string onward at all.
    var response = await _factory.CreateClient().PostAsJsonAsync("/api/audio/events", new
    {
      kind = "Speech", text = "hello", voiceId = "en-US with space"
    });

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    Assert.Equal(
      "VoiceIdHasIllegalCharacter", (await BodyOf(response)).GetProperty("reason").GetString());
  }

  [Fact]
  public async Task Post_AnUnknownEngine_Returns400_UnknownEngine()
  {
    // A numeric string, because Enum.TryParse accepts one and TTSEngine is numbered from 1 — so
    // without Enum.IsDefined this would be accepted here and blow up at engine resolution instead,
    // arriving as a Failed snapshot rather than a named 400 on the field that caused it.
    var response = await _factory.CreateClient().PostAsJsonAsync("/api/audio/events", new
    {
      kind = "Speech", text = "hello", engine = "0"
    });

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    Assert.Equal("UnknownEngine", (await BodyOf(response)).GetProperty("reason").GetString());
  }

  [Fact]
  public async Task Post_AnOverlongLabel_Returns400_LabelTooLong()
  {
    // Proves the cap is reachable from the route, which is the whole reason it was deferred from
    // PR 2 rather than shipped there as unreachable code.
    var response = await _factory.CreateClient().PostAsJsonAsync("/api/audio/events", new
    {
      kind = "Speech", text = "hello", label = new string('a', 129)
    });

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    Assert.Equal("LabelTooLong", (await BodyOf(response)).GetProperty("reason").GetString());
  }

  [Fact]
  public async Task Post_ABodyWithNoKind_Returns400_UnknownKind_NotAModelBindingError()
  {
    // The DTO's whole justification. Binding EventPlaybackRequest directly would make this a generic
    // required-member 400 with no named reason.
    var response = await _factory.CreateClient()
      .PostAsJsonAsync("/api/audio/events", new { text = "hi" });

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    Assert.Equal("UnknownKind", (await BodyOf(response)).GetProperty("reason").GetString());
  }

  [Fact]
  public async Task Post_ASpeechRequestCarryingAnUnparseableMediaKind_Returns400_ArmMismatch()
  {
    // Pins that Map translates rather than decides: an unrecognised mediaKind becomes an undefined
    // enum value, and Validate reports ArmMismatch because this is the Speech arm — which is the
    // correct reason, and not the one a controller-side parse error would have given.
    var response = await _factory.CreateClient().PostAsJsonAsync("/api/audio/events", new
    {
      kind = "Speech", text = "hi", mediaKind = "NotAThing"
    });

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    Assert.Equal("ArmMismatch", (await BodyOf(response)).GetProperty("reason").GetString());
  }

  [Fact]
  public async Task Get_Current_Returns204_WhenNothingHasBeenStarted()
  {
    // ⚠ ITS OWN HOST, so "nothing has been started" is true BY CONSTRUCTION rather than by luck.
    // Sharing the class fixture made this test depend on no sibling having started a playback, which
    // held only while GvMedia:Enabled shipped false and every RemoteMedia POST was refused. PHN-2
    // flipped that default and this test began failing with 200 — not because Current is wrong, but
    // because its precondition had quietly been an artefact of a config value.
    var response = await _factory.WithWebHostBuilder(_ => { })
      .CreateClient().GetAsync("/api/audio/events/current");

    Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
  }

  [Theory]
  [InlineData("DELETE", "/api/audio/events/evp-nope")]
  [InlineData("POST", "/api/audio/events/evp-nope/pause")]
  [InlineData("POST", "/api/audio/events/evp-nope/resume")]
  public async Task TransportOnAnUnknownPlaybackId_Returns404(string method, string path)
  {
    // 404 rather than 409, because Current has never described this id. A route that was not mapped
    // would also give 404, so the reason string is what separates the two.
    using var request = new HttpRequestMessage(new HttpMethod(method), path);
    var response = await _factory.CreateClient().SendAsync(request);

    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    Assert.Equal("UnknownPlaybackId", (await BodyOf(response)).GetProperty("reason").GetString());
  }

  [Fact]
  public async Task Post_Seek_WithANegativePosition_Returns400()
  {
    // The check runs before the id is resolved, so an unknown id still reports the position problem
    // — which is the more useful of the two answers.
    //
    // ⚠ Only the negative case is asserted at the wire, and the reason is worth stating rather than
    // leaving as a gap: NaN and infinity cannot be EXPRESSED in JSON. System.Text.Json refuses to
    // write them without JsonNumberHandling.AllowNamedFloatingPointLiterals and refuses to read them
    // by default, so a body carrying one never reaches this controller — the model binder rejects it
    // first, with a generic 400. The controller's IsNaN/IsInfinity arms are therefore defence for a
    // caller that is not this one, exactly like the seam validating alongside the controller, and
    // they are cheap because TimeSpan.FromSeconds throws on both.
    var response = await _factory.CreateClient().PostAsJsonAsync(
      "/api/audio/events/evp-nope/seek", new { positionSeconds = -1.0 });

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    Assert.Equal("BadPosition", (await BodyOf(response)).GetProperty("reason").GetString());
  }

  [Fact]
  public async Task Post_RemoteMedia_ReachesTheClientAndFailsWithANamedMediaReason()
  {
    // ⚠ THE TEST THAT PROVES THE SURFACE IS ALIVE, and the only one here that exercises the whole
    // chain: route → DTO → Validate → StartAsync → 202 → background acquisition → GvMediaClient →
    // the auth handler → the failure taxonomy → a Failed snapshot → GET current.
    //
    // GvMedia:Enabled is turned ON and BaseUrl is pointed at 127.0.0.1:1, where a connection is
    // refused immediately on every platform — no real network, no GV auth clock, no timeout to wait
    // out. The expected end state is Failed with "MediaTransport".
    //
    // What it can actually catch: a 202 that never starts acquisition, an acquisition wired to the
    // request's cancellation token, a taxonomy that collapsed the reasons, a snapshot never
    // published, or Current never storing it.
    //
    // ⚠ On that second one, precisely: an acquisition cancelled with the request's own token would
    // NOT report Stopped. The OperationCanceledException arm of AcquireAndPlayAsync claims the
    // terminal flag, tears down and publishes NOTHING — so the playback would sit at Preparing
    // forever and PollUntilTerminalAsync below would fail on its deadline with "never left
    // Preparing", which is exactly the message that names the cause.
    var cacheDirectory = Path.Combine(
      Path.GetTempPath(), "evp-route-tests-" + Guid.NewGuid().ToString("N"));

    try
    {
      // ⚠ CacheDirectory is redirected too. CustomWebApplicationFactory gives each host its own
      // storage root but does NOT cover GvMedia:CacheDirectory, which ships as the relative
      // "./data/gvmedia" — so without this a fetch would write into the repo.
      var client = _factory.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) =>
        c.AddInMemoryCollection(new Dictionary<string, string?>
        {
          ["GvMedia:Enabled"] = "true",
          ["GvMedia:BaseUrl"] = "http://127.0.0.1:1",
          ["GvMedia:FetchTimeoutSeconds"] = "3",
          ["GvMedia:CacheDirectory"] = cacheDirectory
        }))).CreateClient();

      var accepted = await client.PostAsJsonAsync("/api/audio/events", new
      {
        kind = "RemoteMedia",
        mediaKind = "GvVoicemail",
        mediaId = "vm-abc123",
        durationSeconds = 12,
        label = "Voicemail from Jane"
      });

      Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
      var start = await BodyOf(accepted);
      Assert.Equal("Preparing", start.GetProperty("state").GetString());
      var id = start.GetProperty("id").GetString();
      Assert.StartsWith("evp-", id, StringComparison.Ordinal);
      Assert.Equal("Voicemail from Jane", start.GetProperty("label").GetString());

      var final = await PollUntilTerminalAsync(client, TimeSpan.FromSeconds(15));

      Assert.Equal("Failed", final.GetProperty("state").GetString());
      Assert.Equal("MediaTransport", final.GetProperty("failureReason").GetString());
      Assert.Equal(id, final.GetProperty("id").GetString());

      // ⚠ THE 404→409 RULE, END TO END, and this is the only place it is pinned at the wire. Current
      // RETAINS this snapshot after the playback ended, so the id is still one Current describes — a
      // transport call against it is "the playback cannot do that right now" (409), not "you
      // invented that id" (404). Every other route test here uses "evp-nope", an id Current has
      // never described, and so can only ever exercise the 404 half. This test is the one that
      // already has a real, finished playback to ask about.
      using var pause = new HttpRequestMessage(HttpMethod.Post, $"/api/audio/events/{id}/pause");
      var refused = await client.SendAsync(pause);

      Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
      Assert.Equal("NotPlaying", (await BodyOf(refused)).GetProperty("reason").GetString());

      // And DELETE agrees with pause about the same id, which it did not until PHN-1c's review: it
      // answered a flat 404 for every refusal, contradicting Transport's own documented rule.
      using var stop = new HttpRequestMessage(HttpMethod.Delete, $"/api/audio/events/{id}");
      var refusedStop = await client.SendAsync(stop);

      Assert.Equal(HttpStatusCode.Conflict, refusedStop.StatusCode);
      Assert.Equal("NotStoppable", (await BodyOf(refusedStop)).GetProperty("reason").GetString());
    }
    finally
    {
      try
      {
        if (Directory.Exists(cacheDirectory))
        {
          Directory.Delete(cacheDirectory, recursive: true);
        }
      }
      catch (Exception)
      {
        // Best effort — a leftover temp directory must never fail a test during teardown.
      }
    }
  }

  /// <summary>
  /// Polls GET current until the state is no longer Preparing, and FAILS on the deadline.
  /// </summary>
  /// <remarks>
  /// ⚠ It fails rather than returning the last snapshot it saw. A helper that returned whatever it
  /// last observed would let the caller pass against a service that never left Preparing, which is
  /// the exact failure mode it exists to catch.
  /// </remarks>
  private static async Task<JsonElement> PollUntilTerminalAsync(HttpClient client, TimeSpan timeout)
  {
    var deadline = DateTime.UtcNow + timeout;
    while (DateTime.UtcNow < deadline)
    {
      var response = await client.GetAsync("/api/audio/events/current");
      if (response.StatusCode == HttpStatusCode.OK)
      {
        var snapshot = await response.Content.ReadFromJsonAsync<JsonElement>();
        if (snapshot.GetProperty("state").GetString() != "Preparing")
        {
          return snapshot;
        }
      }

      await Task.Delay(50);   // a poll INSIDE a bounded wait, not a sleep before an assertion
    }

    Assert.Fail($"The playback never left Preparing within {timeout}.");
    throw new InvalidOperationException("unreachable");
  }
}
