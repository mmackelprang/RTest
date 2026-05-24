using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Core.Interfaces;
using Radio.Core.Models;

namespace Radio.API.Controllers;

/// <summary>
/// Read-only weather endpoint backing the kiosk sleep-screen forecast pane.
/// Wraps <see cref="IWeatherService"/> and translates the typed nullable
/// return into HTTP semantics per ADR-022 §2.7:
///   * 200 + body  — forecast available (fresh or stale)
///   * 400         — ZIP isn't 5 digits
///   * 404         — feature disabled in config (Display:Weather:Enabled=false)
///   * 503         — upstream failed AND no cache available
///
/// The 503 is what the Web layer's WeatherApiService translates into a null
/// return for the Sleep page, which then hides the forecast pane (load-bearing
/// failure-mode contract from ADR §2.3).
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class WeatherController : ControllerBase
{
  private readonly IWeatherService _weather;
  private readonly IOptionsMonitor<WeatherDisplayOptions> _options;
  private readonly ILogger<WeatherController> _logger;

  public WeatherController(
    IWeatherService weather,
    IOptionsMonitor<WeatherDisplayOptions> options,
    ILogger<WeatherController> logger)
  {
    _weather = weather;
    _options = options;
    _logger = logger;
  }

  /// <summary>
  /// Returns the 3-day forecast for the supplied ZIP, or the configured
  /// default ZIP (<c>Display:Weather:Zip</c>) when the parameter is omitted.
  /// </summary>
  [HttpGet("forecast")]
  [ProducesResponseType(typeof(WeatherForecast), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  [ProducesResponseType(StatusCodes.Status404NotFound)]
  [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
  public async Task<ActionResult<WeatherForecast>> GetForecast(
    [FromQuery] string? zip = null,
    CancellationToken ct = default)
  {
    var opts = _options.CurrentValue;
    if (!opts.Enabled)
    {
      // 404 per ADR §2.7. The Web client treats 404 as "feature off" and
      // hides the forecast pane permanently for the session.
      return NotFound(new { error = "Weather feature is disabled" });
    }

    var effectiveZip = string.IsNullOrWhiteSpace(zip) ? opts.Zip : zip;

    // Validate up-front so we can return 400 (caller error) vs 503 (upstream
    // failure). The service returns null for both, but the HTTP boundary
    // needs to distinguish them so the Web client can show the right error.
    if (string.IsNullOrEmpty(effectiveZip) || effectiveZip.Length != 5 || !effectiveZip.All(char.IsDigit))
    {
      return BadRequest(new { error = "ZIP must be exactly 5 digits", zip = effectiveZip });
    }

    try
    {
      var forecast = await _weather.GetForecastAsync(effectiveZip, ct);
      if (forecast is null)
      {
        // Upstream failed AND no cache. Per ADR §2.7 this is 503 (rather
        // than 200 with a null body, which would be ambiguous).
        _logger.LogInformation("Forecast unavailable for ZIP {Zip} (no cache + upstream failure)", effectiveZip);
        return StatusCode(StatusCodes.Status503ServiceUnavailable,
          new { error = "Forecast unavailable", zip = effectiveZip });
      }

      return Ok(forecast);
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested)
    {
      // Client disconnect. ASP.NET will translate to 499/closed; no body.
      throw;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Unexpected error fetching forecast for ZIP {Zip}", effectiveZip);
      return StatusCode(StatusCodes.Status503ServiceUnavailable,
        new { error = "Forecast unavailable", zip = effectiveZip });
    }
  }
}
