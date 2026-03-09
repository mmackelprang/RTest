namespace Radio.Configuration.Bridge;

using Microsoft.Extensions.Configuration;

/// <summary>
/// Configuration source that creates a <see cref="SqliteConfigurationProvider"/>
/// and registers it with the <see cref="ConfigStoreChangeNotifier"/> so that
/// runtime config writes trigger IOptionsMonitor re-evaluation.
/// </summary>
public sealed class SqliteConfigurationSource : IConfigurationSource
{
  private readonly string _connectionString;
  private readonly string _tableName;
  private readonly ConfigStoreChangeNotifier? _notifier;

  public SqliteConfigurationSource(string connectionString, string tableName, ConfigStoreChangeNotifier? notifier)
  {
    _connectionString = connectionString;
    _tableName = tableName;
    _notifier = notifier;
  }

  /// <inheritdoc/>
  public IConfigurationProvider Build(IConfigurationBuilder builder)
  {
    var provider = new SqliteConfigurationProvider(_connectionString, _tableName);
    _notifier?.SetProvider(provider);
    return provider;
  }
}
