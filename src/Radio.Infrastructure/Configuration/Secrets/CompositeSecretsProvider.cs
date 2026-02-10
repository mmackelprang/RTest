namespace Radio.Infrastructure.Configuration.Secrets;

using Microsoft.Extensions.Logging;
using Radio.Infrastructure.Configuration.Abstractions;
using Radio.Infrastructure.Configuration.Models;
using Radio.Infrastructure.Configuration.Options;

/// <summary>
/// Composite secrets provider that uses SQLite as primary storage
/// and falls back to JSON for reads. Writes always go to SQLite.
/// Secrets found in JSON are automatically migrated to SQLite.
/// </summary>
public sealed class CompositeSecretsProvider : ISecretsProvider, IAsyncDisposable
{
  private readonly SqliteSecretsProvider _sqliteProvider;
  private readonly JsonSecretsProvider _jsonProvider;
  private readonly ILogger<CompositeSecretsProvider> _logger;
  private readonly SecretChangeTokenSource? _changeTokenSource;

  public CompositeSecretsProvider(
    SqliteSecretsProvider sqliteProvider,
    JsonSecretsProvider jsonProvider,
    ILogger<CompositeSecretsProvider> logger,
    SecretChangeTokenSource? changeTokenSource = null)
  {
    ArgumentNullException.ThrowIfNull(sqliteProvider);
    ArgumentNullException.ThrowIfNull(jsonProvider);
    ArgumentNullException.ThrowIfNull(logger);

    _sqliteProvider = sqliteProvider;
    _jsonProvider = jsonProvider;
    _logger = logger;
    _changeTokenSource = changeTokenSource;
  }

  /// <inheritdoc/>
  public async Task<string?> GetSecretAsync(string tag, CancellationToken ct = default)
  {
    // Try SQLite first
    var secret = await _sqliteProvider.GetSecretAsync(tag, ct);
    if (secret != null)
      return secret;

    // Fall back to JSON
    secret = await _jsonProvider.GetSecretAsync(tag, ct);
    if (secret != null)
    {
      // Migrate to SQLite for future reads
      _logger.LogInformation("Migrating secret '{Tag}' from JSON to SQLite", tag);
      await _sqliteProvider.SetSecretAsync(tag, secret, ct);
    }

    return secret;
  }

  /// <inheritdoc/>
  public async Task<string> SetSecretAsync(string tag, string value, CancellationToken ct = default)
  {
    // Write only to SQLite
    var result = await _sqliteProvider.SetSecretAsync(tag, value, ct);

    // Signal change so IOptionsMonitor re-evaluates PostConfigure (secret tag resolution)
    _changeTokenSource?.SignalChange();

    return result;
  }

  /// <inheritdoc/>
  public async Task<bool> DeleteSecretAsync(string tag, CancellationToken ct = default)
  {
    // Delete from both providers
    var sqliteDeleted = await _sqliteProvider.DeleteSecretAsync(tag, ct);
    var jsonDeleted = await _jsonProvider.DeleteSecretAsync(tag, ct);

    if (sqliteDeleted || jsonDeleted)
      _changeTokenSource?.SignalChange();

    return sqliteDeleted || jsonDeleted;
  }

  /// <inheritdoc/>
  public async Task<IReadOnlyList<string>> ListTagsAsync(CancellationToken ct = default)
  {
    var sqliteTags = await _sqliteProvider.ListTagsAsync(ct);
    var jsonTags = await _jsonProvider.ListTagsAsync(ct);

    // Union, deduplicated
    var allTags = new HashSet<string>(sqliteTags);
    foreach (var tag in jsonTags)
      allTags.Add(tag);

    return allTags.ToList();
  }

  /// <inheritdoc/>
  public string GenerateTag(string? hint = null) => _sqliteProvider.GenerateTag(hint);

  /// <inheritdoc/>
  public bool ContainsSecretTag(string value) => SecretTag.ContainsTag(value);

  /// <inheritdoc/>
  public async Task<string> ResolveTagsAsync(string value, CancellationToken ct = default)
  {
    if (string.IsNullOrEmpty(value) || !ContainsSecretTag(value))
      return value;

    var result = value;
    foreach (var tag in SecretTag.ExtractAll(value))
    {
      var secret = await GetSecretAsync(tag.Identifier, ct);
      if (secret != null)
      {
        result = result.Replace(tag.Tag, secret);
      }
      else
      {
        _logger.LogWarning("Failed to resolve secret tag {Tag}", tag.Identifier);
      }
    }
    return result;
  }

  /// <inheritdoc/>
  public async ValueTask DisposeAsync()
  {
    await _sqliteProvider.DisposeAsync();
    _jsonProvider.Dispose();
  }
}
