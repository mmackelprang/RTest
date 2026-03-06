namespace Radio.Infrastructure.Configuration.Bridge;

/// <summary>
/// Notification bridge between ConfigurationManager writes and the
/// SqliteConfigurationProvider. When a config value is written via the UI,
/// ConfigurationManager calls <see cref="NotifyReload"/> which triggers the
/// provider to re-read from SQLite and fire IOptionsMonitor change tokens.
/// </summary>
public sealed class ConfigStoreChangeNotifier
{
  private SqliteConfigurationProvider? _provider;

  /// <summary>
  /// Registers the provider that should be reloaded on config changes.
  /// Called by <see cref="SqliteConfigurationSource.Build"/>.
  /// </summary>
  internal void SetProvider(SqliteConfigurationProvider provider)
  {
    _provider = provider;
  }

  /// <summary>
  /// Triggers the provider to reload from SQLite and fire change tokens.
  /// Safe to call when no provider is registered (no-op).
  /// </summary>
  public void NotifyReload()
  {
    _provider?.Reload();
  }
}
