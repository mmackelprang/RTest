using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Core.Interfaces;
using Radio.Core.Models;
using Radio.Infrastructure.Weather.Dtos;

namespace Radio.Infrastructure.Weather;

/// <summary>
/// <see cref="IWeatherService"/> implementation that talks to the US National
/// Weather Service per ADR-022. Encapsulates the three-call chain
/// (ZIP → coords → grid → forecast) plus the stale-while-revalidate cache.
///
/// Callers see only <c>Task&lt;WeatherForecast?&gt;</c>. Upstream failures never
/// surface as exceptions — the sleep screen treats null as "hide the pane,"
/// which is the load-bearing failure-mode contract from ADR §2.3.
/// </summary>
public sealed class NwsWeatherService : IWeatherService
{
  private readonly IHttpClientFactory _httpClientFactory;
  private readonly ZipCoordinatesResolver _zipResolver;
  private readonly IMemoryCache _cache;
  private readonly IOptionsMonitor<WeatherDisplayOptions> _options;
  private readonly ILogger<NwsWeatherService> _logger;

  // Cache key prefixes — kept here so callers can't accidentally collide with
  // unrelated cache entries.
  private const string CoordsKeyPrefix = "weather:zip:";
  private const string CoordsKeySuffix = ":coords";
  private const string GridKeyPrefix = "weather:zip:";
  private const string GridKeySuffix = ":grid";
  private const string ForecastKeyPrefix = "weather:zip:";
  private const string ForecastKeySuffix = ":forecast";

  // ADR §2.3 — grid assignments are stable; coords never move; forecast has a
  // fresh-TTL (configurable) and a stale-serve TTL (24h hard ceiling).
  private static readonly TimeSpan GridCacheTtl = TimeSpan.FromDays(30);
  private static readonly TimeSpan StaleServeHorizon = TimeSpan.FromHours(24);

  // Per-ZIP semaphores serialize cold-cache fills for the coords and grid
  // steps so two simultaneous callers can't both invoke the async factory
  // (the IMemoryCache.GetOrCreateAsync pitfall — it does NOT debounce
  // concurrent first-fills). Static + ConcurrentDictionary because all
  // instances of the service share the same cache; allocating a SemaphoreSlim
  // per ZIP is cheaper than letting zippopotam.us / NWS see duplicate first
  // calls on every cold start.
  //
  // The forecast cache is NOT lock-guarded — its stale-while-revalidate path
  // is designed to tolerate a duplicate refresh on rare race (worst case:
  // one extra forecast fetch per fresh-TTL boundary), and adding a third
  // semaphore tier would just queue requests behind a slow upstream call.
  private static readonly ConcurrentDictionary<string, SemaphoreSlim> _coordsLocks = new(StringComparer.Ordinal);
  private static readonly ConcurrentDictionary<string, SemaphoreSlim> _gridLocks = new(StringComparer.Ordinal);

  public NwsWeatherService(
    IHttpClientFactory httpClientFactory,
    ZipCoordinatesResolver zipResolver,
    IMemoryCache cache,
    IOptionsMonitor<WeatherDisplayOptions> options,
    ILogger<NwsWeatherService> logger)
  {
    _httpClientFactory = httpClientFactory;
    _zipResolver = zipResolver;
    _cache = cache;
    _options = options;
    _logger = logger;
  }

  /// <inheritdoc/>
  public async Task<WeatherForecast?> GetForecastAsync(string zip, CancellationToken ct = default)
  {
    var opts = _options.CurrentValue;
    if (!opts.Enabled)
    {
      _logger.LogDebug("Weather feature disabled; skipping NWS lookup for ZIP {Zip}", zip);
      return null;
    }
    if (!ZipCoordinatesResolver.IsValidZip(zip))
    {
      _logger.LogDebug("ZIP {Zip} rejected: not 5 digits", zip);
      return null;
    }

    var forecastKey = ForecastKeyPrefix + zip + ForecastKeySuffix;
    var freshTtl = TimeSpan.FromMinutes(Math.Clamp(opts.RefreshIntervalMinutes, 15, 360));

    // Fresh cache hit — return immediately. No upstream call.
    if (_cache.TryGetValue<CachedForecast>(forecastKey, out var cached) && cached is not null)
    {
      var age = DateTimeOffset.UtcNow - cached.FetchedAtUtc;
      if (age < freshTtl)
      {
        _logger.LogDebug("Forecast cache hit (fresh, age {Age}s) for ZIP {Zip}", (int)age.TotalSeconds, zip);
        return cached.Forecast with { IsStale = false };
      }
    }

    // Cache miss OR stale — attempt a refresh. On failure with a cached entry
    // inside the stale-serve horizon, return cached + IsStale=true.
    try
    {
      var fresh = await FetchForecastAsync(zip, ct).ConfigureAwait(false);
      if (fresh is null)
      {
        // FetchForecastAsync returned null = ZIP doesn't resolve. No cache to
        // fall back on (would only have one if a previous fetch succeeded for
        // this ZIP, which would mean it DOES resolve). Surface null.
        return null;
      }

      // Write to cache with no absolute expiry — the fresh/stale logic is
      // age-based so we can serve stale on upstream failure.
      _cache.Set(forecastKey, fresh, new MemoryCacheEntryOptions
      {
        // Keep entries up to 48h so a brief outage can still serve stale
        // data through the 24h stale-serve horizon plus a buffer for GC.
        AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(48),
      });

      _logger.LogInformation("Forecast refreshed for ZIP {Zip} (generated {Generated})", zip, fresh.Forecast.GeneratedAtUtc);
      return fresh.Forecast with { IsStale = false };
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested)
    {
      throw;
    }
    catch (Exception ex)
    {
      // Refresh failed. If we have a cache entry within the stale-serve
      // horizon, return it marked stale. Otherwise surface null.
      if (cached is not null)
      {
        var age = DateTimeOffset.UtcNow - cached.FetchedAtUtc;
        if (age <= StaleServeHorizon)
        {
          _logger.LogWarning(ex, "Forecast refresh failed for ZIP {Zip}; serving stale (age {Age}m)", zip, (int)age.TotalMinutes);
          return cached.Forecast with { IsStale = true };
        }
        _logger.LogError(ex, "Forecast refresh failed for ZIP {Zip} and cached entry is too old (age {Age}h) — returning null", zip, (int)age.TotalHours);
      }
      else
      {
        _logger.LogError(ex, "Forecast refresh failed for ZIP {Zip} with no cached fallback — returning null", zip);
      }
      return null;
    }
  }

  /// <summary>
  /// Walks the full NWS chain. Returns a <see cref="CachedForecast"/> on
  /// success or <c>null</c> when the ZIP can't be resolved (unknown ZIP).
  /// Throws on transient upstream failure so the caller can serve stale.
  /// </summary>
  private async Task<CachedForecast?> FetchForecastAsync(string zip, CancellationToken ct)
  {
    // Step 1 — coords. Cached per process lifetime (centroids don't move).
    // Stampede-guarded: see GetOrFillCoordsAsync.
    var coords = await GetOrFillCoordsAsync(zip, ct).ConfigureAwait(false);
    if (coords is null)
    {
      _logger.LogInformation("Skipping forecast fetch — ZIP {Zip} did not resolve to coordinates", zip);
      return null;
    }

    // Step 2 — grid. Cached for 30 days (NWS grid assignments are stable).
    // Stampede-guarded: see GetOrFillGridAsync.
    var gridInfo = await GetOrFillGridAsync(zip, coords, ct).ConfigureAwait(false);
    if (gridInfo is null || string.IsNullOrEmpty(gridInfo.ForecastUrl))
    {
      throw new InvalidOperationException($"NWS points endpoint did not return a forecast URL for ZIP {zip}");
    }

    // Step 3 — forecast.
    var nwsForecast = await FetchForecastPeriodsAsync(gridInfo.ForecastUrl, ct).ConfigureAwait(false);

    // Aggregate day+night pairs into calendar-day WeatherDay records.
    var days = AggregateToDays(nwsForecast.Periods ?? new List<NwsForecastPeriod>(), DateTime.Now.Date);

    var locationName = !string.IsNullOrEmpty(gridInfo.City) && !string.IsNullOrEmpty(gridInfo.State)
      ? $"{gridInfo.City}, {gridInfo.State}"
      : coords.LocationName;

    var forecast = new WeatherForecast(
      Zip: zip,
      LocationName: locationName,
      GeneratedAtUtc: nwsForecast.GeneratedAt ?? DateTimeOffset.UtcNow,
      FetchedAtUtc: DateTimeOffset.UtcNow,
      IsStale: false,
      Days: days);

    return new CachedForecast(forecast, DateTimeOffset.UtcNow);
  }

  /// <summary>
  /// Fetches ZIP coordinates with stampede protection — double-checked cache
  /// inspection around a per-ZIP <see cref="SemaphoreSlim"/> so two
  /// simultaneous cold-cache callers don't both hit zippopotam.us. This
  /// fixes the <c>IMemoryCache.GetOrCreateAsync</c> concurrency pitfall
  /// flagged in the PR #415 first review.
  /// </summary>
  private async Task<ZipCoordinates?> GetOrFillCoordsAsync(string zip, CancellationToken ct)
  {
    var key = CoordsKeyPrefix + zip + CoordsKeySuffix;
    if (_cache.TryGetValue<ZipCoordinates>(key, out var cached) && cached is not null)
    {
      return cached;
    }

    var sem = _coordsLocks.GetOrAdd(zip, _ => new SemaphoreSlim(1, 1));
    await sem.WaitAsync(ct).ConfigureAwait(false);
    try
    {
      // Re-check after acquiring the lock — another caller may have filled
      // the cache while we waited. Without this guard, the second caller in
      // would still issue a redundant upstream call.
      if (_cache.TryGetValue<ZipCoordinates>(key, out cached) && cached is not null)
      {
        return cached;
      }

      var fresh = await _zipResolver.ResolveAsync(zip, ct).ConfigureAwait(false);
      if (fresh is not null)
      {
        // No expiry — ADR §2.2 says coords are cached for process lifetime
        // (ZIP centroids do not move). NeverRemove keeps the entry alive
        // under memory pressure too.
        _cache.Set(key, fresh, new MemoryCacheEntryOptions
        {
          Priority = CacheItemPriority.NeverRemove,
        });
      }
      // Note: a null result is NOT cached — we want a future ResolveAsync
      // call to retry (transient network failure vs. genuinely unknown ZIP
      // are indistinguishable here, and re-asking later is cheap).
      return fresh;
    }
    finally
    {
      sem.Release();
    }
  }

  /// <summary>
  /// Fetches the NWS grid info for a ZIP with the same stampede protection
  /// as <see cref="GetOrFillCoordsAsync"/>. Grid assignments are stable per
  /// ADR §2.2 — cached for 30 days.
  /// </summary>
  private async Task<GridInfo?> GetOrFillGridAsync(string zip, ZipCoordinates coords, CancellationToken ct)
  {
    var key = GridKeyPrefix + zip + GridKeySuffix;
    if (_cache.TryGetValue<GridInfo>(key, out var cached) && cached is not null)
    {
      return cached;
    }

    var sem = _gridLocks.GetOrAdd(zip, _ => new SemaphoreSlim(1, 1));
    await sem.WaitAsync(ct).ConfigureAwait(false);
    try
    {
      if (_cache.TryGetValue<GridInfo>(key, out cached) && cached is not null)
      {
        return cached;
      }

      var fresh = await FetchPointsAsync(coords, ct).ConfigureAwait(false);
      if (fresh is not null)
      {
        _cache.Set(key, fresh, new MemoryCacheEntryOptions
        {
          AbsoluteExpirationRelativeToNow = GridCacheTtl,
        });
      }
      return fresh;
    }
    finally
    {
      sem.Release();
    }
  }

  private async Task<GridInfo?> FetchPointsAsync(ZipCoordinates coords, CancellationToken ct)
  {
    var client = _httpClientFactory.CreateClient("nws");
    // NWS expects "lat,lon" with up to 4 decimal places per their docs.
    var lat = coords.Latitude.ToString("0.####", CultureInfo.InvariantCulture);
    var lon = coords.Longitude.ToString("0.####", CultureInfo.InvariantCulture);
    var url = $"/points/{lat},{lon}";

    using var response = await client.GetAsync(url, ct).ConfigureAwait(false);
    response.EnsureSuccessStatusCode();
    var body = await response.Content.ReadFromJsonAsync<NwsPointsResponse>(ct).ConfigureAwait(false);

    var props = body?.Properties;
    if (props is null)
    {
      return null;
    }

    return new GridInfo(
      ForecastUrl: props.Forecast ?? string.Empty,
      City: props.RelativeLocation?.Properties?.City ?? string.Empty,
      State: props.RelativeLocation?.Properties?.State ?? string.Empty);
  }

  private async Task<NwsForecastProperties> FetchForecastPeriodsAsync(string forecastUrl, CancellationToken ct)
  {
    var client = _httpClientFactory.CreateClient("nws");
    // The forecast URL is absolute (https://api.weather.gov/...) — pass it
    // through directly; HttpClient handles absolute URIs even when BaseAddress
    // is configured.
    using var response = await client.GetAsync(forecastUrl, ct).ConfigureAwait(false);
    response.EnsureSuccessStatusCode();
    var body = await response.Content.ReadFromJsonAsync<NwsForecastResponse>(ct).ConfigureAwait(false);

    return body?.Properties ?? new NwsForecastProperties();
  }

  /// <summary>
  /// Collapses NWS day+night periods into calendar-day records (high from the
  /// daytime period, low from the matching overnight period). Up to 3 days
  /// returned. Day-name format per Designer §10 Q2: "Today" / "Tomorrow" /
  /// 3-letter weekday (invariant culture).
  /// </summary>
  internal static List<WeatherDay> AggregateToDays(IReadOnlyList<NwsForecastPeriod> periods, DateTime kioskToday)
  {
    var byDate = new Dictionary<DateOnly, DayBucket>();

    // Walk periods in order. Each period contributes its high (daytime) or
    // low (overnight) to the day matching its startTime date in kiosk local
    // time. Overnight periods that START before midnight (e.g. "Tonight")
    // belong to the same calendar day as the preceding day period; overnight
    // periods that start AFTER midnight (e.g. "Monday Night") belong to that
    // calendar day's day period.
    foreach (var period in periods)
    {
      if (period.StartTime is null)
      {
        continue;
      }

      // For "Tonight" style periods (isDaytime=false, startTime evening), the
      // associated day is the same calendar date the period starts on. NWS's
      // own convention pairs the day period and the following overnight as
      // the same "day" in the user's mind.
      var date = DateOnly.FromDateTime(period.StartTime.Value.LocalDateTime);

      if (!byDate.TryGetValue(date, out var bucket))
      {
        bucket = new DayBucket();
        byDate[date] = bucket;
      }

      if (period.IsDaytime)
      {
        bucket.DayPeriod = period;
      }
      else
      {
        // For overnight periods, attribute the LOW to the SAME calendar day
        // when the period started in the evening of that day (i.e. its date
        // matches a day-period we've already seen). When the period starts
        // after midnight (e.g. "Monday Night" starting Mon 18:00), this still
        // matches the day-period's date because Mon 18:00 has DateOnly=Mon.
        bucket.NightPeriod = period;
      }
    }

    // Sort by date ascending, take up to 3, build WeatherDay records.
    var ordered = byDate.OrderBy(kv => kv.Key).Take(3).ToList();
    var todayDate = DateOnly.FromDateTime(kioskToday);
    var days = new List<WeatherDay>(ordered.Count);

    foreach (var (date, bucket) in ordered)
    {
      // Prefer day-period fields (icon, condition, narrative) — they're what
      // the user sees while glancing during the day. Fall back to night
      // period if there's no day period (rare, e.g. late-evening boundary
      // when "Today" has already rolled past).
      var headlinePeriod = bucket.DayPeriod ?? bucket.NightPeriod;
      if (headlinePeriod is null)
      {
        continue;
      }

      var highF = bucket.DayPeriod?.Temperature ?? bucket.NightPeriod?.Temperature ?? 0;
      var lowF = bucket.NightPeriod?.Temperature ?? bucket.DayPeriod?.Temperature ?? 0;

      // NWS US grids report in F. Convert to C with standard formula
      // and round to nearest int.
      var highC = (int)Math.Round((highF - 32) * 5.0 / 9.0, MidpointRounding.AwayFromZero);
      var lowC = (int)Math.Round((lowF - 32) * 5.0 / 9.0, MidpointRounding.AwayFromZero);

      days.Add(new WeatherDay(
        Date: date,
        DayName: ComputeDayName(date, todayDate),
        HighF: highF,
        LowF: lowF,
        HighC: highC,
        LowC: lowC,
        ConditionShort: headlinePeriod.ShortForecast ?? string.Empty,
        ConditionLong: headlinePeriod.DetailedForecast ?? string.Empty,
        PrecipitationProbabilityPct: headlinePeriod.ProbabilityOfPrecipitation?.Value ?? 0,
        IconKey: NwsIconMapper.MapToIconKey(headlinePeriod.Icon),
        NwsForecastUrl: null));
    }

    return days;
  }

  /// <summary>
  /// Day name per Designer §10 Q2: "Today" (index 0), "Tomorrow" (index 1),
  /// 3-letter English weekday (index 2+, invariant culture).
  /// </summary>
  internal static string ComputeDayName(DateOnly date, DateOnly today)
  {
    var diff = date.DayNumber - today.DayNumber;
    return diff switch
    {
      0 => "Today",
      1 => "Tomorrow",
      _ => date.ToDateTime(TimeOnly.MinValue).ToString("ddd", CultureInfo.InvariantCulture),
    };
  }

  /// <summary>
  /// Test-only seam: inject a forecast cache entry with a controlled
  /// FetchedAtUtc so the stale-while-revalidate path can be exercised
  /// without waiting out the real fresh-TTL. Exposed via
  /// <c>[InternalsVisibleTo("Radio.Infrastructure.Tests")]</c> on the
  /// Infrastructure csproj — see <c>NwsWeatherServiceTests.GetForecastAsync_StaleEntry_RefreshFails_ReturnsStale</c>.
  ///
  /// Keeping this as an explicit named method (rather than promoting the
  /// private CachedForecast record to internal) means the test depends on a
  /// stable, intention-revealing API rather than on the cache value's
  /// internal struct shape — that record can be refactored freely.
  /// </summary>
  internal void SetForecastCacheEntryForTesting(string zip, WeatherForecast forecast, DateTimeOffset fetchedAtUtc)
  {
    var key = ForecastKeyPrefix + zip + ForecastKeySuffix;
    _cache.Set(key, new CachedForecast(forecast, fetchedAtUtc), new MemoryCacheEntryOptions
    {
      AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(48),
    });
  }

  // ------------------------ internal helper types ------------------------

  private sealed record GridInfo(string ForecastUrl, string City, string State);

  private sealed record CachedForecast(WeatherForecast Forecast, DateTimeOffset FetchedAtUtc);

  private sealed class DayBucket
  {
    public NwsForecastPeriod? DayPeriod { get; set; }
    public NwsForecastPeriod? NightPeriod { get; set; }
  }
}
