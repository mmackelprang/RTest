using System.Net.Http.Json;
using Microsoft.Extensions.Logging;

namespace Radio.Web.Services.ApiClients;

/// <summary>
/// API client service for configuration management endpoints (5 endpoints)
/// </summary>
public class ConfigurationApiService
{
  private readonly HttpClient _httpClient;
  private readonly ILogger<ConfigurationApiService> _logger;

  public ConfigurationApiService(HttpClient httpClient, ILogger<ConfigurationApiService> logger)
  {
    _httpClient = httpClient;
    _logger = logger;
  }

  public async Task<Dictionary<string, object>?> GetAllConfigurationAsync(CancellationToken cancellationToken = default)
  {
    try
    {
      return await _httpClient.GetFromJsonAsync<Dictionary<string, object>>("/api/configuration", cancellationToken);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to get all configuration");
      return null;
    }
  }

  public async Task<T?> GetConfigurationAsync<T>(string section, CancellationToken cancellationToken = default)
  {
    try
    {
      return await _httpClient.GetFromJsonAsync<T>($"/api/configuration/{section}", cancellationToken);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to get configuration section {Section}", section);
      return default;
    }
  }

  public async Task<bool> UpdateConfigurationAsync<T>(string section, T value, CancellationToken cancellationToken = default)
  {
    try
    {
      var response = await _httpClient.PostAsJsonAsync($"/api/configuration/{section}", value, cancellationToken);
      return response.IsSuccessStatusCode;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to update configuration section {Section}", section);
      return false;
    }
  }

  public async Task<bool> ResetConfigurationAsync(string section, CancellationToken cancellationToken = default)
  {
    try
    {
      var response = await _httpClient.DeleteAsync($"/api/configuration/{section}", cancellationToken);
      return response.IsSuccessStatusCode;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to reset configuration section {Section}", section);
      return false;
    }
  }

  public async Task<bool> ReloadConfigurationAsync(CancellationToken cancellationToken = default)
  {
    try
    {
      var response = await _httpClient.PostAsync("/api/configuration/reload", null, cancellationToken);
      return response.IsSuccessStatusCode;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to reload configuration");
      return false;
    }
  }
}
