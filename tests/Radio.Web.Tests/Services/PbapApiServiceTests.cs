using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Radio.Web.Services.ApiClients;
using Radio.Web.Tests.TestHelpers;

namespace Radio.Web.Tests.Services;

/// <summary>
/// Tests <see cref="PbapApiService.LookupNumberAsync"/> — the Web-reachable
/// per-number contact lookup the Messages feed uses as its async resolution
/// fallback (Task #6). A 200 yields the display name; a 404 (no match) or any
/// non-success yields null, never an exception.
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
  public async Task LookupNumberAsync_ReturnsName_OnMatch()
  {
    var handler = new MockHttpHandler(
      JsonSerializer.Serialize(new { DisplayName = "Jane Doe", PhoneNumber = "9193718044" }, JsonOptions));
    var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5000") };

    var name = await Create(http).LookupNumberAsync("9193718044");

    Assert.Equal("Jane Doe", name);
  }

  [Fact]
  public async Task LookupNumberAsync_ReturnsNull_OnNotFound()
  {
    var handler = new MockHttpHandler(statusCode: HttpStatusCode.NotFound);
    var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5000") };

    var name = await Create(http).LookupNumberAsync("9995551212");

    Assert.Null(name);
  }

  [Fact]
  public async Task LookupNumberAsync_ReturnsNull_OnServerError()
  {
    var handler = new MockHttpHandler(statusCode: HttpStatusCode.InternalServerError);
    var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5000") };

    var name = await Create(http).LookupNumberAsync("9193718044");

    Assert.Null(name);
  }

  [Fact]
  public async Task LookupNumberAsync_ReturnsNull_ForBlankNumber_WithoutHittingNetwork()
  {
    var handler = new MockHttpHandler("{}");
    var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5000") };

    var name = await Create(http).LookupNumberAsync("   ");

    Assert.Null(name);
    Assert.Equal(0, handler.RequestCount);
  }
}
