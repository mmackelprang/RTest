using Microsoft.AspNetCore.Mvc;
using Radio.Configuration.Abstractions;

namespace Radio.API.Controllers;

/// <summary>
/// Dedicated API controller for secret CRUD, operating directly on ISecretsProvider.
/// </summary>
/// <remarks>
/// <para>
/// The read path never returns plaintext: <see cref="GetSectionSecrets"/> masks every value, and
/// this controller has no raw mode. That is what makes the write path the dangerous one - the only
/// value a client can echo back from a read is the mask itself, so a form that round-trips what it
/// loaded would overwrite each real secret with its own placeholder.
/// </para>
/// <para>
/// The write contract is therefore "send only what changed":
/// <list type="bullet">
///   <item>a value equal to the mask of the value already stored is treated as unchanged, and not written;</item>
///   <item>an absent or blank property is treated as unchanged, and not written;</item>
///   <item>deletion is explicit - <see cref="DeleteSectionSecret"/> for one property,
///         <see cref="DeleteSectionSecrets"/> for a whole section.</item>
/// </list>
/// </para>
/// <para>
/// A blank value used to mean "delete this secret". It no longer does. The Secrets UI presents a
/// configured secret as an empty field with a hint rather than as editable text, so under the old
/// rule an ordinary Save would have deleted every secret the user did not retype.
/// </para>
/// </remarks>
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
  /// Gets all secrets for a section, with values always masked. There is no raw mode: an unset
  /// secret comes back as an empty string, and a set one as <see cref="MaskValue"/> of its value.
  /// </summary>
  /// <param name="section">The section name. "tts" is currently the only one defined.</param>
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
  /// Stores secrets for a section. Each property is mapped to a well-known tag. Properties that are
  /// blank, or that carry the mask of the secret already stored, are left untouched; see the class
  /// remarks for the full write contract.
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
        // Blank means "keep what is stored". Deleting is an explicit request against one of the
        // DELETE routes, so that a client cannot erase a secret merely by submitting a form.
        _logger.LogInformation(
          "Secret '{Tag}' in section '{Section}' was submitted blank; left unchanged", tag, section);
        unchangedCount++;
        continue;
      }

      var stored = await _secrets.GetSecretAsync(tag);

      // The only value a client can echo back from GET is the mask, so a submitted value that is
      // byte-identical to the mask of what is already stored is a round-trip, not an edit. The
      // comparison is against MaskValue(stored) specifically - never a loose pattern such as
      // "contains an ellipsis" - so a genuine secret that happens to contain "..." or a run of
      // asterisks is still stored normally.
      if (!string.IsNullOrEmpty(stored) && string.Equals(value, MaskValue(stored), StringComparison.Ordinal))
      {
        _logger.LogInformation(
          "Secret '{Tag}' in section '{Section}' was submitted as its own mask; left unchanged", tag, section);
        unchangedCount++;
        continue;
      }

      await _secrets.SetSecretAsync(tag, value);
      storedCount++;
    }

    _logger.LogInformation(
      "Section '{Section}': stored {StoredCount} secret(s), left {UnchangedCount} unchanged",
      section, storedCount, unchangedCount);

    return Ok(new
    {
      message = storedCount > 0
        ? $"Stored {storedCount} secret(s) for section '{section}'"
        : "No secret was changed",
      section,
      storedCount,
      unchangedCount
    });
  }

  /// <summary>
  /// Deletes one secret from a section. This is the only way to clear a single property, since a
  /// blank value on the POST route means "unchanged".
  /// </summary>
  /// <param name="section">The section name.</param>
  /// <param name="property">The section property name (e.g., "AzureAPIKey").</param>
  [HttpDelete("{section}/{property}")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  public async Task<ActionResult> DeleteSectionSecret(string section, string property)
  {
    if (!SectionTagMappings.TryGetValue(section, out var tagMap))
    {
      return BadRequest(new { error = $"Unknown secrets section '{section}'" });
    }

    if (!tagMap.TryGetValue(property, out var tag))
    {
      return BadRequest(new { error = $"Unknown property '{property}' for section '{section}'" });
    }

    var deleted = await _secrets.DeleteSecretAsync(tag);

    _logger.LogInformation(
      "Section '{Section}': delete of secret '{Tag}' {Outcome}",
      section, tag, deleted ? "removed the stored value" : "found nothing stored");

    return Ok(new
    {
      message = deleted ? "Secret cleared" : "No secret was stored for that property",
      section,
      property,
      deleted
    });
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
  /// Produces the masked form of a secret. Both the read path and the round-trip guard in
  /// <see cref="SetSectionSecrets"/> call this one method deliberately: the guard only works while
  /// it compares against exactly the string a client could have received from
  /// <see cref="GetSectionSecrets"/>, so the two must not drift into separate implementations.
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
