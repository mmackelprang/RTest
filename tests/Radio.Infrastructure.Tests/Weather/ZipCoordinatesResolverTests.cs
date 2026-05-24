using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;
using Radio.Infrastructure.Weather;

namespace Radio.Infrastructure.Tests.Weather;

/// <summary>
/// Tests for the fallback-table-then-network ZIP-to-coords resolver.
/// </summary>
public class ZipCoordinatesResolverTests
{
  [Fact]
  public async Task ResolveAsync_DefaultZip_HitsFallbackTable_WithoutNetwork()
  {
    var (handler, factory) = CreateHandlerAndFactory(_ => throw new InvalidOperationException("Network should not be called for the default ZIP"));

    var resolver = new ZipCoordinatesResolver(factory.Object, NullLogger<ZipCoordinatesResolver>.Instance);
    var result = await resolver.ResolveAsync("27312");

    Assert.NotNull(result);
    Assert.Equal("27312", result!.Zip);
    Assert.Equal("Pittsboro", result.PlaceName);
    Assert.Equal("NC", result.StateAbbreviation);
    Assert.Equal("Pittsboro, NC", result.LocationName);
    // Handler should not have been invoked — the fallback path returns first.
    handler.Protected().Verify("SendAsync", Times.Never(),
      ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>());
  }

  [Fact]
  public async Task ResolveAsync_UnknownZip_FetchesFromZippopotam()
  {
    const string responseJson = """
      {
        "post code": "10001",
        "country": "United States",
        "country abbreviation": "US",
        "places": [
          {
            "place name": "New York",
            "longitude": "-73.9967",
            "state": "New York",
            "state abbreviation": "NY",
            "latitude": "40.7484"
          }
        ]
      }
      """;

    var (_, factory) = CreateHandlerAndFactory(_ => new HttpResponseMessage(HttpStatusCode.OK)
    {
      Content = new StringContent(responseJson, System.Text.Encoding.UTF8, "application/json"),
    });

    var resolver = new ZipCoordinatesResolver(factory.Object, NullLogger<ZipCoordinatesResolver>.Instance);
    var result = await resolver.ResolveAsync("10001");

    Assert.NotNull(result);
    Assert.Equal("10001", result!.Zip);
    Assert.Equal("New York", result.PlaceName);
    Assert.Equal("NY", result.StateAbbreviation);
    Assert.Equal(40.7484m, result.Latitude);
    Assert.Equal(-73.9967m, result.Longitude);
  }

  [Fact]
  public async Task ResolveAsync_NotFoundFromZippopotam_ReturnsNull()
  {
    var (_, factory) = CreateHandlerAndFactory(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

    var resolver = new ZipCoordinatesResolver(factory.Object, NullLogger<ZipCoordinatesResolver>.Instance);
    var result = await resolver.ResolveAsync("99999");

    Assert.Null(result);
  }

  [Fact]
  public async Task ResolveAsync_NetworkFailure_ReturnsNull()
  {
    var (_, factory) = CreateHandlerAndFactory(_ => throw new HttpRequestException("Network down"));

    var resolver = new ZipCoordinatesResolver(factory.Object, NullLogger<ZipCoordinatesResolver>.Instance);
    // 10001 is not in the fallback table so it has to hit the network; the
    // exception above simulates a network failure.
    var result = await resolver.ResolveAsync("10001");

    Assert.Null(result);
  }

  [Theory]
  [InlineData("")]
  [InlineData(null)]
  [InlineData("1234")]      // 4 digits
  [InlineData("123456")]    // 6 digits
  [InlineData("abcde")]     // non-numeric
  [InlineData("12 34")]     // space
  public async Task ResolveAsync_InvalidZip_ReturnsNullWithoutNetwork(string? zip)
  {
    var (handler, factory) = CreateHandlerAndFactory(_ => throw new InvalidOperationException("Network should not be called for an invalid ZIP"));

    var resolver = new ZipCoordinatesResolver(factory.Object, NullLogger<ZipCoordinatesResolver>.Instance);
    var result = await resolver.ResolveAsync(zip!);

    Assert.Null(result);
    handler.Protected().Verify("SendAsync", Times.Never(),
      ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>());
  }

  [Theory]
  [InlineData("12345", true)]
  [InlineData("00000", true)]
  [InlineData("99999", true)]
  [InlineData("", false)]
  [InlineData(null, false)]
  [InlineData("1234", false)]
  [InlineData("123456", false)]
  [InlineData("12a45", false)]
  [InlineData("1234 ", false)]
  public void IsValidZip_HandlesEdgeCases(string? zip, bool expected)
  {
    Assert.Equal(expected, ZipCoordinatesResolver.IsValidZip(zip));
  }

  // ────────────────────────── helpers ──────────────────────────

  private static (Mock<HttpMessageHandler>, Mock<IHttpClientFactory>) CreateHandlerAndFactory(
    Func<HttpRequestMessage, HttpResponseMessage> respond)
  {
    var handler = new Mock<HttpMessageHandler>(MockBehavior.Strict);
    handler.Protected()
      .Setup<Task<HttpResponseMessage>>(
        "SendAsync",
        ItExpr.IsAny<HttpRequestMessage>(),
        ItExpr.IsAny<CancellationToken>())
      .Returns<HttpRequestMessage, CancellationToken>((req, _) => Task.FromResult(respond(req)));

    var client = new HttpClient(handler.Object) { BaseAddress = new Uri("https://api.zippopotam.us") };
    var factory = new Mock<IHttpClientFactory>();
    factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(client);
    return (handler, factory);
  }
}
