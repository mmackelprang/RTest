namespace Radio.Infrastructure.DependencyInjection;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Infrastructure.Configuration.Abstractions;
using Radio.Infrastructure.Configuration.Backup;
using Radio.Infrastructure.Configuration.Models;
using Radio.Infrastructure.Configuration.Options;
using Radio.Infrastructure.Configuration.Secrets;
using Radio.Infrastructure.Configuration.Services;
using Radio.Infrastructure.Configuration.Stores;

using IRadioConfigurationManager = Radio.Infrastructure.Configuration.Abstractions.IConfigurationManager;
using RadioConfigurationManager = Radio.Infrastructure.Configuration.Services.ConfigurationManager;

/// <summary>
/// Extension methods for registering configuration infrastructure services.
/// </summary>
public static class ConfigurationServiceExtensions
{
  /// <summary>
  /// Adds the managed configuration infrastructure to the service collection.
  /// Registers a composite secrets provider (SQLite primary + JSON fallback).
  /// </summary>
  /// <param name="services">The service collection.</param>
  /// <param name="configuration">The configuration instance.</param>
  /// <returns>The service collection for chaining.</returns>
  public static IServiceCollection AddManagedConfiguration(
    this IServiceCollection services,
    IConfiguration configuration)
  {
    // Bind unified database options
    services.Configure<DatabaseOptions>(
      configuration.GetSection(DatabaseOptions.SectionName));

    // Register database path resolver
    services.AddSingleton<DatabasePathResolver>();

    // Bind configuration options
    services.Configure<ConfigurationOptions>(
      configuration.GetSection(ConfigurationOptions.SectionName));

    // Add data protection for secret encryption
    services.AddDataProtection()
      .SetApplicationName("Radio.Configuration");

    // Register secret change notification source (signals IOptionsMonitor to re-evaluate)
    services.AddSingleton<SecretChangeTokenSource>();

    // Register both concrete secrets providers as singletons
    services.AddSingleton<SqliteSecretsProvider>();
    services.AddSingleton<JsonSecretsProvider>();

    // Register CompositeSecretsProvider as ISecretsProvider (SQLite primary + JSON fallback)
    services.AddSingleton<ISecretsProvider, CompositeSecretsProvider>();

    // Register store factory
    services.AddSingleton<IConfigurationStoreFactory, ConfigurationStoreFactory>();

    // Register backup services
    services.AddSingleton<IConfigurationBackupService, ConfigurationBackupService>();
    services.AddSingleton<IUnifiedDatabaseBackupService, UnifiedDatabaseBackupService>();

    // Register configuration manager
    services.AddSingleton<IRadioConfigurationManager, RadioConfigurationManager>();

    // Register preferences persistence service as a background service
    services.AddHostedService<PreferencesPersistenceService>();

    return services;
  }

  /// <summary>
  /// Adds secret resolution for the specified options type.
  /// </summary>
  /// <typeparam name="TOptions">The options type.</typeparam>
  /// <param name="services">The service collection.</param>
  /// <returns>The service collection for chaining.</returns>
  public static IServiceCollection AddSecretResolution<TOptions>(this IServiceCollection services)
    where TOptions : class
  {
    services.ConfigureOptions<SecretResolvingPostConfigureOptions<TOptions>>();

    // Register change token source so IOptionsMonitor invalidates when secrets change
    services.AddSingleton<IOptionsChangeTokenSource<TOptions>>(sp =>
      new SecretOptionsChangeTokenSource<TOptions>(sp.GetRequiredService<SecretChangeTokenSource>()));

    return services;
  }
}
