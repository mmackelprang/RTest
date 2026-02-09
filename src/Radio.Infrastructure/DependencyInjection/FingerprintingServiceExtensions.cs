using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Radio.Core.Configuration;
using Radio.Core.Interfaces;
using Radio.Core.Interfaces.Audio;
using Radio.Infrastructure.Audio.Fingerprinting;
using Radio.Infrastructure.Audio.Fingerprinting.Data;
using Radio.Infrastructure.Audio.Playlists;
using Radio.Infrastructure.Audio.Services;

namespace Radio.Infrastructure.DependencyInjection;

/// <summary>
/// Extension methods for registering fingerprinting services.
/// </summary>
public static class FingerprintingServiceExtensions
{
  /// <summary>
  /// Adds fingerprinting services to the service collection.
  /// </summary>
  /// <param name="services">The service collection.</param>
  /// <param name="configuration">The configuration.</param>
  /// <returns>The service collection for chaining.</returns>
  public static IServiceCollection AddFingerprinting(
    this IServiceCollection services,
    IConfiguration configuration)
  {
    // Bind configuration
    services.Configure<FingerprintingOptions>(
      configuration.GetSection(FingerprintingOptions.SectionName));
    services.AddSecretResolution<FingerprintingOptions>();

    // Register database context as singleton (manages connection)
    services.AddSingleton<FingerprintDbContext>();

    // Register repositories as scoped
    services.AddScoped<IFingerprintCacheRepository, SqliteFingerprintCacheRepository>();
    services.AddScoped<ITrackMetadataRepository, SqliteTrackMetadataRepository>();
    services.AddScoped<IPlayHistoryRepository, SqlitePlayHistoryRepository>();
    services.AddScoped<IRadioPresetRepository, SqliteRadioPresetRepository>();
    services.AddScoped<IAudioFileRepository, SqliteAudioFileRepository>();
    services.AddScoped<IPlaylistRepository, SqlitePlaylistRepository>();

    // Register fingerprint service
    services.AddSingleton<IFingerprintService, ChromaprintFingerprintService>();

    // Register radio preset service (scoped to match repository)
    services.AddScoped<IRadioPresetService, RadioPresetService>();

    // Register AcoustID client as scoped
    // Note: HttpClient is created directly rather than using IHttpClientFactory due to
    // package version constraints. This is acceptable because:
    // 1. AcoustID lookups are infrequent (once per fingerprint, rate-limited to 3/sec)
    // 2. The client is scoped, so HttpClient instances are tied to request lifetime
    // 3. Adding IHttpClientFactory would require upgrading Microsoft.Extensions packages
    services.AddScoped<AcoustIdClient>(sp =>
    {
      var httpClient = new HttpClient
      {
        Timeout = TimeSpan.FromSeconds(
          configuration.GetSection(FingerprintingOptions.SectionName)
            .Get<FingerprintingOptions>()?.AcoustId.TimeoutSeconds ?? 10)
      };
      httpClient.DefaultRequestHeaders.Add("User-Agent", "RadioConsole/1.0");
      
      return new AcoustIdClient(
        httpClient,
        sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<AcoustIdClient>>(),
        sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<FingerprintingOptions>>(),
        ownsHttpClient: true); // HttpClient is created here, so client owns it
    });

    // Register metadata lookup service as scoped (uses repositories)
    services.AddScoped<IMetadataLookupService>(sp =>
    {
      var httpClient = new HttpClient
      {
        Timeout = TimeSpan.FromSeconds(
          configuration.GetSection(FingerprintingOptions.SectionName)
            .Get<FingerprintingOptions>()?.MusicBrainz.TimeoutSeconds ?? 10)
      };

      return new MetadataLookupService(
        sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<MetadataLookupService>>(),
        sp.GetRequiredService<IFingerprintCacheRepository>(),
        sp.GetRequiredService<ITrackMetadataRepository>(),
        sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<FingerprintingOptions>>(),
        httpClient,
        sp.GetService<AcoustIdClient>());
    });

    // Register audio tap as scoped
    services.AddScoped<IAudioSampleProvider, SoundFlowAudioTap>();

    // Register background identification service as singleton so other components
    // (AudioManager, FilePlayerAudioSource, etc.) can subscribe to TrackIdentified events.
    // AddHostedService alone only registers as IHostedService, not as the concrete type.
    services.AddSingleton<BackgroundIdentificationService>();
    services.AddHostedService(sp => sp.GetRequiredService<BackgroundIdentificationService>());

    return services;
  }
}
