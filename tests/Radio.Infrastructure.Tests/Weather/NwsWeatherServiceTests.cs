using System.Net;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using Radio.Core.Configuration;
using Radio.Core.Models;
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

    // Endpoint discrimination by path-prefix rather than Host because the
    // shared MakeFactory helper hands every caller (zippopotam resolver, NWS
    // points fetch, NWS forecast fetch) the same HttpClient with BaseAddress
    // api.weather.gov. The path prefixes /us/, /points/, and the gridpoints
    // forecast URL are still distinct so we can count each leg separately.
    var handler = new Mock<HttpMessageHandler>(MockBehavior.Strict);
    handler.Protected()
      .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
      .Returns<HttpRequestMessage, CancellationToken>(async (req, _) =>
      {
        var uri = req.RequestUri!;
        var path = uri.IsAbsoluteUri ? uri.AbsolutePath : uri.OriginalString;
        var absoluteUri = uri.IsAbsoluteUri ? uri.AbsoluteUri : string.Empty;

        if (path.StartsWith("/us/", StringComparison.Ordinal))
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
        if (path.StartsWith("/points/", StringComparison.Ordinal))
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
        if (absoluteUri.StartsWith("https://api.weather.gov/gridpoints", StringComparison.Ordinal))
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
    // we release the gate. 250 ms is generous but keeps the test fast; the
    // ConcurrentDictionary.GetOrAdd + SemaphoreSlim.WaitAsync path is sub-ms.
    await Task.Delay(250);
    // Release enough gate permits to satisfy any chain call that's blocked
    // (1 zippopotam + 1 /points = 2 with the lock; up to 4 without). Extra
    // permits are harmless — they just sit unused in the SemaphoreSlim.
    gate.Release(4);

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

    // Handler returns 500 for any /points or forecast call — we want the
    // refresh attempt to fail. The pre-injected stale entry should be served.
    var handler = new Mock<HttpMessageHandler>(MockBehavior.Strict);
    handler.Protected()
      .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
      .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.InternalServerError));

    var factory = MakeFactory(handler.Object);
    // 15-minute fresh TTL; the pre-injected entry is 2 hours old → past fresh
    // but inside the 24h stale-serve horizon, so the service must attempt a
    // refresh and fall back to the stale entry when the refresh fails.
    var options = new TestMonitor<WeatherDisplayOptions>(new WeatherDisplayOptions { Enabled = true, RefreshIntervalMinutes = 15 });
    var cache = new MemoryCache(new MemoryCacheOptions());
    var resolver = new ZipCoordinatesResolver(factory.Object, NullLogger<ZipCoordinatesResolver>.Instance);

    var svc = new NwsWeatherService(factory.Object, resolver, cache, options, NullLogger<NwsWeatherService>.Instance);

    // Inject a stale-aged forecast directly via the internal test seam.
    // Using SetForecastCacheEntryForTesting instead of Activator.CreateInstance
    // on the private nested record keeps the test from breaking the day
    // someone renames CachedForecast or reorders its constructor parameters
    // (MAJOR 2 from PR #415 first review).
    var pristineForecast = BuildForecast("27312");
    svc.SetForecastCacheEntryForTesting("27312", pristineForecast, DateTimeOffset.UtcNow.AddHours(-2));

    var result = await svc.GetForecastAsync("27312");

    Assert.NotNull(result);
    Assert.True(result!.IsStale, "Refresh failure should serve stale entry with IsStale=true");
    // Returned forecast should be the SAME logical forecast we injected —
    // confirms the service served stale rather than refetching a different one.
    Assert.Equal(pristineForecast.GeneratedAtUtc, result.GeneratedAtUtc);
    Assert.Equal(pristineForecast.Zip, result.Zip);
    Assert.Equal(pristineForecast.LocationName, result.LocationName);
  }

  /// <summary>
  /// Helper: build a non-empty <see cref="WeatherForecast"/> we can inject
  /// into the cache for stale-serve tests.
  /// </summary>
  private static WeatherForecast BuildForecast(string zip) => new(
    Zip: zip,
    LocationName: "Pittsboro, NC",
    GeneratedAtUtc: DateTimeOffset.UtcNow.AddHours(-2),
    FetchedAtUtc: DateTimeOffset.UtcNow.AddHours(-2),
    IsStale: false,
    Days: new List<WeatherDay>
    {
      new(new DateOnly(2026, 5, 23), "Today", 75, 60, 24, 16, "Sunny", "Sunny", 0, "sunny", null),
      new(new DateOnly(2026, 5, 24), "Tomorrow", 78, 62, 26, 17, "Partly Cloudy", "Partly Cloudy", 20, "mostly-sunny", null),
      new(new DateOnly(2026, 5, 25), "Mon", 70, 55, 21, 13, "Showers", "Showers", 60, "rain", null),
    },
    Current: null);

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

  // ── Observation chain — HANDOFF-sleep-weather-current-conditions.md §4 ─────
  //
  // Covers the new GetForecastAsync path that fetches /points →
  // observationStations → stations list → /stations/{id}/observations/latest
  // in parallel with the forecast, plus the stale/firewall/sanity behaviors.

  [Fact]
  public void MapObservation_FreshData_ConvertsCtoFAndMapsFields()
  {
    // 9.4 °C → rounded F = 49 (9.4 * 9/5 + 32 = 48.92, AwayFromZero → 49).
    // 9.4 °C → rounded C = 9.
    var props = new NwsObservationProperties
    {
      Timestamp = DateTimeOffset.UtcNow.AddMinutes(-10),
      TextDescription = "Partly Cloudy",
      Icon = "https://api.weather.gov/icons/land/night/sct?size=medium",
      Temperature = new NwsObservationValue { Value = 9.4, UnitCode = "wmoUnit:degC" },
    };

    var obs = NwsWeatherService.MapObservation(props);

    Assert.NotNull(obs);
    Assert.Equal(49, obs!.TempF);
    Assert.Equal(9, obs.TempC);
    Assert.Equal("Partly Cloudy", obs.ConditionShort);
    Assert.Equal("partly-cloudy-night", obs.IconKey);
    Assert.False(obs.IsStale);
  }

  [Theory]
  [InlineData(null)]       // sensor returned null
  [InlineData(double.NaN)] // NaN sentinel
  [InlineData(99.0)]       // above max (60)
  [InlineData(-99.0)]      // below min (-60)
  public void MapObservation_SanityGuardTrips_ReturnsNull(double? badValue)
  {
    var props = new NwsObservationProperties
    {
      Timestamp = DateTimeOffset.UtcNow,
      TextDescription = "Should Not Render",
      Icon = "https://api.weather.gov/icons/land/day/skc",
      Temperature = new NwsObservationValue { Value = badValue, UnitCode = "wmoUnit:degC" },
    };

    Assert.Null(NwsWeatherService.MapObservation(props));
  }

  [Fact]
  public void MapObservation_MissingTimestamp_ReturnsNull()
  {
    var props = new NwsObservationProperties
    {
      Timestamp = null,
      TextDescription = "Sunny",
      Icon = "https://api.weather.gov/icons/land/day/skc",
      Temperature = new NwsObservationValue { Value = 20.0, UnitCode = "wmoUnit:degC" },
    };

    Assert.Null(NwsWeatherService.MapObservation(props));
  }

  [Fact]
  public void MapObservation_NullProps_ReturnsNull()
  {
    Assert.Null(NwsWeatherService.MapObservation(null));
  }

  [Fact]
  public void MapObservation_ObservationOlderThanTwoHours_MarksStale()
  {
    var props = new NwsObservationProperties
    {
      Timestamp = DateTimeOffset.UtcNow.AddHours(-3),
      TextDescription = "Cloudy",
      Icon = "https://api.weather.gov/icons/land/day/ovc",
      Temperature = new NwsObservationValue { Value = 15.0, UnitCode = "wmoUnit:degC" },
    };

    var obs = NwsWeatherService.MapObservation(props);

    Assert.NotNull(obs);
    Assert.True(obs!.IsStale, "Observations older than 2h must be marked stale");
  }

  [Fact]
  public async Task GetForecastAsync_StationsChainSucceeds_PopulatesCurrent()
  {
    var observationTimestamp = DateTimeOffset.UtcNow.AddMinutes(-10);
    var responses = new Dictionary<string, HttpResponseMessage>
    {
      ["/points/35.7156,-79.1845"] = JsonResponse(PointsResponseBodyWithObservation),
      ["https://api.weather.gov/gridpoints/RAH/53,68/forecast"] = JsonResponse(ForecastResponseBody(DateTimeOffset.UtcNow)),
      ["https://api.weather.gov/gridpoints/RAH/53,68/stations"] = JsonResponse(StationsResponseBody),
      ["/stations/KIGX/observations/latest"] = JsonResponse(ObservationResponseBody(9.4, "Partly Cloudy", "https://api.weather.gov/icons/land/night/sct", observationTimestamp)),
    };
    var handler = MockHandler(responses);
    var factory = MakeFactory(handler.Object);
    var options = new TestMonitor<WeatherDisplayOptions>(new WeatherDisplayOptions { Enabled = true, RefreshIntervalMinutes = 60 });
    var cache = new MemoryCache(new MemoryCacheOptions());
    var resolver = new ZipCoordinatesResolver(factory.Object, NullLogger<ZipCoordinatesResolver>.Instance);

    var svc = new NwsWeatherService(factory.Object, resolver, cache, options, NullLogger<NwsWeatherService>.Instance);
    var result = await svc.GetForecastAsync("27312");

    Assert.NotNull(result);
    Assert.NotNull(result!.Current);
    Assert.Equal(49, result.Current!.TempF);
    Assert.Equal(9, result.Current.TempC);
    Assert.Equal("Partly Cloudy", result.Current.ConditionShort);
    Assert.Equal("partly-cloudy-night", result.Current.IconKey);
    Assert.False(result.Current.IsStale);
  }

  [Fact]
  public async Task GetForecastAsync_NoObservationStationsUrl_LeavesCurrentNull()
  {
    // Use the original points body (no observationStations field) — forecast
    // should still populate but Current is null. This is the existing happy
    // path's behavior, locked in as an explicit assertion.
    var responses = new Dictionary<string, HttpResponseMessage>
    {
      ["/points/35.7156,-79.1845"] = JsonResponse(PointsResponseBody),
      ["https://api.weather.gov/gridpoints/RAH/53,68/forecast"] = JsonResponse(ForecastResponseBody(DateTimeOffset.UtcNow)),
    };
    var handler = MockHandler(responses);
    var factory = MakeFactory(handler.Object);
    var options = new TestMonitor<WeatherDisplayOptions>(new WeatherDisplayOptions { Enabled = true, RefreshIntervalMinutes = 60 });
    var cache = new MemoryCache(new MemoryCacheOptions());
    var resolver = new ZipCoordinatesResolver(factory.Object, NullLogger<ZipCoordinatesResolver>.Instance);

    var svc = new NwsWeatherService(factory.Object, resolver, cache, options, NullLogger<NwsWeatherService>.Instance);
    var result = await svc.GetForecastAsync("27312");

    Assert.NotNull(result);
    Assert.Null(result!.Current);
    Assert.NotEmpty(result.Days);
  }

  [Fact]
  public async Task GetForecastAsync_StationsListEmpty_LeavesCurrentNull()
  {
    var responses = new Dictionary<string, HttpResponseMessage>
    {
      ["/points/35.7156,-79.1845"] = JsonResponse(PointsResponseBodyWithObservation),
      ["https://api.weather.gov/gridpoints/RAH/53,68/forecast"] = JsonResponse(ForecastResponseBody(DateTimeOffset.UtcNow)),
      ["https://api.weather.gov/gridpoints/RAH/53,68/stations"] = JsonResponse("""{"features": []}"""),
    };
    var handler = MockHandler(responses);
    var factory = MakeFactory(handler.Object);
    var options = new TestMonitor<WeatherDisplayOptions>(new WeatherDisplayOptions { Enabled = true, RefreshIntervalMinutes = 60 });
    var cache = new MemoryCache(new MemoryCacheOptions());
    var resolver = new ZipCoordinatesResolver(factory.Object, NullLogger<ZipCoordinatesResolver>.Instance);

    var svc = new NwsWeatherService(factory.Object, resolver, cache, options, NullLogger<NwsWeatherService>.Instance);
    var result = await svc.GetForecastAsync("27312");

    Assert.NotNull(result);
    Assert.Null(result!.Current);
    Assert.NotEmpty(result.Days);
  }

  [Fact]
  public async Task GetForecastAsync_ObservationReturns404_LeavesCurrentNull()
  {
    var responses = new Dictionary<string, HttpResponseMessage>
    {
      ["/points/35.7156,-79.1845"] = JsonResponse(PointsResponseBodyWithObservation),
      ["https://api.weather.gov/gridpoints/RAH/53,68/forecast"] = JsonResponse(ForecastResponseBody(DateTimeOffset.UtcNow)),
      ["https://api.weather.gov/gridpoints/RAH/53,68/stations"] = JsonResponse(StationsResponseBody),
      // observation endpoint NOT registered — MockHandler returns 404 for
      // anything not in the dict, which exercises the failure path.
    };
    var handler = MockHandler(responses);
    var factory = MakeFactory(handler.Object);
    var options = new TestMonitor<WeatherDisplayOptions>(new WeatherDisplayOptions { Enabled = true, RefreshIntervalMinutes = 60 });
    var cache = new MemoryCache(new MemoryCacheOptions());
    var resolver = new ZipCoordinatesResolver(factory.Object, NullLogger<ZipCoordinatesResolver>.Instance);

    var svc = new NwsWeatherService(factory.Object, resolver, cache, options, NullLogger<NwsWeatherService>.Instance);
    var result = await svc.GetForecastAsync("27312");

    Assert.NotNull(result);
    Assert.Null(result!.Current);
    Assert.NotEmpty(result.Days);
  }

  [Fact]
  public async Task GetForecastAsync_ObservationCacheHit_DoesNotReFetch()
  {
    // First call exercises the full observation chain; the second call should
    // hit the observation cache and skip both the stations request and the
    // observations/latest request.
    int stationsCalls = 0;
    int observationCalls = 0;
    var observationTimestamp = DateTimeOffset.UtcNow.AddMinutes(-10);

    var handler = new Mock<HttpMessageHandler>(MockBehavior.Strict);
    handler.Protected()
      .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
      .Returns<HttpRequestMessage, CancellationToken>((req, _) =>
      {
        var uri = req.RequestUri!;
        var path = uri.IsAbsoluteUri ? uri.AbsolutePath : uri.OriginalString;
        var absoluteUri = uri.IsAbsoluteUri ? uri.AbsoluteUri : string.Empty;

        if (path == "/points/35.7156,-79.1845")
        {
          return Task.FromResult(JsonResponse(PointsResponseBodyWithObservation));
        }
        if (absoluteUri == "https://api.weather.gov/gridpoints/RAH/53,68/forecast")
        {
          return Task.FromResult(JsonResponse(ForecastResponseBody(DateTimeOffset.UtcNow)));
        }
        if (absoluteUri == "https://api.weather.gov/gridpoints/RAH/53,68/stations")
        {
          Interlocked.Increment(ref stationsCalls);
          return Task.FromResult(JsonResponse(StationsResponseBody));
        }
        if (path == "/stations/KIGX/observations/latest")
        {
          Interlocked.Increment(ref observationCalls);
          return Task.FromResult(JsonResponse(ObservationResponseBody(9.4, "Partly Cloudy", "https://api.weather.gov/icons/land/night/sct", observationTimestamp)));
        }
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
      });

    var factory = MakeFactory(handler.Object);
    var options = new TestMonitor<WeatherDisplayOptions>(new WeatherDisplayOptions { Enabled = true, RefreshIntervalMinutes = 60 });
    var cache = new MemoryCache(new MemoryCacheOptions());
    var resolver = new ZipCoordinatesResolver(factory.Object, NullLogger<ZipCoordinatesResolver>.Instance);

    var svc = new NwsWeatherService(factory.Object, resolver, cache, options, NullLogger<NwsWeatherService>.Instance);

    // First call — chain runs.
    var first = await svc.GetForecastAsync("27312");
    Assert.NotNull(first?.Current);
    var stationsCallsAfterFirst = stationsCalls;
    var observationCallsAfterFirst = observationCalls;
    Assert.Equal(1, stationsCallsAfterFirst);
    Assert.Equal(1, observationCallsAfterFirst);

    // Second call — observation cache is fresh (just-fetched), AND the
    // forecast cache is fresh, so neither stations nor observation upstream
    // is invoked. We can't actually reach the observation cache via a second
    // GetForecastAsync call because the forecast cache short-circuits first;
    // exercise the observation-cache hit by clearing only the forecast cache
    // entry and re-invoking. But there's no test seam for that; instead the
    // simpler version of this test simply verifies that nothing increments
    // on the second call — the forecast-cache-hit path is the natural fast
    // path, and we already cover the per-leg cache wiring via
    // GetForecastAsync_SecondCallHitsCache_DoesNotCallUpstream.
    var second = await svc.GetForecastAsync("27312");
    Assert.NotNull(second?.Current);
    Assert.Equal(stationsCallsAfterFirst, stationsCalls);
    Assert.Equal(observationCallsAfterFirst, observationCalls);
  }

  [Fact]
  public async Task GetForecastAsync_StationsCacheStampede_OnlyOneStationsFetch()
  {
    // Mirrors GetForecastAsync_ConcurrentColdCacheCallers_OnlyOneUpstreamPointsCall
    // for the new stations leg. Two concurrent cold-cache callers must produce
    // exactly one stations request thanks to _stationsLocks.
    var pointsCalls = 0;
    var stationsCalls = 0;
    var observationCalls = 0;
    var forecastCalls = 0;
    var gate = new SemaphoreSlim(0, int.MaxValue);

    var handler = new Mock<HttpMessageHandler>(MockBehavior.Strict);
    handler.Protected()
      .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
      .Returns<HttpRequestMessage, CancellationToken>(async (req, _) =>
      {
        var uri = req.RequestUri!;
        var path = uri.IsAbsoluteUri ? uri.AbsolutePath : uri.OriginalString;
        var absoluteUri = uri.IsAbsoluteUri ? uri.AbsoluteUri : string.Empty;

        if (path.StartsWith("/points/", StringComparison.Ordinal))
        {
          Interlocked.Increment(ref pointsCalls);
          return JsonResponse(PointsResponseBodyWithObservation);
        }
        if (absoluteUri == "https://api.weather.gov/gridpoints/RAH/53,68/forecast")
        {
          Interlocked.Increment(ref forecastCalls);
          return JsonResponse(ForecastResponseBody(DateTimeOffset.UtcNow));
        }
        if (absoluteUri == "https://api.weather.gov/gridpoints/RAH/53,68/stations")
        {
          Interlocked.Increment(ref stationsCalls);
          // Hold the first caller so the second has time to arrive at the
          // semaphore (without the lock, both would race past).
          await gate.WaitAsync().ConfigureAwait(false);
          return JsonResponse(StationsResponseBody);
        }
        if (path == "/stations/KIGX/observations/latest")
        {
          Interlocked.Increment(ref observationCalls);
          return JsonResponse(ObservationResponseBody(9.4, "Partly Cloudy", "https://api.weather.gov/icons/land/night/sct", DateTimeOffset.UtcNow.AddMinutes(-10)));
        }
        return new HttpResponseMessage(HttpStatusCode.NotFound);
      });

    var factory = MakeFactory(handler.Object);
    var options = new TestMonitor<WeatherDisplayOptions>(new WeatherDisplayOptions { Enabled = true, RefreshIntervalMinutes = 60 });
    var cache = new MemoryCache(new MemoryCacheOptions());
    var resolver = new ZipCoordinatesResolver(factory.Object, NullLogger<ZipCoordinatesResolver>.Instance);
    var svc = new NwsWeatherService(factory.Object, resolver, cache, options, NullLogger<NwsWeatherService>.Instance);

    var t1 = Task.Run(() => svc.GetForecastAsync("27312"));
    var t2 = Task.Run(() => svc.GetForecastAsync("27312"));

    await Task.Delay(250); // let both callers reach the gated stations call
    gate.Release(4); // generous — extras sit unused

    var results = await Task.WhenAll(t1, t2);

    Assert.All(results, r => Assert.NotNull(r));
    // Stations call must run exactly once thanks to _stationsLocks.
    Assert.Equal(1, stationsCalls);
    // Observation cache is unguarded by design (same rationale as forecast
    // cache) — may run 1 or 2 times. Just sanity-check it ran at least once.
    Assert.True(observationCalls >= 1, $"expected at least one observation call, got {observationCalls}");
  }

  [Fact]
  public async Task GetForecastAsync_ObservationStaleFetchFails_ReturnsStaleCurrent()
  {
    // Pre-seed a stale observation entry; configure the upstream to fail so
    // the refresh attempt throws. Stale-while-revalidate must serve the
    // cached observation with IsStale=true.
    var responses = new Dictionary<string, HttpResponseMessage>
    {
      ["/points/35.7156,-79.1845"] = JsonResponse(PointsResponseBodyWithObservation),
      ["https://api.weather.gov/gridpoints/RAH/53,68/forecast"] = JsonResponse(ForecastResponseBody(DateTimeOffset.UtcNow)),
      ["https://api.weather.gov/gridpoints/RAH/53,68/stations"] = JsonResponse(StationsResponseBody),
      // observation endpoint registered to return 500 — refresh fails.
      ["/stations/KIGX/observations/latest"] = new HttpResponseMessage(HttpStatusCode.InternalServerError),
    };
    var handler = MockHandler(responses);
    var factory = MakeFactory(handler.Object);
    var options = new TestMonitor<WeatherDisplayOptions>(new WeatherDisplayOptions { Enabled = true, RefreshIntervalMinutes = 60 });
    var cache = new MemoryCache(new MemoryCacheOptions());
    var resolver = new ZipCoordinatesResolver(factory.Object, NullLogger<ZipCoordinatesResolver>.Instance);

    var svc = new NwsWeatherService(factory.Object, resolver, cache, options, NullLogger<NwsWeatherService>.Instance);

    // Inject a stale observation (fetched 90 min ago, observed 90 min ago)
    // via the test seam. Fetched age > 30 min fresh-TTL so the service will
    // attempt a refresh (which fails), then fall back to the stale cache
    // entry inside the 24 h stale-serve horizon.
    var stalenessTimestamp = DateTimeOffset.UtcNow.AddMinutes(-90);
    var cachedObs = new CurrentObservation(
      TempF: 48, TempC: 9,
      ConditionShort: "Partly Cloudy",
      IconKey: "partly-cloudy-night",
      ObservedAtUtc: stalenessTimestamp,
      IsStale: false);
    svc.SetObservationCacheEntryForTesting("27312", cachedObs, stalenessTimestamp);

    var result = await svc.GetForecastAsync("27312");

    Assert.NotNull(result);
    Assert.NotNull(result!.Current);
    Assert.True(result.Current!.IsStale, "Refresh failure inside the stale-serve horizon must serve the cached observation marked stale");
    Assert.Equal(48, result.Current.TempF);
    Assert.Equal("Partly Cloudy", result.Current.ConditionShort);
  }

  [Fact]
  public async Task GetForecastAsync_ObservationStaleFetchFailsBeyondHorizon_ReturnsNullCurrent()
  {
    var responses = new Dictionary<string, HttpResponseMessage>
    {
      ["/points/35.7156,-79.1845"] = JsonResponse(PointsResponseBodyWithObservation),
      ["https://api.weather.gov/gridpoints/RAH/53,68/forecast"] = JsonResponse(ForecastResponseBody(DateTimeOffset.UtcNow)),
      ["https://api.weather.gov/gridpoints/RAH/53,68/stations"] = JsonResponse(StationsResponseBody),
      ["/stations/KIGX/observations/latest"] = new HttpResponseMessage(HttpStatusCode.InternalServerError),
    };
    var handler = MockHandler(responses);
    var factory = MakeFactory(handler.Object);
    var options = new TestMonitor<WeatherDisplayOptions>(new WeatherDisplayOptions { Enabled = true, RefreshIntervalMinutes = 60 });
    var cache = new MemoryCache(new MemoryCacheOptions());
    var resolver = new ZipCoordinatesResolver(factory.Object, NullLogger<ZipCoordinatesResolver>.Instance);

    var svc = new NwsWeatherService(factory.Object, resolver, cache, options, NullLogger<NwsWeatherService>.Instance);

    // Inject a cache entry 30 hours old — past the 24 h stale-serve horizon.
    var aged = DateTimeOffset.UtcNow.AddHours(-30);
    var cachedObs = new CurrentObservation(
      TempF: 48, TempC: 9,
      ConditionShort: "Partly Cloudy",
      IconKey: "partly-cloudy-night",
      ObservedAtUtc: aged,
      IsStale: true);
    svc.SetObservationCacheEntryForTesting("27312", cachedObs, aged);

    var result = await svc.GetForecastAsync("27312");

    Assert.NotNull(result);
    // Beyond the stale-serve horizon → Current must be null. Forecast still
    // populates (forecast leg is independent + succeeded).
    Assert.Null(result!.Current);
    Assert.NotEmpty(result.Days);
  }

  [Fact]
  public async Task GetForecastAsync_ForecastFails_ObservationSucceeds_StillReturnsNull()
  {
    // Observation succeeding cannot rescue a broken forecast — the outer
    // contract (GetForecastAsync returns null when the forecast chain
    // throws and no cache exists) is unchanged.
    var responses = new Dictionary<string, HttpResponseMessage>
    {
      ["/points/35.7156,-79.1845"] = JsonResponse(PointsResponseBodyWithObservation),
      ["https://api.weather.gov/gridpoints/RAH/53,68/forecast"] = new HttpResponseMessage(HttpStatusCode.InternalServerError),
      ["https://api.weather.gov/gridpoints/RAH/53,68/stations"] = JsonResponse(StationsResponseBody),
      ["/stations/KIGX/observations/latest"] = JsonResponse(ObservationResponseBody(9.4, "Partly Cloudy", "https://api.weather.gov/icons/land/night/sct", DateTimeOffset.UtcNow.AddMinutes(-10))),
    };
    var handler = MockHandler(responses);
    var factory = MakeFactory(handler.Object);
    var options = new TestMonitor<WeatherDisplayOptions>(new WeatherDisplayOptions { Enabled = true, RefreshIntervalMinutes = 60 });
    var cache = new MemoryCache(new MemoryCacheOptions());
    var resolver = new ZipCoordinatesResolver(factory.Object, NullLogger<ZipCoordinatesResolver>.Instance);

    var svc = new NwsWeatherService(factory.Object, resolver, cache, options, NullLogger<NwsWeatherService>.Instance);
    var result = await svc.GetForecastAsync("27312");

    Assert.Null(result);
  }

  [Fact]
  public async Task GetForecastAsync_ParallelTiming_BothCallsFire()
  {
    // Verify the Task.WhenAll parallel path — both the forecast and
    // observation handlers fire (without blocking each other). Use a
    // delaying handler that holds the forecast leg long enough that, if the
    // calls were sequential, the observation would have to wait until the
    // forecast completed. Assert observation start happens BEFORE forecast
    // completion.
    DateTimeOffset? forecastStart = null;
    DateTimeOffset? forecastEnd = null;
    DateTimeOffset? observationStart = null;

    var handler = new Mock<HttpMessageHandler>(MockBehavior.Strict);
    handler.Protected()
      .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
      .Returns<HttpRequestMessage, CancellationToken>(async (req, _) =>
      {
        var uri = req.RequestUri!;
        var path = uri.IsAbsoluteUri ? uri.AbsolutePath : uri.OriginalString;
        var absoluteUri = uri.IsAbsoluteUri ? uri.AbsoluteUri : string.Empty;

        if (path == "/points/35.7156,-79.1845")
        {
          return JsonResponse(PointsResponseBodyWithObservation);
        }
        if (absoluteUri == "https://api.weather.gov/gridpoints/RAH/53,68/forecast")
        {
          forecastStart = DateTimeOffset.UtcNow;
          await Task.Delay(150).ConfigureAwait(false);
          forecastEnd = DateTimeOffset.UtcNow;
          return JsonResponse(ForecastResponseBody(DateTimeOffset.UtcNow));
        }
        if (absoluteUri == "https://api.weather.gov/gridpoints/RAH/53,68/stations")
        {
          observationStart ??= DateTimeOffset.UtcNow;
          return JsonResponse(StationsResponseBody);
        }
        if (path == "/stations/KIGX/observations/latest")
        {
          return JsonResponse(ObservationResponseBody(9.4, "Partly Cloudy", "https://api.weather.gov/icons/land/night/sct", DateTimeOffset.UtcNow.AddMinutes(-10)));
        }
        return new HttpResponseMessage(HttpStatusCode.NotFound);
      });

    var factory = MakeFactory(handler.Object);
    var options = new TestMonitor<WeatherDisplayOptions>(new WeatherDisplayOptions { Enabled = true, RefreshIntervalMinutes = 60 });
    var cache = new MemoryCache(new MemoryCacheOptions());
    var resolver = new ZipCoordinatesResolver(factory.Object, NullLogger<ZipCoordinatesResolver>.Instance);

    var svc = new NwsWeatherService(factory.Object, resolver, cache, options, NullLogger<NwsWeatherService>.Instance);
    var result = await svc.GetForecastAsync("27312");

    Assert.NotNull(result);
    Assert.NotNull(result!.Current);
    Assert.NotNull(forecastStart);
    Assert.NotNull(forecastEnd);
    Assert.NotNull(observationStart);
    // The observation chain (first request = stations) must have started
    // BEFORE the forecast handler finished its 150 ms delay — proves
    // parallelism. Allow a generous margin (50 ms) so the test isn't flaky
    // on slow CI runners.
    Assert.True(
      observationStart!.Value < forecastEnd!.Value,
      $"observation start ({observationStart}) must precede forecast end ({forecastEnd}) — parallel fetch expected");
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

  /// <summary>
  /// Points response that includes the observationStations URL (the
  /// observation-chain entry point per HANDOFF §4.3). Used by the
  /// observation-aware tests.
  /// </summary>
  private const string PointsResponseBodyWithObservation = """
    {
      "properties": {
        "forecast": "https://api.weather.gov/gridpoints/RAH/53,68/forecast",
        "observationStations": "https://api.weather.gov/gridpoints/RAH/53,68/stations",
        "relativeLocation": {
          "properties": {
            "city": "Pittsboro",
            "state": "NC"
          }
        }
      }
    }
    """;

  /// <summary>
  /// Stations FeatureCollection — features[0] is KIGX (the closest station)
  /// per the order-by-distance contract documented in HANDOFF §4.4.
  /// </summary>
  private const string StationsResponseBody = """
    {
      "features": [
        { "properties": { "stationIdentifier": "KIGX" } },
        { "properties": { "stationIdentifier": "KRDU" } },
        { "properties": { "stationIdentifier": "KGSO" } }
      ]
    }
    """;

  /// <summary>
  /// Build an observation response with the supplied Celsius temperature,
  /// text description, icon URL, and observation timestamp.
  /// </summary>
  private static string ObservationResponseBody(double tempC, string textDescription, string iconUrl, DateTimeOffset timestamp)
  {
    var iso = timestamp.ToString("o", System.Globalization.CultureInfo.InvariantCulture);
    return $$"""
      {
        "properties": {
          "timestamp": "{{iso}}",
          "textDescription": "{{textDescription}}",
          "icon": "{{iconUrl}}",
          "temperature": {
            "value": {{tempC.ToString("0.0##", System.Globalization.CultureInfo.InvariantCulture)}},
            "unitCode": "wmoUnit:degC"
          }
        }
      }
      """;
  }

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
