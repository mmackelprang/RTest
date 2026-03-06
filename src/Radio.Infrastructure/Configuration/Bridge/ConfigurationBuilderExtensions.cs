namespace Radio.Infrastructure.Configuration.Bridge;

using Microsoft.Extensions.Configuration;

/// <summary>
/// Extension methods for adding the SQLite configuration bridge to the
/// .NET configuration pipeline.
/// </summary>
public static class ConfigurationBuilderExtensions
{
  /// <summary>
  /// Adds the SQLite configuration store as a configuration source.
  /// Values from the SQLite store override earlier sources (e.g., appsettings.json)
  /// because they are added later in the configuration chain.
  /// </summary>
  /// <param name="builder">The configuration builder.</param>
  /// <param name="dbPath">Full path to the SQLite database file.</param>
  /// <param name="storeId">The store identifier (used to derive the table name).</param>
  /// <param name="notifier">
  /// Optional notifier that will be wired to the provider so that
  /// <see cref="ConfigStoreChangeNotifier.NotifyReload"/> triggers
  /// IOptionsMonitor change tokens.
  /// </param>
  /// <returns>The configuration builder for chaining.</returns>
  public static IConfigurationBuilder AddSqliteConfigStore(
    this IConfigurationBuilder builder,
    string dbPath,
    string storeId,
    ConfigStoreChangeNotifier? notifier = null)
  {
    var connectionString = $"Data Source={dbPath}";

    // Sanitize table name the same way SqliteConfigurationStore does
    var sanitized = new string(storeId
      .Replace("-", "_")
      .Replace(".", "_")
      .Where(c => char.IsLetterOrDigit(c) || c == '_')
      .ToArray());
    var tableName = $"Config_{sanitized}";

    builder.Add(new SqliteConfigurationSource(connectionString, tableName, notifier));
    return builder;
  }
}
