namespace Radio.Configuration;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Radio.Configuration.Abstractions;
using Radio.Configuration.Backup;
using Radio.Configuration.Models;
using Radio.Configuration.Options;
using Radio.Configuration.Secrets;
using Radio.Configuration.Services;
using Radio.Configuration.Stores;

using IRadioConfigurationManager = Radio.Configuration.Abstractions.IConfigurationManager;
using RadioConfigurationManager = Radio.Configuration.Services.ConfigurationManager;

/// <summary>
/// Extension methods for registering managed configuration services.
/// </summary>
public static class ConfigurationServiceExtensions
{
  /// <summary>
  /// Adds the managed configuration infrastructure to the service collection.
  /// Registers a composite secrets provider (SQLite primary + JSON fallback),
  /// store factory, backup service, and configuration manager.
  /// </summary>
  /// <param name="services">The service collection.</param>
  /// <param name="configuration">The configuration instance.</param>
  /// <returns>The service collection for chaining.</returns>
  public static IServiceCollection AddManagedConfiguration(
    this IServiceCollection services,
    IConfiguration configuration)
  {
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

    // Register config-level backup service
    services.AddSingleton<IConfigurationBackupService, ConfigurationBackupService>();

    // Register configuration manager
    services.AddSingleton<IRadioConfigurationManager, RadioConfigurationManager>();

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
