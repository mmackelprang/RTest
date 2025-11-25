namespace Radio.Infrastructure.Configuration.Secrets;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using Radio.Infrastructure.Configuration.Abstractions;
using Radio.Infrastructure.Configuration.Models;

/// <summary>
/// Base class for secrets providers with common encryption and tag resolution logic.
/// </summary>
public abstract class SecretsProviderBase : ISecretsProvider
{
  /// <summary>Length of the GUID substring used for tag generation.</summary>
  protected const int TagIdLength = 12;

  private readonly IDataProtector _protector;
  private readonly ILogger _logger;

  /// <summary>
  /// Initializes a new instance of the SecretsProviderBase class.
  /// </summary>
  protected SecretsProviderBase(IDataProtectionProvider dataProtection, ILogger logger)
  {
    ArgumentNullException.ThrowIfNull(dataProtection);
    ArgumentNullException.ThrowIfNull(logger);

    _protector = dataProtection.CreateProtector("Radio.Configuration.Secrets");
    _logger = logger;
  }

  /// <inheritdoc/>
  public abstract Task<string?> GetSecretAsync(string tag, CancellationToken ct = default);

  /// <inheritdoc/>
  public abstract Task<string> SetSecretAsync(string tag, string value, CancellationToken ct = default);

  /// <inheritdoc/>
  public abstract Task<bool> DeleteSecretAsync(string tag, CancellationToken ct = default);

  /// <inheritdoc/>
  public abstract Task<IReadOnlyList<string>> ListTagsAsync(CancellationToken ct = default);

  /// <inheritdoc/>
  public string GenerateTag(string? hint = null)
  {
    var id = Guid.NewGuid().ToString("N")[..TagIdLength];
    if (!string.IsNullOrWhiteSpace(hint))
    {
      // Sanitize hint for use in identifier
      var sanitized = new string(hint
        .Replace(":", "_")
        .Replace(" ", "_")
        .Where(c => char.IsLetterOrDigit(c) || c == '_' || c == '-')
        .Take(20)
        .ToArray());
      if (!string.IsNullOrEmpty(sanitized))
      {
        return $"{sanitized}_{id}";
      }
    }
    return id;
  }

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
    }
    return result;
  }

  /// <summary>
  /// Encrypts plain text using Data Protection.
  /// </summary>
  protected string Encrypt(string plainText)
  {
    return _protector.Protect(plainText);
  }

  /// <summary>
  /// Decrypts cipher text using Data Protection.
  /// </summary>
  protected string Decrypt(string cipherText)
  {
    try
    {
      return _protector.Unprotect(cipherText);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to decrypt secret");
      throw new InvalidOperationException("Failed to decrypt secret", ex);
    }
  }
}
