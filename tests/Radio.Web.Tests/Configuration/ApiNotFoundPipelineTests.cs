namespace Radio.Web.Tests.Configuration;

using System.Net;
using System.Text.Json;
using Radio.Web.Tests.TestHelpers;

/// <summary>
/// Pins the two halves of Radio.Web's unmatched-path contract against the real Program.cs pipeline.
/// </summary>
/// <remarks>
/// <para>
/// UI-11. The row was filed believing an unmatched <c>/api/*</c> path returned <c>200</c> with an SPA
/// shell. It does not, and never did — this app is a Blazor Web App
/// (<c>MapRazorComponents</c>), which routes by real per-page endpoints and has no fallback; the
/// reported symptom came from the other repo's service. See
/// <c>design/plans/UI-11-the-404-that-was-already-a-404.md</c> §0.1-0.2.
/// </para>
/// <para>
/// ⚠ <b>The pair is the point, and neither half is sufficient alone.</b> A change that 404s
/// <c>/api/*</c> by 404ing <em>everything</em> would satisfy the first group and break the console;
/// a change that serves the shell for everything would satisfy the second and reintroduce the bug the
/// row describes. Do not delete one group to make the other pass.
/// </para>
/// </remarks>
public class ApiNotFoundPipelineTests : IClassFixture<RadioWebFactory>
{
  private readonly RadioWebFactory _factory;

  public ApiNotFoundPipelineTests(RadioWebFactory factory) => _factory = factory;

  /// <summary>Every <c>@page</c> route in the app, read out of the tree at <c>ab72bef3</c>.</summary>
  public static TheoryData<string> SpaDeepLinks() => new()
  {
    "/", "/bare", "/bluetooth", "/devices", "/Error", "/history",
    "/metrics", "/minimal", "/phone", "/radio", "/sleep", "/system",
  };

  // ─── Direction 1: an unmatched /api/* path must never look like a success ──────────────────

  [Theory]
  [InlineData("/api/definitely-not-a-route")]
  [InlineData("/api/gvsms/")]            // the exact probe that cost RotaryPhone a request
  [InlineData("/api/albumart")]          // the real route minus its required {filename}
  [InlineData("/api")]
  [InlineData("/API/Definitely/Not/A/Route")]  // routing is case-insensitive; so is the rule
  public async Task UnmatchedApiPath_Returns404_AndNeverAnHtmlPage(string path)
  {
    var response = await _factory.CreateClient().GetAsync(path);
    var body = await response.Content.ReadAsStringAsync();

    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    Assert.NotEqual("text/html", response.Content.Headers.ContentType?.MediaType);
    Assert.DoesNotContain("blazor.web.js", body);
    Assert.DoesNotContain("<!DOCTYPE html>", body, StringComparison.OrdinalIgnoreCase);
  }

  [Fact]
  public async Task UnmatchedApiPath_AnswersAJsonCallerWithAProblemBody()
  {
    var response = await _factory.CreateClient().GetAsync("/api/definitely-not-a-route");
    var body = await response.Content.ReadAsStringAsync();

    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

    using var doc = JsonDocument.Parse(body);
    Assert.Equal(404, doc.RootElement.GetProperty("status").GetInt32());
    Assert.Equal("Not Found", doc.RootElement.GetProperty("title").GetString());
  }

  /// <summary>
  /// PHN-5. The paths people mistype here carry phone numbers and thread ids, and Radio.Web's Console
  /// sink is unrestricted, so nothing request-derived may reach the body or the journal.
  /// </summary>
  /// <remarks>
  /// ⚠ The <c>DoesNotContain</c> assertions below are <b>vacuously true of an empty body</b> — which is
  /// exactly what this endpoint returned before UI-11. The two assertions above them are therefore
  /// load-bearing: they prove a body exists and is well-formed, so the absence checks are absence and
  /// not emptiness. Do not reorder or remove them.
  /// </remarks>
  [Fact]
  public async Task UnmatchedApiPath_BodyEchoesNothingFromTheRequest()
  {
    const string path = "/api/gvbridge/sms/threads/8015550137?token=sekrit";

    var response = await _factory.CreateClient().GetAsync(path);
    var body = await response.Content.ReadAsStringAsync();

    Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    using (var doc = JsonDocument.Parse(body))
    {
      Assert.Equal(404, doc.RootElement.GetProperty("status").GetInt32());
    }

    Assert.DoesNotContain("8015550137", body);
    Assert.DoesNotContain("sekrit", body);
    Assert.DoesNotContain("token", body);
    Assert.DoesNotContain("threads", body);
    Assert.DoesNotContain("gvbridge", body);
  }

  /// <summary>
  /// The <c>/api/</c> scope of the terminal rule is real: an unmatched path that is NOT under
  /// <c>/api/</c> must not be answered with the API problem document.
  /// </summary>
  /// <remarks>
  /// <para>
  /// ⭐ <b>This test exists because mutation testing found the gap it closes.</b> Widening the route
  /// pattern from <c>/api/{**rest}</c> to <c>/{**rest}</c> left the entire suite <b>20/20 green</b> —
  /// including all twelve deep-link assertions, which the plan had nominated as the guard against
  /// exactly that widening. They cannot detect it: <c>MapFallback</c> stamps
  /// <c>Order = int.MaxValue</c>, so it loses to every real <c>@page</c> endpoint no matter how wide
  /// its pattern is. A wider pattern therefore does <em>not</em> "black out the console" — the plan's
  /// stated reason for the narrow scope is false, and nothing was testing the scope at all.
  /// </para>
  /// <para>
  /// What a wider pattern actually breaks is <b>truthfulness</b>. <c>/some-typo</c> is a mistyped
  /// <em>page</em>, and answering it with "No API route on this service matches the request path"
  /// tells the caller something untrue about what they asked for — a wrong answer wearing a
  /// well-formed body, which is this repo's most expensive recurring defect class.
  /// </para>
  /// <para>
  /// ⚠ The differential is what makes this non-vacuous. The first assertion proves the terminal rule
  /// is live and reachable, so the second is testing the <em>scope</em> rather than passing because
  /// the endpoint was absent. Do not split them into two tests.
  /// </para>
  /// </remarks>
  [Theory]
  [InlineData("/definitely-not-a-page")]
  [InlineData("/phone/nope")]
  [InlineData("/apitypo")]  // shares a prefix with /api but is not under it
  public async Task UnmatchedNonApiPath_DoesNotGetTheApiProblemBody(string pagePath)
  {
    var client = _factory.CreateClient();

    // Non-vacuity guard: the terminal /api/ rule must actually be live for the check below to mean
    // anything. If this assertion ever fails, the one after it is proving nothing.
    var apiResponse = await client.GetAsync("/api/definitely-not-a-route");
    Assert.Equal("application/problem+json", apiResponse.Content.Headers.ContentType?.MediaType);

    var pageResponse = await client.GetAsync(pagePath);
    var pageBody = await pageResponse.Content.ReadAsStringAsync();

    Assert.Equal(HttpStatusCode.NotFound, pageResponse.StatusCode);
    Assert.NotEqual("application/problem+json", pageResponse.Content.Headers.ContentType?.MediaType);
    Assert.DoesNotContain("No API route on this service", pageBody);
  }

  // ─── Direction 2: real routes still win, and every SPA deep link still serves the shell ────

  /// <summary>
  /// Proves the terminal rule does not shadow a real <c>/api/*</c> endpoint.
  /// </summary>
  /// <remarks>
  /// <c>/api/health/version</c> is the whole proof deliberately: it is served in-process from the
  /// assembly's own build info, so it is deterministic. The other real route,
  /// <c>/api/albumart/{filename}</c>, proxies outward to Radio.API and its status therefore depends on
  /// whether a Radio.API happens to be listening on the developer's box — an unstable oracle, and a
  /// timing-dependent one. It is covered by the theory above only in its unmatched form.
  /// </remarks>
  [Fact]
  public async Task RealApiRoute_StillWins_AndIsNotShadowedByTheTerminalRule()
  {
    var response = await _factory.CreateClient().GetAsync("/api/health/version");
    var body = await response.Content.ReadAsStringAsync();

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    Assert.Contains("gitSha", body, StringComparison.OrdinalIgnoreCase);
  }

  [Theory]
  [MemberData(nameof(SpaDeepLinks))]
  public async Task SpaDeepLink_StillServesTheAppShell(string path)
  {
    var response = await _factory.CreateClient().GetAsync(path);
    var body = await response.Content.ReadAsStringAsync();

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
    Assert.Contains("_framework/blazor.web.js", body);
    Assert.Contains("<title>Radio Console</title>", body);
  }
}
