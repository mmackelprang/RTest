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

    // Add data protection for secret encryption.
    //
    // CRITICAL: persist the key ring to an EXPLICIT, deploy-safe path. Without
    // this call ASP.NET Core stores keys under the ambient "$HOME/.aspnet/
    // DataProtection-Keys". That location silently moves whenever the process's
    // effective HOME changes — which is exactly what happened on 2026-02-13 when
    // the systemd unit added `Environment=HOME=/opt/radio-console` (the
    // dual-service split): the key that had encrypted the 2026-02-12 secrets was
    // written to the pre-change location and orphaned, so every stored secret
    // became undecryptable ("key {…} was not found in the key ring"). See
    // design/plans/SECRET-KEYRING-INVESTIGATION.md.
    //
    // Resolution order for the key-ring directory:
    //   1. DataProtection:KeysPath (explicit override), else
    //   2. <Database:RootPath>/keys (co-located with secrets.db / config.db), else
    //   3. ./data/keys
    // The chosen path lives under the persistent data root, which the deploy
    // preserves (Deploy-ToLinux.ps1's `rsync --delete` only wipes api/ and web/),
    // and is derived independently of HOME so a future user/HOME change cannot
    // move it again. SetApplicationName pins the purpose-isolation discriminator.
    var keysPath = configuration["DataProtection:KeysPath"];
    if (string.IsNullOrWhiteSpace(keysPath))
    {
      var dataRoot = configuration["Database:RootPath"];
      if (string.IsNullOrWhiteSpace(dataRoot))
      {
        dataRoot = "./data";
      }
      keysPath = Path.Combine(dataRoot, "keys");
    }
    keysPath = Path.GetFullPath(keysPath);
    Directory.CreateDirectory(keysPath);

    services.AddDataProtection()
      .SetApplicationName("Radio.Configuration")
      .PersistKeysToFileSystem(new DirectoryInfo(keysPath));

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
