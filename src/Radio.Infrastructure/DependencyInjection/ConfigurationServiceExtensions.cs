namespace Radio.Infrastructure.DependencyInjection;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Radio.Infrastructure.Configuration.Abstractions;
using Radio.Infrastructure.Configuration.Backup;
using Radio.Infrastructure.Configuration.Models;
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
  /// </summary>
  /// <param name="services">The service collection.</param>
  /// <param name="configuration">The configuration instance.</param>
  /// <param name="useSqliteSecrets">If true, uses SQLite secrets provider; otherwise uses JSON.</param>
  /// <returns>The service collection for chaining.</returns>
  public static IServiceCollection AddManagedConfiguration(
    this IServiceCollection services,
    IConfiguration configuration,
    bool useSqliteSecrets = false)
  {
    // Bind configuration options
    services.Configure<ConfigurationOptions>(
      configuration.GetSection(ConfigurationOptions.SectionName));

    // Add data protection for secret encryption
    services.AddDataProtection()
      .SetApplicationName("Radio.Configuration");

    // Register secrets provider based on parameter
    if (useSqliteSecrets)
      services.AddSingleton<ISecretsProvider, SqliteSecretsProvider>();
    else
      services.AddSingleton<ISecretsProvider, JsonSecretsProvider>();

    // Register store factory
    services.AddSingleton<IConfigurationStoreFactory, ConfigurationStoreFactory>();

    // Register backup service
    services.AddSingleton<IConfigurationBackupService, ConfigurationBackupService>();

    // Register configuration manager
    services.AddSingleton<IRadioConfigurationManager, RadioConfigurationManager>();

    return services;
  }

  /// <summary>
  /// Adds the managed configuration infrastructure with SQLite secrets provider.
  /// </summary>
  /// <param name="services">The service collection.</param>
  /// <param name="configuration">The configuration instance.</param>
  /// <returns>The service collection for chaining.</returns>
  public static IServiceCollection AddManagedConfigurationWithSqliteSecrets(
    this IServiceCollection services,
    IConfiguration configuration)
  {
    return services.AddManagedConfiguration(configuration, useSqliteSecrets: true);
  }
}
