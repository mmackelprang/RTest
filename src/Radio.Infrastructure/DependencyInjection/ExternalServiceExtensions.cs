using Microsoft.Extensions.DependencyInjection;
using Radio.Core.Interfaces.External;
using Radio.Infrastructure.External.Spotify;

namespace Radio.Infrastructure.DependencyInjection;

/// <summary>
/// Extension methods for registering external service integrations.
/// </summary>
public static class ExternalServiceExtensions
{
  /// <summary>
  /// Adds Spotify authentication and integration services to the service collection.
  /// </summary>
  /// <param name="services">The service collection.</param>
  /// <returns>The service collection for chaining.</returns>
  public static IServiceCollection AddSpotifyServices(this IServiceCollection services)
  {
    // Register Spotify authentication service (singleton to maintain token state)
    services.AddSingleton<SpotifyAuthService>();
    services.AddSingleton<ISpotifyAuthService>(sp => sp.GetRequiredService<SpotifyAuthService>());

    return services;
  }
}
