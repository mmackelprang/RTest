namespace Radio.Infrastructure.Configuration.Abstractions;

/// <summary>
/// Provides secrets storage and resolution for tag-based substitution.
/// </summary>
public interface ISecretsProvider
{
  /// <summary>Retrieves the actual secret value for a given tag identifier.</summary>
  Task<string?> GetSecretAsync(string tag, CancellationToken ct = default);

  /// <summary>Stores a secret and returns its tag identifier.</summary>
  Task<string> SetSecretAsync(string tag, string value, CancellationToken ct = default);

  /// <summary>Generates a new unique tag identifier for a secret.</summary>
  string GenerateTag(string? hint = null);

  /// <summary>Deletes a secret by its tag identifier.</summary>
  Task<bool> DeleteSecretAsync(string tag, CancellationToken ct = default);

  /// <summary>Lists all secret tag identifiers (not values).</summary>
  Task<IReadOnlyList<string>> ListTagsAsync(CancellationToken ct = default);

  /// <summary>Checks if a value contains any secret tag patterns.</summary>
  bool ContainsSecretTag(string value);

  /// <summary>Resolves all secret tags in a value, replacing with actual secrets.</summary>
  Task<string> ResolveTagsAsync(string value, CancellationToken ct = default);
}
