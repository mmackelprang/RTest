namespace Radio.Infrastructure.DependencyInjection;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Radio.Configuration.Models;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;
using Radio.Infrastructure.Audio.Services;
using Radio.Infrastructure.Configuration.Abstractions;
using Radio.Infrastructure.Configuration.Backup;
using Radio.Infrastructure.Configuration.Services;

/// <summary>
/// Extension methods for registering Radio-specific configuration services.
/// Wraps <see cref="Radio.Configuration.ConfigurationServiceExtensions.AddManagedConfiguration"/>
/// and adds unified backup, preferences persistence, and database path wiring.
/// </summary>
public static class ConfigurationServiceExtensions
{
  /// <summary>
  /// Adds the managed configuration infrastructure plus Radio-specific services.
  /// </summary>
  public static IServiceCollection AddManagedConfiguration(
    this IServiceCollection services,
    IConfiguration configuration)
  {
    // Register the standalone configuration library services
    Radio.Configuration.ConfigurationServiceExtensions.AddManagedConfiguration(services, configuration);

    // Bind unified database options (Radio-specific, drives all DB paths)
    services.Configure<DatabaseOptions>(
      configuration.GetSection(DatabaseOptions.SectionName));

    // Register database path resolver (Radio-specific, shared across subsystems)
    services.AddSingleton<DatabasePathResolver>();

    // Post-configure: wire DatabasePathResolver paths into ConfigurationOptions
    // so the standalone library uses the Radio app's unified path layout
    services.AddSingleton<IPostConfigureOptions<ConfigurationOptions>>(sp =>
    {
      var resolver = sp.GetRequiredService<DatabasePathResolver>();
      return new PostConfigureOptions<ConfigurationOptions>(
        Options.DefaultName,
        opts =>
        {
          opts.DatabasePath ??= resolver.GetConfigurationDatabasePath();
          opts.SecretsDatabasePath ??= resolver.GetSecretsDatabasePath();
        });
    });

    // Register unified database backup service (backs up config + fingerprinting DBs)
    services.AddSingleton<IUnifiedDatabaseBackupService, UnifiedDatabaseBackupService>();

    // ENC-5. Singleton: per-band dial memory is one physical dial's state, read on every band
    // commit and written after every tune, and every consumer of it is a singleton.
    //
    // Registered here, beside the configuration manager it reads through, rather than in
    // AddRotaryEncoders: RotaryEncoderRegistrationTests builds a provider holding only AddLogging
    // and AddRotaryEncoders, so a registration there would need IConfigurationManager in that
    // provider too and the failure would surface at service start on the appliance.
    services.AddSingleton<IRadioBandMemory, RadioBandMemoryService>();

    // Register preferences persistence service as a background service
    services.AddHostedService<PreferencesPersistenceService>();

    return services;
  }

  /// <summary>
  /// Adds secret resolution for the specified options type.
  /// Delegates to <see cref="Radio.Configuration.ConfigurationServiceExtensions.AddSecretResolution{TOptions}"/>.
  /// </summary>
  public static IServiceCollection AddSecretResolution<TOptions>(this IServiceCollection services)
    where TOptions : class
  {
    return Radio.Configuration.ConfigurationServiceExtensions.AddSecretResolution<TOptions>(services);
  }
}
