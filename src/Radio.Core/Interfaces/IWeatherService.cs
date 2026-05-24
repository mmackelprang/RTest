using Radio.Core.Models;

namespace Radio.Core.Interfaces;

/// <summary>
/// Fetches and caches a 3-day forecast for a US ZIP code from the National
/// Weather Service. Implementations wrap the NWS three-call chain (ZIP →
/// coords → grid → forecast) and the stale-while-revalidate cache.
///
/// Defined in ADR-022 §2.3. The interface is deliberately small so a future
/// provider swap (Open-Meteo for international, OpenWeatherMap for richer
/// data) is a one-class change in Infrastructure.
/// </summary>
public interface IWeatherService
{
  /// <summary>
  /// Get the 3-day forecast for the supplied ZIP, honoring the cache and
  /// stale-while-revalidate rules.
  /// </summary>
  /// <param name="zip">
  /// 5-digit US ZIP code. Callers SHOULD validate format up-front; the service
  /// returns <c>null</c> for malformed or unknown ZIPs rather than throwing.
  /// </param>
  /// <param name="ct">Cancellation for the upstream HTTP calls.</param>
  /// <returns>
  /// A <see cref="WeatherForecast"/> when fresh or stale-but-serviceable
  /// cached data is available; <c>null</c> when no forecast can be served
  /// (cache empty AND upstream unreachable, or ZIP invalid, or weather
  /// feature disabled in config). The Sleep screen treats <c>null</c> as
  /// "hide the forecast pane" — this contract is load-bearing per ADR §2.3.
  /// Never throws on upstream failure.
  /// </returns>
  Task<WeatherForecast?> GetForecastAsync(string zip, CancellationToken ct = default);
}
