using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Core.Interfaces;
using Radio.Infrastructure.Weather;

namespace Radio.Infrastructure.DependencyInjection;

/// <summary>
/// DI wiring for the NWS weather service (ADR-022 §2.4).
/// </summary>
public static class WeatherServiceExtensions
{
  /// <summary>
  /// Registers <see cref="IWeatherService"/> + the two named HttpClients
  /// (<c>nws</c> for api.weather.gov, <c>weather-zippopotam</c> for the
  /// ZIP-to-coords lookup) + <see cref="Microsoft.Extensions.Caching.Memory.IMemoryCache"/>
  /// + the <c>WeatherDisplayOptions</c> binding.
  ///
  /// Safe to call after other Infrastructure registrations. <c>AddMemoryCache</c>
  /// is idempotent — if the API host already registered one (currently
  /// neither API nor Web does), this is a no-op for that line.
  /// </summary>
  public static IServiceCollection AddRadioWeather(
    this IServiceCollection services,
    IConfiguration configuration)
  {
    services.Configure<WeatherDisplayOptions>(
      configuration.GetSection(WeatherDisplayOptions.SectionName));

    services.AddMemoryCache();

    // Resolve the contact email at HttpClient configuration time. Reuses
    // IOptionsMonitor so a change to ContactEmail mid-process gets picked up
    // by the NEXT client constructed (per ASP.NET Core's named-client
    // lifetime — handlers are cached for 2 min by default).
    services.AddHttpClient("nws", (sp, client) =>
    {
      var opts = sp.GetRequiredService<IOptionsMonitor<WeatherDisplayOptions>>().CurrentValue;
      var email = string.IsNullOrWhiteSpace(opts.ContactEmail)
        ? "radioconsole@localhost.local"
        : opts.ContactEmail;

      client.BaseAddress = new Uri("https://api.weather.gov");
      client.Timeout = TimeSpan.FromSeconds(15);
      // NWS requires a User-Agent with contact info. Per their docs:
      // "include contact information (website or email)" so they can reach
      // out if our traffic causes issues. ParseAdd is forgiving on minor
      // format quirks the curly-brace form would reject.
      client.DefaultRequestHeaders.UserAgent.ParseAdd($"RadioConsole/1.0 (+{email})");
      client.DefaultRequestHeaders.Accept.ParseAdd("application/geo+json");
    });

    services.AddHttpClient("weather-zippopotam", (sp, client) =>
    {
      var opts = sp.GetRequiredService<IOptionsMonitor<WeatherDisplayOptions>>().CurrentValue;
      var email = string.IsNullOrWhiteSpace(opts.ContactEmail)
        ? "radioconsole@localhost.local"
        : opts.ContactEmail;

      client.BaseAddress = new Uri("https://api.zippopotam.us");
      client.Timeout = TimeSpan.FromSeconds(10);
      client.DefaultRequestHeaders.UserAgent.ParseAdd($"RadioConsole/1.0 (+{email})");
    });

    services.AddSingleton<ZipCoordinatesResolver>();
    services.AddSingleton<IWeatherService, NwsWeatherService>();

    return services;
  }
}
