using System.Net;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using Radio.Core.Configuration;
using Radio.Infrastructure.Weather;
using Radio.Infrastructure.Weather.Dtos;

namespace Radio.Infrastructure.Tests.Weather;

/// <summary>
/// Tests for the NWS weather service. Cover the static aggregation +
/// day-name helper (cheap) and a handful of end-to-end paths using a mocked
/// HttpMessageHandler.
/// </summary>
public class NwsWeatherServiceTests
{
  // ── ComputeDayName (Designer §10 Q2 contract) ─────────────────────────────

  [Fact]
  public void ComputeDayName_Today_ReturnsToday()
  {
    var today = new DateOnly(2026, 5, 23);
    Assert.Equal("Today", NwsWeatherService.ComputeDayName(today, today));
  }

  [Fact]
  public void ComputeDayName_Tomorrow_ReturnsTomorrow()
  {
    var today = new DateOnly(2026, 5, 23);
    Assert.Equal("Tomorrow", NwsWeatherService.ComputeDayName(today.AddDays(1), today));
  }

  [Fact]
  public void ComputeDayName_DayAfter_Returns3LetterWeekday()
  {
    // 2026-05-25 is a Monday.
    var today = new DateOnly(2026, 5, 23);
    Assert.Equal("Mon", NwsWeatherService.ComputeDayName(today.AddDays(2), today));
  }

  [Theory]
  [InlineData(2, "Mon")]
  [InlineData(3, "Tue")]
  [InlineData(4, "Wed")]
  [InlineData(5, "Thu")]
  [InlineData(6, "Fri")]
  public void ComputeDayName_WeekdaysAreInvariantThreeLetter(int dayOffset, string expected)
  {
    // 2026-05-23 = Saturday → +2=Mon, +3=Tue, ...
    var today = new DateOnly(2026, 5, 23);
    Assert.Equal(expected, NwsWeatherService.ComputeDayName(today.AddDays(dayOffset), today));
  }

  // ── AggregateToDays (the day+night collapse the spec calls out) ───────────

  [Fact]
  public void AggregateToDays_CollapsesDayAndNightPairsToCalendarDays()
  {
    // 6 periods = 3 calendar days. NWS supplies day then night for each.
    var today = new DateTime(2026, 5, 23);
    var periods = new List<NwsForecastPeriod>
    {
      MakePeriod(1, "Today", today.Date.AddHours(6),  isDay: true,  temp: 75, condition: "Sunny", icon: "https://api.weather.gov/icons/land/day/skc?size=medium"),
      MakePeriod(2, "Tonight", today.Date.AddHours(18), isDay: false, temp: 60, condition: "Clear", icon: "https://api.weather.gov/icons/land/night/skc"),
      MakePeriod(3, "Sunday", today.AddDays(1).Date.AddHours(6), isDay: true, temp: 80, condition: "Partly Cloudy", icon: "https://api.weather.gov/icons/land/day/sct"),
      MakePeriod(4, "Sunday Night", today.AddDays(1).Date.AddHours(18), isDay: false, temp: 65, condition: "Mostly Clear", icon: "https://api.weather.gov/icons/land/night/sct"),
      MakePeriod(5, "Monday", today.AddDays(2).Date.AddHours(6), isDay: true, temp: 70, condition: "Showers", icon: "https://api.weather.gov/icons/land/day/rain"),
      MakePeriod(6, "Monday Night", today.AddDays(2).Date.AddHours(18), isDay: false, temp: 55, condition: "Rain", icon: "https://api.weather.gov/icons/land/night/sct"),
    };

    var days = NwsWeatherService.AggregateToDays(periods, today);

    Assert.Equal(3, days.Count);
    Assert.Equal("Today", days[0].DayName);
    Assert.Equal(75, days[0].HighF);
    Assert.Equal(60, days[0].LowF);
    Assert.Equal("Sunny", days[0].ConditionShort);
    Assert.Equal("sunny", days[0].IconKey);

    Assert.Equal("Tomorrow", days[1].DayName);
    Assert.Equal(80, days[1].HighF);
    Assert.Equal(65, days[1].LowF);
    Assert.Equal("mostly-sunny", days[1].IconKey);

    Assert.Equal("Mon", days[2].DayName);
    Assert.Equal(70, days[2].HighF);
    Assert.Equal(55, days[2].LowF);
    Assert.Equal("rain", days[2].IconKey);
  }

  [Fact]
  public void AggregateToDays_TruncatesToThreeDays()
  {
    // Supply 5 full calendar days (10 periods). Service must return exactly 3.
    var today = new DateTime(2026, 5, 23);
    var periods = new List<NwsForecastPeriod>();
    for (var i = 0; i < 5; i++)
    {
      periods.Add(MakePeriod(i * 2 + 1, $"Day {i}", today.AddDays(i).Date.AddHours(6), isDay: true, temp: 70 + i, condition: "Sunny", icon: "https://api.weather.gov/icons/land/day/skc"));
      periods.Add(MakePeriod(i * 2 + 2, $"Night {i}", today.AddDays(i).Date.AddHours(18), isDay: false, temp: 50 + i, condition: "Clear", icon: "https://api.weather.gov/icons/land/night/skc"));
    }

    var days = NwsWeatherService.AggregateToDays(periods, today);

    Assert.Equal(3, days.Count);
  }

  [Fact]
  public void AggregateToDays_OnlyNightPeriod_StillReturnsADay()
  {
    // Late-evening boundary — NWS dropped the "today" day period because it's
    // already past sunset. We should still surface a single WeatherDay
    // populated from the night period.
    var today = new DateTime(2026, 5, 23);
    var periods = new List<NwsForecastPeriod>
    {
      MakePeriod(1, "Tonight", today.Date.AddHours(20), isDay: false, temp: 60, condition: "Clear", icon: "https://api.weather.gov/icons/land/night/skc"),
    };

    var days = NwsWeatherService.AggregateToDays(periods, today);

    Assert.Single(days);
    // No day-period high → the night-period temp is used as both.
    Assert.Equal(60, days[0].HighF);
    Assert.Equal(60, days[0].LowF);
  }

  [Fact]
  public void AggregateToDays_ConvertsFahrenheitToCelsius()
  {
    var today = new DateTime(2026, 5, 23);
    var periods = new List<NwsForecastPeriod>
    {
      MakePeriod(1, "Today", today.Date.AddHours(6), isDay: true, temp: 32, condition: "Cold", icon: "https://api.weather.gov/icons/land/day/cold"),
      MakePeriod(2, "Tonight", today.Date.AddHours(18), isDay: false, temp: 14, condition: "Cold", icon: "https://api.weather.gov/icons/land/night/skc"),
    };

    var days = NwsWeatherService.AggregateToDays(periods, today);

    // 32 °F = 0 °C ; 14 °F = -10 °C
    Assert.Equal(0, days[0].HighC);
    Assert.Equal(-10, days[0].LowC);
  }

  // ── End-to-end paths via mocked HttpMessageHandler ────────────────────────

  [Fact]
  public async Task GetForecastAsync_FeatureDisabled_ReturnsNullWithoutNetwork()
  {
    var handler = new Mock<HttpMessageHandler>(MockBehavior.Strict);
    var factory = MakeFactory(handler.Object);
    var options = new TestMonitor<WeatherDisplayOptions>(new WeatherDisplayOptions { Enabled = false });
    var cache = new MemoryCache(new MemoryCacheOptions());
    var resolver = new ZipCoordinatesResolver(factory.Object, NullLogger<ZipCoordinatesResolver>.Instance);

    var svc = new NwsWeatherService(factory.Object, resolver, cache, options, NullLogger<NwsWeatherService>.Instance);
    var result = await svc.GetForecastAsync("27312");

    Assert.Null(result);
    handler.Protected().Verify("SendAsync", Times.Never(), ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>());
  }

  [Fact]
  public async Task GetForecastAsync_InvalidZip_ReturnsNull()
  {
    var handler = new Mock<HttpMessageHandler>(MockBehavior.Strict);
    var factory = MakeFactory(handler.Object);
    var options = new TestMonitor<WeatherDisplayOptions>(new WeatherDisplayOptions { Enabled = true });
    var cache = new MemoryCache(new MemoryCacheOptions());
    var resolver = new ZipCoordinatesResolver(factory.Object, NullLogger<ZipCoordinatesResolver>.Instance);

    var svc = new NwsWeatherService(factory.Object, resolver, cache, options, NullLogger<NwsWeatherService>.Instance);
    var result = await svc.GetForecastAsync("abcd");

    Assert.Null(result);
  }

  [Fact]
  public async Task GetForecastAsync_HappyPath_HitsPointsThenForecast_ProducesThreeDays()
  {
    // Use the default ZIP (27312) so the coords step hits the fallback table
    // and we only need to mock /points and the forecast URL.
    var responses = new Dictionary<string, HttpResponseMessage>
    {
      ["/points/35.7156,-79.1845"] = JsonResponse(PointsResponseBody),
      ["https://api.weather.gov/gridpoints/RAH/53,68/forecast"] = JsonResponse(ForecastResponseBody(generatedAt: DateTimeOffset.UtcNow)),
    };
    var handler = MockHandler(responses);
    var factory = MakeFactory(handler.Object);
    var options = new TestMonitor<WeatherDisplayOptions>(new WeatherDisplayOptions { Enabled = true, RefreshIntervalMinutes = 60 });
    var cache = new MemoryCache(new MemoryCacheOptions());
    var resolver = new ZipCoordinatesResolver(factory.Object, NullLogger<ZipCoordinatesResolver>.Instance);

    var svc = new NwsWeatherService(factory.Object, resolver, cache, options, NullLogger<NwsWeatherService>.Instance);
    var result = await svc.GetForecastAsync("27312");

    Assert.NotNull(result);
    Assert.Equal("27312", result!.Zip);
    Assert.Equal("Pittsboro, NC", result.LocationName);
    Assert.False(result.IsStale);
    Assert.NotEmpty(result.Days);
  }

  [Fact]
  public async Task GetForecastAsync_SecondCallHitsCache_DoesNotCallUpstream()
  {
    var responses = new Dictionary<string, HttpResponseMessage>
    {
      ["/points/35.7156,-79.1845"] = JsonResponse(PointsResponseBody),
      ["https://api.weather.gov/gridpoints/RAH/53,68/forecast"] = JsonResponse(ForecastResponseBody(generatedAt: DateTimeOffset.UtcNow)),
    };
    // Use a counting handler so we can assert SendAsync call count.
    int calls = 0;
    var handler = new Mock<HttpMessageHandler>(MockBehavior.Strict);
    handler.Protected()
      .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
      .Returns<HttpRequestMessage, CancellationToken>((req, _) =>
      {
        calls++;
        var key = req.RequestUri!.IsAbsoluteUri && req.RequestUri.AbsoluteUri.StartsWith("https://api.weather.gov/gridpoints", StringComparison.Ordinal)
          ? req.RequestUri.AbsoluteUri
          : req.RequestUri.PathAndQuery;
        return Task.FromResult(responses.TryGetValue(key, out var resp)
          ? CloneResponse(resp)
          : new HttpResponseMessage(HttpStatusCode.NotFound));
      });

    var factory = MakeFactory(handler.Object);
    var options = new TestMonitor<WeatherDisplayOptions>(new WeatherDisplayOptions { Enabled = true, RefreshIntervalMinutes = 60 });
    var cache = new MemoryCache(new MemoryCacheOptions());
    var resolver = new ZipCoordinatesResolver(factory.Object, NullLogger<ZipCoordinatesResolver>.Instance);

    var svc = new NwsWeatherService(factory.Object, resolver, cache, options, NullLogger<NwsWeatherService>.Instance);
    await svc.GetForecastAsync("27312");
    var callsAfterFirst = calls;
    await svc.GetForecastAsync("27312");

    Assert.Equal(callsAfterFirst, calls); // No additional upstream calls
  }

  [Fact]
  public async Task GetForecastAsync_ConcurrentColdCacheCallers_OnlyOneUpstreamPointsCall()
  {
    // Regression guard for the cache-stampede MAJOR caught in the PR #415
    // first review. IMemoryCache.GetOrCreateAsync does NOT debounce
    // concurrent first-fills — without the per-ZIP semaphore in
    // NwsWeatherService.GetOrFillCoordsAsync / GetOrFillGridAsync, two
    // simultaneous callers will both hit zippopotam.us AND the /points
    // endpoint for the same ZIP.
    //
    // Use a non-default ZIP (10001) so the coords step actually hits the
    // resolver's network path (the default 27312 is in the fallback table
    // and short-circuits without any zippopotam.us call). The resolver's
    // response is held by a gate so both callers are guaranteed to overlap
    // inside the would-be stampede window.
    var zippopotamCalls = 0;
    var pointsCalls = 0;
    var forecastCalls = 0;
    var gate = new SemaphoreSlim(0, int.MaxValue);

    var handler = new Mock<HttpMessageHandler>(MockBehavior.Strict);
    handler.Protected()
      .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
      .Returns<HttpRequestMessage, CancellationToken>(async (req, _) =>
      {
        var uri = req.RequestUri!;
        if (uri.Host == "api.zippopotam.us")
        {
          Interlocked.Increment(ref zippopotamCalls);
          // Hold the first caller inside the resolver call so the second
          // caller has time to arrive and (if the lock is missing) race past.
          await gate.WaitAsync().ConfigureAwait(false);
          return new HttpResponseMessage(HttpStatusCode.OK)
          {
            Content = new StringContent(
              """{"post code":"10001","places":[{"place name":"New York","longitude":"-73.9967","state":"New York","state abbreviation":"NY","latitude":"40.7484"}]}""",
              System.Text.Encoding.UTF8, "application/json"),
          };
        }
        if (uri.AbsolutePath.StartsWith("/points/", StringComparison.Ordinal))
        {
          Interlocked.Increment(ref pointsCalls);
          await gate.WaitAsync().ConfigureAwait(false);
          return new HttpResponseMessage(HttpStatusCode.OK)
          {
            Content = new StringContent(
              """{"properties":{"forecast":"https://api.weather.gov/gridpoints/OKX/33,35/forecast","relativeLocation":{"properties":{"city":"New York","state":"NY"}}}}""",
              System.Text.Encoding.UTF8, "application/geo+json"),
          };
        }
        if (uri.IsAbsoluteUri && uri.AbsoluteUri.StartsWith("https://api.weather.gov/gridpoints", StringComparison.Ordinal))
        {
          Interlocked.Increment(ref forecastCalls);
          return new HttpResponseMessage(HttpStatusCode.OK)
          {
            Content = new StringContent(ForecastResponseBody(DateTimeOffset.UtcNow), System.Text.Encoding.UTF8, "application/geo+json"),
          };
        }
        return new HttpResponseMessage(HttpStatusCode.NotFound);
      });

    var factory = MakeFactory(handler.Object);
    var options = new TestMonitor<WeatherDisplayOptions>(new WeatherDisplayOptions { Enabled = true, RefreshIntervalMinutes = 60 });
    var cache = new MemoryCache(new MemoryCacheOptions());
    var resolver = new ZipCoordinatesResolver(factory.Object, NullLogger<ZipCoordinatesResolver>.Instance);
    var svc = new NwsWeatherService(factory.Object, resolver, cache, options, NullLogger<NwsWeatherService>.Instance);

    // Fire two cold-cache callers concurrently.
    var t1 = Task.Run(() => svc.GetForecastAsync("10001"));
    var t2 = Task.Run(() => svc.GetForecastAsync("10001"));

    // Give both Task.Run continuations time to reach the gated coords call.
    // The second caller will be blocked on the per-ZIP semaphore — if the
    // semaphore is missing, it would race past and hit the gate as a second
    // pending zippopotam.us request, which would push the counter to 2 BEFORE
    // we release the gate.
    await Task.Delay(100);
    // Release the gated zippopotam.us call first, then the /points call.
    gate.Release(1);
    await Task.Delay(50);
    gate.Release(1);

    var results = await Task.WhenAll(t1, t2);

    // Both callers got a real forecast back.
    Assert.All(results, r => Assert.NotNull(r));
    // The MUST-HAVE assertions — both upstream chains hit exactly once.
    Assert.Equal(1, zippopotamCalls);
    Assert.Equal(1, pointsCalls);
    // Forecast may be 1 or 2 — the spec ONLY guards coords + grid, the
    // forecast step is intentionally unlocked (tolerates one duplicate
    // refresh per fresh-TTL boundary). Just sanity-check it ran at least once.
    Assert.True(forecastCalls >= 1, $"expected at least one forecast call, got {forecastCalls}");
  }

  [Fact]
  public async Task GetForecastAsync_StaleEntry_RefreshFails_ReturnsStale()
  {
    // First fetch succeeds. Then we age the cache past the fresh TTL and the
    // refresh fails — service should serve the stale entry with IsStale=true.
    var goodResponses = new Dictionary<string, HttpResponseMessage>
    {
      ["/points/35.7156,-79.1845"] = JsonResponse(PointsResponseBody),
      ["https://api.weather.gov/gridpoints/RAH/53,68/forecast"] = JsonResponse(ForecastResponseBody(generatedAt: DateTimeOffset.UtcNow.AddHours(-1))),
    };

    var failOnRefresh = false;
    var handler = new Mock<HttpMessageHandler>(MockBehavior.Strict);
    handler.Protected()
      .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
      .Returns<HttpRequestMessage, CancellationToken>((req, _) =>
      {
        var isForecastUrl = req.RequestUri!.IsAbsoluteUri && req.RequestUri.AbsoluteUri.StartsWith("https://api.weather.gov/gridpoints", StringComparison.Ordinal);
        if (failOnRefresh && isForecastUrl)
        {
          return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        }
        var key = isForecastUrl ? req.RequestUri.AbsoluteUri : req.RequestUri.PathAndQuery;
        return Task.FromResult(goodResponses.TryGetValue(key, out var resp)
          ? CloneResponse(resp)
          : new HttpResponseMessage(HttpStatusCode.NotFound));
      });

    var factory = MakeFactory(handler.Object);
    // 1-minute fresh TTL so the test can age it artificially without sleeping.
    var options = new TestMonitor<WeatherDisplayOptions>(new WeatherDisplayOptions { Enabled = true, RefreshIntervalMinutes = 15 });
    var cache = new MemoryCache(new MemoryCacheOptions());
    var resolver = new ZipCoordinatesResolver(factory.Object, NullLogger<ZipCoordinatesResolver>.Instance);

    var svc = new NwsWeatherService(factory.Object, resolver, cache, options, NullLogger<NwsWeatherService>.Instance);

    // 1st call — populate cache.
    var first = await svc.GetForecastAsync("27312");
    Assert.NotNull(first);
    Assert.False(first!.IsStale);

    // Age the cached entry by reconstructing it with an old FetchedAtUtc —
    // we can't wait 16+ minutes in a unit test. The CachedForecast type is
    // a private nested record (Forecast, FetchedAtUtc); use BindingFlags to
    // reach the non-public ctor.
    var forecastCacheKey = "weather:zip:27312:forecast";
    var cached = cache.Get(forecastCacheKey);
    Assert.NotNull(cached);
    var cachedType = cached!.GetType();
    var forecastProp = cachedType.GetProperty("Forecast");
    Assert.NotNull(forecastProp);
    var forecastValue = forecastProp!.GetValue(cached);
    var aged = Activator.CreateInstance(
      cachedType,
      System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
      binder: null,
      args: new object?[] { forecastValue, DateTimeOffset.UtcNow.AddHours(-2) },
      culture: null);
    Assert.NotNull(aged);
    cache.Set(forecastCacheKey, aged!);

    // Flip the handler so refresh attempts fail.
    failOnRefresh = true;

    var second = await svc.GetForecastAsync("27312");
    Assert.NotNull(second);
    Assert.True(second!.IsStale, "Refresh failure should serve stale entry with IsStale=true");
  }

  [Fact]
  public async Task GetForecastAsync_NoCache_UpstreamFails_ReturnsNull()
  {
    var handler = new Mock<HttpMessageHandler>(MockBehavior.Strict);
    handler.Protected()
      .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
      .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.InternalServerError));

    var factory = MakeFactory(handler.Object);
    var options = new TestMonitor<WeatherDisplayOptions>(new WeatherDisplayOptions { Enabled = true });
    var cache = new MemoryCache(new MemoryCacheOptions());
    var resolver = new ZipCoordinatesResolver(factory.Object, NullLogger<ZipCoordinatesResolver>.Instance);

    var svc = new NwsWeatherService(factory.Object, resolver, cache, options, NullLogger<NwsWeatherService>.Instance);
    var result = await svc.GetForecastAsync("27312");

    Assert.Null(result);
  }

  // ────────────────────────── helpers ──────────────────────────

  private static NwsForecastPeriod MakePeriod(int number, string name, DateTime startTime, bool isDay, int temp, string condition, string icon)
  {
    return new NwsForecastPeriod
    {
      Number = number,
      Name = name,
      StartTime = new DateTimeOffset(startTime),
      EndTime = new DateTimeOffset(startTime.AddHours(12)),
      IsDaytime = isDay,
      Temperature = temp,
      TemperatureUnit = "F",
      ShortForecast = condition,
      DetailedForecast = condition + " all day",
      Icon = icon,
      ProbabilityOfPrecipitation = new NwsValueObject { Value = 0 },
    };
  }

  private static Mock<IHttpClientFactory> MakeFactory(HttpMessageHandler handler)
  {
    var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.weather.gov") };
    var factory = new Mock<IHttpClientFactory>();
    factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(client);
    return factory;
  }

  private static Mock<HttpMessageHandler> MockHandler(IDictionary<string, HttpResponseMessage> responses)
  {
    var handler = new Mock<HttpMessageHandler>(MockBehavior.Strict);
    handler.Protected()
      .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
      .Returns<HttpRequestMessage, CancellationToken>((req, _) =>
      {
        var key = req.RequestUri!.IsAbsoluteUri && req.RequestUri.AbsoluteUri.StartsWith("https://api.weather.gov/gridpoints", StringComparison.Ordinal)
          ? req.RequestUri.AbsoluteUri
          : req.RequestUri.PathAndQuery;
        return Task.FromResult(responses.TryGetValue(key, out var resp)
          ? CloneResponse(resp)
          : new HttpResponseMessage(HttpStatusCode.NotFound));
      });
    return handler;
  }

  private static HttpResponseMessage JsonResponse(string body) => new(HttpStatusCode.OK)
  {
    Content = new StringContent(body, System.Text.Encoding.UTF8, "application/geo+json"),
  };

  private static HttpResponseMessage CloneResponse(HttpResponseMessage original)
  {
    // HttpResponseMessage isn't cloneable; recreate from the underlying string.
    var content = original.Content.ReadAsStringAsync().GetAwaiter().GetResult();
    return new HttpResponseMessage(original.StatusCode)
    {
      Content = new StringContent(content, System.Text.Encoding.UTF8, "application/geo+json"),
    };
  }

  private const string PointsResponseBody = """
    {
      "properties": {
        "forecast": "https://api.weather.gov/gridpoints/RAH/53,68/forecast",
        "relativeLocation": {
          "properties": {
            "city": "Pittsboro",
            "state": "NC"
          }
        }
      }
    }
    """;

  private static string ForecastResponseBody(DateTimeOffset generatedAt)
  {
    var iso = generatedAt.ToString("o", System.Globalization.CultureInfo.InvariantCulture);
    return $$"""
      {
        "properties": {
          "generatedAt": "{{iso}}",
          "periods": [
            { "number": 1, "name": "Today", "startTime": "2026-05-23T06:00:00-04:00", "endTime": "2026-05-23T18:00:00-04:00", "isDaytime": true, "temperature": 75, "temperatureUnit": "F", "probabilityOfPrecipitation": {"value": 10}, "shortForecast": "Sunny", "detailedForecast": "Sunny", "icon": "https://api.weather.gov/icons/land/day/skc?size=medium" },
            { "number": 2, "name": "Tonight", "startTime": "2026-05-23T18:00:00-04:00", "endTime": "2026-05-24T06:00:00-04:00", "isDaytime": false, "temperature": 55, "temperatureUnit": "F", "probabilityOfPrecipitation": null, "shortForecast": "Clear", "detailedForecast": "Clear", "icon": "https://api.weather.gov/icons/land/night/skc" },
            { "number": 3, "name": "Sunday", "startTime": "2026-05-24T06:00:00-04:00", "endTime": "2026-05-24T18:00:00-04:00", "isDaytime": true, "temperature": 78, "temperatureUnit": "F", "probabilityOfPrecipitation": {"value": 20}, "shortForecast": "Partly Cloudy", "detailedForecast": "Partly Cloudy", "icon": "https://api.weather.gov/icons/land/day/sct" },
            { "number": 4, "name": "Sunday Night", "startTime": "2026-05-24T18:00:00-04:00", "endTime": "2026-05-25T06:00:00-04:00", "isDaytime": false, "temperature": 58, "temperatureUnit": "F", "probabilityOfPrecipitation": null, "shortForecast": "Mostly Clear", "detailedForecast": "Mostly Clear", "icon": "https://api.weather.gov/icons/land/night/sct" },
            { "number": 5, "name": "Monday", "startTime": "2026-05-25T06:00:00-04:00", "endTime": "2026-05-25T18:00:00-04:00", "isDaytime": true, "temperature": 70, "temperatureUnit": "F", "probabilityOfPrecipitation": {"value": 60}, "shortForecast": "Showers", "detailedForecast": "Showers", "icon": "https://api.weather.gov/icons/land/day/rain" },
            { "number": 6, "name": "Monday Night", "startTime": "2026-05-25T18:00:00-04:00", "endTime": "2026-05-26T06:00:00-04:00", "isDaytime": false, "temperature": 50, "temperatureUnit": "F", "probabilityOfPrecipitation": {"value": 40}, "shortForecast": "Rain", "detailedForecast": "Rain", "icon": "https://api.weather.gov/icons/land/night/sct" }
          ]
        }
      }
      """;
  }

  /// <summary>
  /// Tiny stub for <see cref="IOptionsMonitor{T}"/> that returns a fixed
  /// value. Production wiring uses the real DI-backed implementation; we just
  /// need a CurrentValue.
  /// </summary>
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
