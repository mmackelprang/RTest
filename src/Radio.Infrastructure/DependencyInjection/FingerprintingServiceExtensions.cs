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
    services.AddSingleton<ITTSVoiceRepository, SqliteTTSVoiceRepository>();

    // Register radio preset service (scoped to match repository)
    services.AddScoped<IRadioPresetService, RadioPresetService>();

    // Read fingerprinting options for HTTP client configuration
    var fpOptions = configuration.GetSection(FingerprintingOptions.SectionName)
      .Get<FingerprintingOptions>();

    // Register named HTTP client for MusicBrainz (used for cover art search)
    var mb = fpOptions?.MusicBrainz;
    services.AddHttpClient("MusicBrainz", client =>
    {
      client.Timeout = TimeSpan.FromSeconds(mb?.TimeoutSeconds ?? 10);
      if (!string.IsNullOrEmpty(mb?.ApplicationName))
      {
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
          $"{mb.ApplicationName}/{mb.ApplicationVersion} ({mb.ContactEmail})");
      }
    });

    // Register metadata lookup service as scoped (uses IHttpClientFactory for connection pooling)
    services.AddScoped<IMetadataLookupService>(sp =>
    {
      var factory = sp.GetRequiredService<IHttpClientFactory>();
      var httpClient = factory.CreateClient("MusicBrainz");
      return new MetadataLookupService(
        sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<MetadataLookupService>>(),
        sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<FingerprintingOptions>>(),
        httpClient,
        sp.GetService<IMetricsCollector>());
    });

    // Register audio tap as scoped
    services.AddScoped<IAudioSampleProvider, SoundFlowAudioTap>();

    // Register SongRec (Shazam) recognizer — checks IsAvailable at runtime
    services.AddSingleton<ISongRecRecognitionService, SongRecRecognitionService>();

    // Register background identification service as singleton so other components
    // (AudioManager, FilePlayerAudioSource, etc.) can subscribe to TrackIdentified events.
    // AddHostedService alone only registers as IHostedService, not as the concrete type.
    services.AddSingleton<BackgroundIdentificationService>(sp =>
      new BackgroundIdentificationService(
        sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<BackgroundIdentificationService>>(),
        sp,
        sp.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<FingerprintingOptions>>(),
        sp.GetService<IMetricsCollector>()));
    services.AddHostedService(sp => sp.GetRequiredService<BackgroundIdentificationService>());

    return services;
  }
}
