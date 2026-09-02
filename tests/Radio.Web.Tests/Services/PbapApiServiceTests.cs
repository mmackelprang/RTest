using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Radio.Web.Services.ApiClients;
using Radio.Web.Tests.TestHelpers;

namespace Radio.Web.Tests.Services;

/// <summary>
/// Tests <see cref="PbapApiService.LookupNumberAsync"/> — the Web-reachable
/// per-number contact lookup the Messages feed uses as its async resolution
/// fallback (Task #6). A 200 yields Found+name; a 404 yields NotFound (a
/// definitive answer the caller may cache); any other failure yields Unavailable
/// (transient — must NOT be cached). Never throws.
/// </summary>
public class PbapApiServiceTests
{
  private static readonly JsonSerializerOptions JsonOptions = new()
  {
    PropertyNameCaseInsensitive = true
  };

  private static PbapApiService Create(HttpClient httpClient) =>
    new(httpClient, NullLogger<PbapApiService>.Instance);

  [Fact]
  public async Task LookupNumberAsync_ReturnsFoundName_OnMatch()
  {
    var handler = new MockHttpHandler(
      JsonSerializer.Serialize(new { DisplayName = "Jane Doe", PhoneNumber = "9193718044" }, JsonOptions));
    var http = new HttpClient(handler) { BaseAddress = new Uri(HermeticTestRig.ApiBaseUrl) };

    var (outcome, name) = await Create(http).LookupNumberAsync("9193718044");

    Assert.Equal(ContactLookupOutcome.Found, outcome);
    Assert.Equal("Jane Doe", name);
  }

  [Fact]
  public async Task LookupNumberAsync_ReturnsNotFound_On404()
  {
    var handler = new MockHttpHandler(statusCode: HttpStatusCode.NotFound);
    var http = new HttpClient(handler) { BaseAddress = new Uri(HermeticTestRig.ApiBaseUrl) };

    var (outcome, name) = await Create(http).LookupNumberAsync("9995551212");

    Assert.Equal(ContactLookupOutcome.NotFound, outcome);
    Assert.Null(name);
  }

  [Fact]
  public async Task LookupNumberAsync_ReturnsUnavailable_OnServerError()
  {
    // A 5xx is a transient failure, NOT a definitive "no contact" — the caller must
    // be able to tell them apart so it doesn't cache a hiccup as a permanent miss.
    var handler = new MockHttpHandler(statusCode: HttpStatusCode.InternalServerError);
    var http = new HttpClient(handler) { BaseAddress = new Uri(HermeticTestRig.ApiBaseUrl) };

    var (outcome, name) = await Create(http).LookupNumberAsync("9193718044");

    Assert.Equal(ContactLookupOutcome.Unavailable, outcome);
    Assert.Null(name);
  }

  [Fact]
  public async Task LookupNumberAsync_ReturnsNotFound_ForBlankNumber_WithoutHittingNetwork()
  {
    var handler = new MockHttpHandler("{}");
    var http = new HttpClient(handler) { BaseAddress = new Uri(HermeticTestRig.ApiBaseUrl) };

    var (outcome, name) = await Create(http).LookupNumberAsync("   ");

    Assert.Equal(ContactLookupOutcome.NotFound, outcome);
    Assert.Null(name);
    Assert.Equal(0, handler.RequestCount);
  }
}
