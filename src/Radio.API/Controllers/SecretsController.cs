using Microsoft.AspNetCore.Mvc;
using Radio.Configuration.Abstractions;

namespace Radio.API.Controllers;

/// <summary>
/// Dedicated API controller for secret CRUD, operating directly on ISecretsProvider.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class SecretsController : ControllerBase
{
  private readonly ISecretsProvider _secrets;
  private readonly ILogger<SecretsController> _logger;

  // Well-known tag mappings per section (case-insensitive keys to handle camelCase from JSON serialization)
  private static readonly Dictionary<string, Dictionary<string, string>> SectionTagMappings = new(StringComparer.OrdinalIgnoreCase)
  {
    ["tts"] = new(StringComparer.OrdinalIgnoreCase)
    {
      ["GoogleAPIKey"] = "tts_google_api_key",
      ["AzureAPIKey"] = "tts_azure_api_key",
      ["AzureRegion"] = "tts_azure_region"
    }
  };

  public SecretsController(ISecretsProvider secrets, ILogger<SecretsController> logger)
  {
    _secrets = secrets;
    _logger = logger;
  }

  /// <summary>
  /// Gets all secrets for a section, with values always masked.
  /// </summary>
  /// <param name="section">The section name (e.g., "tts", "acoustid").</param>
  [HttpGet("{section}")]
  [ProducesResponseType(typeof(Dictionary<string, string>), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  public async Task<ActionResult<Dictionary<string, string>>> GetSectionSecrets(
    string section)
  {
    if (!SectionTagMappings.TryGetValue(section, out var tagMap))
    {
      return BadRequest(new { error = $"Unknown secrets section '{section}'" });
    }

    var result = new Dictionary<string, string>();
    foreach (var (property, tag) in tagMap)
    {
      var value = await _secrets.GetSecretAsync(tag);
      result[property] = MaskValue(value);
    }

    return Ok(result);
  }

  /// <summary>
  /// Stores secrets for a section. Each property is mapped to a well-known tag.
  /// </summary>
  /// <param name="section">The section name.</param>
  /// <param name="data">Key-value pairs matching section property names.</param>
  [HttpPost("{section}")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  public async Task<ActionResult> SetSectionSecrets(
    string section, [FromBody] Dictionary<string, string> data)
  {
    if (!SectionTagMappings.TryGetValue(section, out var tagMap))
    {
      return BadRequest(new { error = $"Unknown secrets section '{section}'" });
    }

    if (data == null || data.Count == 0)
    {
      return BadRequest(new { error = "No secret data provided" });
    }

    var storedCount = 0;
    var unchangedCount = 0;
    foreach (var (property, value) in data)
    {
      if (!tagMap.TryGetValue(property, out var tag))
      {
        _logger.LogWarning("Unknown property '{Property}' for section '{Section}', skipping", property, section);
        continue;
      }

      if (string.IsNullOrEmpty(value))
      {
        // Empty value means delete
        await _secrets.DeleteSecretAsync(tag);
        _logger.LogInformation("Secret '{Tag}' cleared for section '{Section}'", tag, section);
      }
      else if (await IsMaskOfStoredSecretAsync(tag, value))
      {
        // GetSectionSecrets returns masked values and the config UI binds them straight into
        // its inputs, so a field the user did not edit posts its own mask back. Storing that
        // would replace the real secret with its display form — an unrecoverable overwrite,
        // because the plaintext is not kept anywhere else.
        unchangedCount++;
        _logger.LogInformation(
          "Secret '{Tag}' for section '{Section}' was posted unchanged (masked); leaving the stored value intact",
          tag,
          section);
      }
      else
      {
        await _secrets.SetSecretAsync(tag, value);
        storedCount++;
      }
    }

    _logger.LogInformation(
      "Section '{Section}': stored {Count} secrets, left {Unchanged} unchanged",
      section,
      storedCount,
      unchangedCount);
    return Ok(new { message = "Secrets saved successfully", section, storedCount, unchangedCount });
  }

  /// <summary>
  /// Deletes all secrets for a section.
  /// </summary>
  /// <param name="section">The section name.</param>
  [HttpDelete("{section}")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  public async Task<ActionResult> DeleteSectionSecrets(string section)
  {
    if (!SectionTagMappings.TryGetValue(section, out var tagMap))
    {
      return BadRequest(new { error = $"Unknown secrets section '{section}'" });
    }

    var deletedCount = 0;
    foreach (var (_, tag) in tagMap)
    {
      if (await _secrets.DeleteSecretAsync(tag))
      {
        deletedCount++;
      }
    }

    _logger.LogInformation("Section '{Section}': deleted {Count} secrets", section, deletedCount);
    return Ok(new { message = "Secrets cleared successfully", section, deletedCount });
  }

  /// <summary>
  /// Lists all stored secret tags (diagnostic endpoint).
  /// </summary>
  [HttpGet("tags")]
  [ProducesResponseType(typeof(IReadOnlyList<string>), StatusCodes.Status200OK)]
  public async Task<ActionResult<IReadOnlyList<string>>> ListTags()
  {
    var tags = await _secrets.ListTagsAsync();
    return Ok(tags);
  }

  /// <summary>
  /// Determines whether a submitted value is just the mask of the secret already stored under
  /// <paramref name="tag"/>, i.e. the caller echoed back what <see cref="GetSectionSecrets"/> showed.
  /// </summary>
  private async Task<bool> IsMaskOfStoredSecretAsync(string tag, string submitted)
  {
    var existing = await _secrets.GetSecretAsync(tag);
    return existing != null && submitted == MaskValue(existing);
  }

  /// <summary>
  /// Renders a secret for display. A submitted value is rejected as an echo only when it equals
  /// the mask of the secret currently stored under the same tag — see
  /// <see cref="IsMaskOfStoredSecretAsync"/>. A mask-shaped string submitted for a tag that holds
  /// nothing is stored like any other value.
  /// </summary>
  private static string MaskValue(string? value)
  {
    if (string.IsNullOrEmpty(value))
    {
      return "";
    }

    if (value.Length <= 8)
    {
      return "********";
    }

    return $"{value[..4]}...{value[^4..]}";
  }
}
