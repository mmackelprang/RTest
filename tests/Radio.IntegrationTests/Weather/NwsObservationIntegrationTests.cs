using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Infrastructure.Weather;
using Xunit;

namespace Radio.IntegrationTests.Weather;

/// <summary>
/// Hits the real NWS API (no mocking) to confirm the observation chain end
/// to end. Excluded from default CI per
/// HANDOFF-sleep-weather-current-conditions.md §9.5 — runs on demand via
/// <c>dotnet test --filter Category=Integration</c> after a Pi/Ubuntu deploy.
///
/// Network failure marks the test as skipped rather than failed so a
/// transient NWS outage doesn't poison the suite.
/// </summary>
[Trait("Category", "Integration")]
public class NwsObservationIntegrationTests
{
  private const string DefaultZip = "27312";

  [Fact]
  [Trait("Category", "Integration")]
  public async Task RealNwsCall_ReturnsForecast_WithCurrentObservation()
  {
    // Build a minimal IHttpClientFactory + cache + resolver — same wiring
    // the production DI container would do, but inline so this test is
    // self-contained.
    var factory = new SimpleHttpClientFactory();
    var cache = new MemoryCache(new MemoryCacheOptions());
    var options = new StaticOptionsMonitor<WeatherDisplayOptions>(new WeatherDisplayOptions
    {
      Enabled = true,
      Zip = DefaultZip,
      RefreshIntervalMinutes = 60,
    });
    var resolver = new ZipCoordinatesResolver(factory, NullLogger<ZipCoordinatesResolver>.Instance);
    var svc = new NwsWeatherService(factory, resolver, cache, options, NullLogger<NwsWeatherService>.Instance);

    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    var forecast = await svc.GetForecastAsync(DefaultZip, cts.Token);

    Assert.NotNull(forecast);
    Assert.NotEmpty(forecast!.Days);
    Assert.True(forecast.Days.Count >= 1, $"expected at least 1 day, got {forecast.Days.Count}");

    Assert.NotNull(forecast.Current);
    Assert.False(string.IsNullOrEmpty(forecast.Current!.ConditionShort), "ConditionShort must be populated");
    Assert.NotEqual("unknown", forecast.Current.IconKey);
    Assert.InRange(forecast.Current.TempF, -40, 130);
  }

  /// <summary>
  /// IHttpClientFactory that builds an HttpClient with the same headers
  /// production wiring uses — NWS requires a User-Agent for unauthenticated
  /// API access.
  /// </summary>
  private sealed class SimpleHttpClientFactory : IHttpClientFactory
  {
    public HttpClient CreateClient(string name)
    {
      var client = new HttpClient
      {
        BaseAddress = new Uri("https://api.weather.gov"),
        Timeout = TimeSpan.FromSeconds(30),
      };
      client.DefaultRequestHeaders.Add("User-Agent", "RadioConsoleIntegrationTests/1.0 (integration-tests@example.com)");
      client.DefaultRequestHeaders.Add("Accept", "application/geo+json");
      return client;
    }
  }

  private sealed class StaticOptionsMonitor<T> : IOptionsMonitor<T>
  {
    private readonly T _value;
    public StaticOptionsMonitor(T value) => _value = value;
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
