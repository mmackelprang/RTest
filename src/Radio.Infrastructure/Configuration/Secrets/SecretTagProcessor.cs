namespace Radio.Infrastructure.Configuration.Secrets;

using Microsoft.Extensions.Logging;
using Radio.Infrastructure.Configuration.Abstractions;
using Radio.Infrastructure.Configuration.Models;

/// <summary>
/// Processes secret tags in configuration values, handling detection,
/// extraction, and resolution of secrets.
/// </summary>
public sealed class SecretTagProcessor
{
  private readonly ISecretsProvider _provider;
  private readonly ILogger<SecretTagProcessor> _logger;

  /// <summary>
  /// Initializes a new instance of the SecretTagProcessor class.
  /// </summary>
  public SecretTagProcessor(ISecretsProvider provider, ILogger<SecretTagProcessor> logger)
  {
    _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    _logger = logger ?? throw new ArgumentNullException(nameof(logger));
  }

  /// <summary>Checks if value contains any secret tags.</summary>
  public bool ContainsTags(string? value) => SecretTag.ContainsTag(value);

  /// <summary>Extracts all tag identifiers from a value.</summary>
  public IReadOnlyList<string> ExtractTagIdentifiers(string value)
  {
    return SecretTag.ExtractAll(value)
      .Select(t => t.Identifier)
      .ToList();
  }

  /// <summary>Resolves all tags in value, replacing with actual secrets.</summary>
  public async Task<string> ResolveAsync(string? value, CancellationToken ct = default)
  {
    if (string.IsNullOrEmpty(value) || !ContainsTags(value))
      return value ?? string.Empty;

    var result = value;
    var tags = SecretTag.ExtractAll(value).ToList();

    foreach (var tag in tags)
    {
      var secret = await _provider.GetSecretAsync(tag.Identifier, ct);
      if (secret != null)
      {
        result = result.Replace(tag.Tag, secret);
      }
      else
      {
        _logger.LogWarning("Secret not found for tag: {TagIdentifier}", tag.Identifier);
        // Preserve original tag if secret not found
      }
    }

    return result;
  }

  /// <summary>Creates a new secret and returns the tag to use in config.</summary>
  public async Task<SecretTag> CreateSecretAsync(string value, string? hint = null, CancellationToken ct = default)
  {
    var identifier = _provider.GenerateTag(hint);
    await _provider.SetSecretAsync(identifier, value, ct);
    return SecretTag.Create(identifier);
  }

  /// <summary>Wraps a value in a secret tag (for converting plain text to secret).</summary>
  public async Task<string> SecretifyAsync(string plainValue, string? hint = null, CancellationToken ct = default)
  {
    var tag = await CreateSecretAsync(plainValue, hint, ct);
    return tag.Tag;
  }
}
