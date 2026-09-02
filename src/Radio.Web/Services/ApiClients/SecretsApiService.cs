using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Radio.Web.Services.ApiClients;

/// <summary>
/// API client for the dedicated secrets endpoints.
/// </summary>
/// <remarks>
/// Every value this client reads back is masked - the API has no plaintext read - so nothing it
/// receives is safe to submit again. <see cref="OnlyProvided"/> is the client half of that
/// contract: callers build a payload from what the user actually typed, and omit the rest.
/// </remarks>
public class SecretsApiService
{
  private readonly HttpClient _httpClient;
  private readonly ILogger<SecretsApiService> _logger;
  private static readonly JsonSerializerOptions JsonOptions = new()
  {
    PropertyNameCaseInsensitive = true,
    NumberHandling = JsonNumberHandling.AllowReadingFromString
  };

  public SecretsApiService(HttpClient httpClient, ILogger<SecretsApiService> logger)
  {
    _httpClient = httpClient;
    _logger = logger;
  }

  /// <summary>
  /// Gets the status of a section's secrets. Every value is masked by the API: an empty string
  /// means "not set", anything else is a placeholder such as <c>abcd...wxyz</c> or <c>********</c>
  /// and is never the secret itself. Display it as a hint; never put it in an editable field, and
  /// never send it back.
  /// </summary>
  public async Task<T?> GetSecretsAsync<T>(string section, CancellationToken ct = default) where T : class, new()
  {
    try
    {
      var response = await _httpClient.GetAsync($"/api/secrets/{section}", ct);
      if (!response.IsSuccessStatusCode)
      {
        _logger.LogWarning("Failed to get secrets for section {Section}: {Status}", section, response.StatusCode);
        return new T();
      }

      var result = await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct);
      return result ?? new T();
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error fetching secrets for section {Section}", section);
      return new T();
    }
  }

  /// <summary>
  /// Saves secrets for a section. Only the properties present and non-blank in
  /// <paramref name="secrets"/> are written; the API leaves every other property of the section
  /// alone. Build the payload with <see cref="OnlyProvided"/>.
  /// </summary>
  public async Task<bool> SaveSecretsAsync<T>(string section, T secrets, CancellationToken ct = default) where T : class
  {
    try
    {
      var response = await _httpClient.PostAsJsonAsync($"/api/secrets/{section}", secrets, ct);
      return response.IsSuccessStatusCode;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error saving secrets for section {Section}", section);
      return false;
    }
  }

  /// <summary>
  /// Clears all secrets for a section.
  /// </summary>
  public async Task<bool> ClearSecretsAsync(string section, CancellationToken ct = default)
  {
    try
    {
      var response = await _httpClient.DeleteAsync($"/api/secrets/{section}", ct);
      return response.IsSuccessStatusCode;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error clearing secrets for section {Section}", section);
      return false;
    }
  }

  /// <summary>
  /// Clears one secret in a section. Clearing is a distinct request because submitting a blank
  /// value through <see cref="SaveSecretsAsync"/> means "leave it as it is".
  /// </summary>
  public async Task<bool> ClearSecretAsync(string section, string property, CancellationToken ct = default)
  {
    try
    {
      var response = await _httpClient.DeleteAsync($"/api/secrets/{section}/{property}", ct);
      return response.IsSuccessStatusCode;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error clearing secret {Property} in section {Section}", property, section);
      return false;
    }
  }

  /// <summary>
  /// Builds a save payload from the values a user actually entered, dropping every field left
  /// blank so that an untouched field is never transmitted at all.
  /// </summary>
  /// <remarks>
  /// This is the first of two defences against the mask round-trip, and the weaker one: it stops
  /// the UI from sending a placeholder, while the API's own guard stops any client from storing
  /// one. Whitespace-only input is treated as blank, since it cannot be a deliberate secret and is
  /// far more likely to be a stray keystroke.
  /// </remarks>
  public static Dictionary<string, string> OnlyProvided(IReadOnlyDictionary<string, string?> fields)
  {
    var payload = new Dictionary<string, string>();
    foreach (var (property, value) in fields)
    {
      if (!string.IsNullOrWhiteSpace(value))
      {
        payload[property] = value;
      }
    }

    return payload;
  }
}
