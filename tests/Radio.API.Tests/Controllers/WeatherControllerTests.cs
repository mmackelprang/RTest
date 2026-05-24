using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Radio.API.Controllers;
using Radio.Core.Configuration;
using Radio.Core.Interfaces;
using Radio.Core.Models;

namespace Radio.API.Tests.Controllers;

/// <summary>
/// Tests for the WeatherController HTTP semantics (ADR-022 §2.7).
/// </summary>
public class WeatherControllerTests
{
  [Fact]
  public async Task GetForecast_FeatureDisabled_Returns404()
  {
    var service = new Mock<IWeatherService>(MockBehavior.Strict);
    var controller = CreateController(service, new WeatherDisplayOptions { Enabled = false });

    var result = await controller.GetForecast(zip: null);

    var actionResult = Assert.IsType<ActionResult<WeatherForecast>>(result);
    var notFound = Assert.IsType<NotFoundObjectResult>(actionResult.Result);
    Assert.Equal(404, notFound.StatusCode);
    // Service must NOT be called when the feature is disabled.
    service.Verify(s => s.GetForecastAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
  }

  [Theory]
  [InlineData("")]
  [InlineData("123")]
  [InlineData("1234")]
  [InlineData("123456")]
  [InlineData("abcde")]
  [InlineData("12a45")]
  public async Task GetForecast_InvalidZip_Returns400(string badZip)
  {
    var service = new Mock<IWeatherService>(MockBehavior.Strict);
    var controller = CreateController(service, new WeatherDisplayOptions { Enabled = true, Zip = badZip });

    var result = await controller.GetForecast(zip: null);

    var actionResult = Assert.IsType<ActionResult<WeatherForecast>>(result);
    var badRequest = Assert.IsType<BadRequestObjectResult>(actionResult.Result);
    Assert.Equal(400, badRequest.StatusCode);
  }

  [Fact]
  public async Task GetForecast_QueryStringOverridesConfig()
  {
    var service = new Mock<IWeatherService>(MockBehavior.Strict);
    var forecast = BuildForecast("10001");
    service
      .Setup(s => s.GetForecastAsync("10001", It.IsAny<CancellationToken>()))
      .ReturnsAsync(forecast);

    var controller = CreateController(service, new WeatherDisplayOptions { Enabled = true, Zip = "27312" });

    var result = await controller.GetForecast(zip: "10001");

    var actionResult = Assert.IsType<ActionResult<WeatherForecast>>(result);
    var ok = Assert.IsType<OkObjectResult>(actionResult.Result);
    var returned = Assert.IsType<WeatherForecast>(ok.Value);
    Assert.Equal("10001", returned.Zip);
  }

  [Fact]
  public async Task GetForecast_HappyPath_Returns200WithForecast()
  {
    var service = new Mock<IWeatherService>(MockBehavior.Strict);
    var forecast = BuildForecast("27312");
    service
      .Setup(s => s.GetForecastAsync("27312", It.IsAny<CancellationToken>()))
      .ReturnsAsync(forecast);

    var controller = CreateController(service, new WeatherDisplayOptions { Enabled = true, Zip = "27312" });

    var result = await controller.GetForecast(zip: null);

    var actionResult = Assert.IsType<ActionResult<WeatherForecast>>(result);
    var ok = Assert.IsType<OkObjectResult>(actionResult.Result);
    var returned = Assert.IsType<WeatherForecast>(ok.Value);
    Assert.Equal("27312", returned.Zip);
    Assert.Equal(3, returned.Days.Count);
  }

  [Fact]
  public async Task GetForecast_ServiceReturnsNull_Returns503()
  {
    var service = new Mock<IWeatherService>(MockBehavior.Strict);
    service
      .Setup(s => s.GetForecastAsync("27312", It.IsAny<CancellationToken>()))
      .ReturnsAsync((WeatherForecast?)null);

    var controller = CreateController(service, new WeatherDisplayOptions { Enabled = true, Zip = "27312" });

    var result = await controller.GetForecast(zip: null);

    var actionResult = Assert.IsType<ActionResult<WeatherForecast>>(result);
    var status = Assert.IsType<ObjectResult>(actionResult.Result);
    Assert.Equal(503, status.StatusCode);
  }

  [Fact]
  public async Task GetForecast_ServiceThrows_Returns503()
  {
    var service = new Mock<IWeatherService>(MockBehavior.Strict);
    service
      .Setup(s => s.GetForecastAsync("27312", It.IsAny<CancellationToken>()))
      .ThrowsAsync(new InvalidOperationException("upstream blew up"));

    var controller = CreateController(service, new WeatherDisplayOptions { Enabled = true, Zip = "27312" });

    var result = await controller.GetForecast(zip: null);

    var actionResult = Assert.IsType<ActionResult<WeatherForecast>>(result);
    var status = Assert.IsType<ObjectResult>(actionResult.Result);
    Assert.Equal(503, status.StatusCode);
  }

  [Fact]
  public async Task GetForecast_CancellationPropagates()
  {
    var service = new Mock<IWeatherService>(MockBehavior.Strict);
    service
      .Setup(s => s.GetForecastAsync("27312", It.IsAny<CancellationToken>()))
      .ThrowsAsync(new OperationCanceledException());

    var controller = CreateController(service, new WeatherDisplayOptions { Enabled = true, Zip = "27312" });
    using var cts = new CancellationTokenSource();
    cts.Cancel();

    await Assert.ThrowsAsync<OperationCanceledException>(() => controller.GetForecast(zip: null, ct: cts.Token));
  }

  // ────────────────────────── helpers ──────────────────────────

  private static WeatherController CreateController(
    IMock<IWeatherService> service,
    WeatherDisplayOptions options)
  {
    return new WeatherController(
      service.Object,
      new TestMonitor<WeatherDisplayOptions>(options),
      NullLogger<WeatherController>.Instance);
  }

  private static WeatherForecast BuildForecast(string zip) => new(
    Zip: zip,
    LocationName: "Test City, ST",
    GeneratedAtUtc: DateTimeOffset.UtcNow,
    FetchedAtUtc: DateTimeOffset.UtcNow,
    IsStale: false,
    Days: new List<WeatherDay>
    {
      new(new DateOnly(2026, 5, 23), "Today", 75, 60, 24, 16, "Sunny", "Sunny", 0, "sunny", null),
      new(new DateOnly(2026, 5, 24), "Tomorrow", 78, 62, 26, 17, "Partly Cloudy", "Partly Cloudy", 20, "mostly-sunny", null),
      new(new DateOnly(2026, 5, 25), "Mon", 70, 55, 21, 13, "Showers", "Showers", 60, "rain", null),
    },
    Current: null);

  private sealed class TestMonitor<T> : IOptionsMonitor<T>
  {
    private readonly T _value;
    public TestMonitor(T value) => _value = value;
    public T CurrentValue => _value;
    public T Get(string? name) => _value;
    public IDisposable OnChange(Action<T, string?> listener) => NullDisposable.Instance;

    private sealed class NullDisposable : IDisposable
    {
      public static readonly NullDisposable Instance = new();
      public void Dispose() { }
    }
  }
}
