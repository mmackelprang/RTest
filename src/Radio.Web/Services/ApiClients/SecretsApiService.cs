using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Radio.Web.Services.ApiClients;

/// <summary>
/// API client for the dedicated secrets endpoints.
/// </summary>
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
  /// Gets secrets for a section, returning raw values.
  /// </summary>
  public async Task<T?> GetSecretsAsync<T>(string section, CancellationToken ct = default) where T : class, new()
  {
    try
    {
      var response = await _httpClient.GetAsync($"/api/secrets/{section}?raw=true", ct);
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
  /// Saves secrets for a section.
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
}
