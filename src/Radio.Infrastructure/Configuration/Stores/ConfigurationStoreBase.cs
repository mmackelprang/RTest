namespace Radio.Infrastructure.Configuration.Stores;

using Microsoft.Extensions.Logging;
using Radio.Infrastructure.Configuration.Abstractions;
using Radio.Infrastructure.Configuration.Models;

/// <summary>
/// Base class for configuration stores with common entry resolution logic.
/// </summary>
public abstract class ConfigurationStoreBase : IConfigurationStore
{
  /// <summary>The secrets provider for resolving secret tags.</summary>
  protected readonly ISecretsProvider SecretsProvider;

  /// <summary>The logger instance.</summary>
  protected readonly ILogger Logger;

  /// <inheritdoc/>
  public abstract string StoreId { get; }

  /// <inheritdoc/>
  public abstract ConfigurationStoreType StoreType { get; }

  /// <summary>
  /// Initializes a new instance of the ConfigurationStoreBase class.
  /// </summary>
  protected ConfigurationStoreBase(ISecretsProvider secretsProvider, ILogger logger)
  {
    SecretsProvider = secretsProvider ?? throw new ArgumentNullException(nameof(secretsProvider));
    Logger = logger ?? throw new ArgumentNullException(nameof(logger));
  }

  /// <inheritdoc/>
  public abstract Task<ConfigurationEntry?> GetEntryAsync(string key, ConfigurationReadMode mode = ConfigurationReadMode.Resolved, CancellationToken ct = default);

  /// <inheritdoc/>
  public abstract Task<IReadOnlyList<ConfigurationEntry>> GetAllEntriesAsync(ConfigurationReadMode mode = ConfigurationReadMode.Resolved, CancellationToken ct = default);

  /// <inheritdoc/>
  public abstract Task<IReadOnlyList<ConfigurationEntry>> GetEntriesBySectionAsync(string sectionPrefix, ConfigurationReadMode mode = ConfigurationReadMode.Resolved, CancellationToken ct = default);

  /// <inheritdoc/>
  public abstract Task SetEntryAsync(string key, string value, CancellationToken ct = default);

  /// <inheritdoc/>
  public abstract Task SetEntriesAsync(IEnumerable<ConfigurationEntry> entries, CancellationToken ct = default);

  /// <inheritdoc/>
  public abstract Task<bool> DeleteEntryAsync(string key, CancellationToken ct = default);

  /// <inheritdoc/>
  public abstract Task<bool> ExistsAsync(string key, CancellationToken ct = default);

  /// <inheritdoc/>
  public abstract Task<bool> SaveAsync(CancellationToken ct = default);

  /// <inheritdoc/>
  public abstract Task ReloadAsync(CancellationToken ct = default);

  /// <summary>
  /// Creates a ConfigurationEntry from raw data, resolving secrets if needed.
  /// </summary>
  protected async Task<ConfigurationEntry> CreateEntryAsync(
    string key,
    string rawValue,
    string? description,
    DateTimeOffset? lastModified,
    ConfigurationReadMode mode,
    CancellationToken ct)
  {
    var containsSecret = SecretsProvider.ContainsSecretTag(rawValue);

    string resolvedValue;
    if (mode == ConfigurationReadMode.Resolved && containsSecret)
    {
      resolvedValue = await SecretsProvider.ResolveTagsAsync(rawValue, ct);
    }
    else
    {
      resolvedValue = rawValue;
    }

    return new ConfigurationEntry
    {
      Key = key,
      Value = resolvedValue,
      RawValue = containsSecret ? rawValue : null,
      ContainsSecret = containsSecret,
      LastModified = lastModified,
      Description = description
    };
  }

  /// <summary>
  /// Normalizes a section prefix to ensure it ends with a colon.
  /// </summary>
  protected static string NormalizeSectionPrefix(string sectionPrefix)
  {
    return sectionPrefix.EndsWith(':') ? sectionPrefix : $"{sectionPrefix}:";
  }
}
