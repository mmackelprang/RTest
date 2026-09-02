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
///   <item>a value shaped like a mask is treated as unchanged, and not written.
///         <see cref="LooksLikeMask"/> is the authority on that rule and on why it tests shape
///         rather than comparing against what is stored; this list does not restate it.</item>
///   <item>an absent, blank or whitespace-only property is treated as unchanged, and not written;</item>
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
  /// <summary>Mask shown for a secret short enough that partial disclosure would reveal much of it.</summary>
  private const string FullMask = "********";

  /// <summary>Longest secret rendered as <see cref="FullMask"/> rather than partially.</summary>
  private const int MaskShortValueLength = 8;

  /// <summary>Clear-text characters kept at each end of a partially masked secret.</summary>
  private const int MaskEdgeLength = 4;

  /// <summary>Separator between the two clear-text runs of a partially masked secret.</summary>
  private const string MaskSeparator = "...";

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
  /// blank, or that carry a masked value, are left untouched; see the class remarks for the full
  /// write contract.
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

      if (string.IsNullOrWhiteSpace(value))
      {
        // Blank means "keep what is stored". Deleting is an explicit request against one of the
        // DELETE routes, so that a client cannot erase a secret merely by submitting a form.
        //
        // Whitespace counts as blank deliberately. No secret here is a run of spaces, and a client
        // that does not trim - Swagger, curl, anything not the Blazor page - would otherwise fall
        // through to the write below and replace a live credential with " ": the same silent,
        // unrecoverable overwrite this controller exists to prevent, through a different door.
        _logger.LogInformation(
          "Secret '{Tag}' in section '{Section}' was submitted blank; left unchanged", tag, section);
        unchangedCount++;
        continue;
      }

      if (LooksLikeMask(value))
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
  /// Determines whether a submitted value has the shape of a <see cref="MaskValue"/> result, and
  /// is therefore a display form echoed back rather than a secret to store.
  /// </summary>
  /// <remarks>
  /// The test deliberately looks at the submitted string alone rather than comparing it against
  /// the secret currently stored. A comparison would only recognise a mask built from the value
  /// stored *now*, so a form loaded before the secret changed elsewhere would post a mask that no
  /// longer matches — and that stale mask would be written over the newer secret, which is the
  /// same unrecoverable overwrite this guard exists to prevent.
  ///
  /// The cost is that a genuine secret shaped like a mask cannot be saved through this endpoint.
  /// Declining to store is the safe direction, and the response reports it in
  /// <c>unchangedCount</c>.
  /// </remarks>
  private static bool LooksLikeMask(string value) =>
    value == FullMask ||
    (value.Length == (MaskEdgeLength * 2) + MaskSeparator.Length &&
     value.AsSpan(MaskEdgeLength, MaskSeparator.Length).SequenceEqual(MaskSeparator));

  /// <summary>
  /// Renders a secret for display. Every non-empty result satisfies <see cref="LooksLikeMask"/>,
  /// so a value this method produced is never stored back as a secret.
  /// </summary>
  private static string MaskValue(string? value)
  {
    if (string.IsNullOrEmpty(value))
    {
      return "";
    }

    if (value.Length <= MaskShortValueLength)
    {
      return FullMask;
    }

    return $"{value[..MaskEdgeLength]}{MaskSeparator}{value[^MaskEdgeLength..]}";
  }
}
