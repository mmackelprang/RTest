using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;
using Radio.Infrastructure.Audio.Fingerprinting;
using Radio.Infrastructure.Audio.Fingerprinting.Data;

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

    // Register database context as singleton (manages connection)
    services.AddSingleton<FingerprintDbContext>();

    // Register repositories as scoped
    services.AddScoped<IFingerprintCacheRepository, SqliteFingerprintCacheRepository>();
    services.AddScoped<ITrackMetadataRepository, SqliteTrackMetadataRepository>();
    services.AddScoped<IPlayHistoryRepository, SqlitePlayHistoryRepository>();

    // Register fingerprint service
    services.AddSingleton<IFingerprintService, MockFingerprintService>();

    // Register metadata lookup service as scoped (uses repositories)
    services.AddScoped<IMetadataLookupService, MetadataLookupService>();

    // Register audio tap as scoped
    services.AddScoped<IAudioSampleProvider, SoundFlowAudioTap>();

    // Register background identification service
    services.AddHostedService<BackgroundIdentificationService>();

    return services;
  }
}
