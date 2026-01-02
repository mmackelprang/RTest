using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Radio.Web.Models;

namespace Radio.Web.Services.ApiClients;

/// <summary>
/// API client service for configuration management endpoints (5 endpoints)
/// </summary>
public class ConfigurationApiService
{
  private readonly HttpClient _httpClient;
  private readonly ILogger<ConfigurationApiService> _logger;
  private static readonly JsonSerializerOptions JsonOptions = new()
  {
    PropertyNameCaseInsensitive = true
  };

  public ConfigurationApiService(HttpClient httpClient, ILogger<ConfigurationApiService> logger)
  {
    _httpClient = httpClient;
    _logger = logger;
  }

  public async Task<Dictionary<string, object>?> GetAllConfigurationAsync(CancellationToken cancellationToken = default)
  {
    try
    {
      return await _httpClient.GetFromJsonAsync<Dictionary<string, object>>("/api/configuration", JsonOptions, cancellationToken);
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
      _logger.LogDebug("Fetching configuration section: {Section}", section);
      var response = await _httpClient.GetAsync($"/api/configuration/{section}", cancellationToken);

      if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
      {
        _logger.LogDebug("Configuration section {Section} not found, will use defaults", section);
        return default;
      }

      response.EnsureSuccessStatusCode();
      var result = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
      _logger.LogDebug("Successfully loaded configuration section: {Section}", section);
      return result;
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

  /// <summary>
  /// Update a specific key within a configuration section
  /// </summary>
  public async Task<bool> UpdateConfigurationAsync(string section, string key, object value, CancellationToken cancellationToken = default)
  {
    try
    {
      // Get existing section or create new one
      var sectionData = await GetConfigurationAsync<Dictionary<string, object>>(section, cancellationToken) 
                        ?? new Dictionary<string, object>();
      
      // Update the specific key
      sectionData[key] = value;
      
      // Save back the entire section
      var response = await _httpClient.PostAsJsonAsync($"/api/configuration/{section}", sectionData, cancellationToken);
      return response.IsSuccessStatusCode;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to update configuration key {Key} in section {Section}", key, section);
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

  // ========== Phase 5: Configuration Store Management Methods ==========

  public async Task<ConfigurationStoreInfoDto?> GetStoreInfoAsync(string? storeType = null, CancellationToken cancellationToken = default)
  {
    try
    {
      var url = "/api/configuration/store-info";
      if (!string.IsNullOrEmpty(storeType))
      {
        url += $"?storeType={storeType}";
      }

      return await _httpClient.GetFromJsonAsync<ConfigurationStoreInfoDto>(url, JsonOptions, cancellationToken);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to get store info");
      return null;
    }
  }

  public async Task<ConfigurationComparisonDto?> CompareStoresAsync(CancellationToken cancellationToken = default)
  {
    try
    {
      return await _httpClient.GetFromJsonAsync<ConfigurationComparisonDto>("/api/configuration/compare", JsonOptions, cancellationToken);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to compare stores");
      return null;
    }
  }

  public async Task<bool> ReconcileStoresAsync(ReconcileConfigurationRequestDto request, CancellationToken cancellationToken = default)
  {
    try
    {
      var response = await _httpClient.PostAsJsonAsync("/api/configuration/reconcile", request, cancellationToken);
      return response.IsSuccessStatusCode;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to reconcile stores");
      return false;
    }
  }

  public async Task<byte[]?> ExportConfigurationAsync(string format = "json", string? storeType = null, CancellationToken cancellationToken = default)
  {
    try
    {
      var url = $"/api/configuration/export?format={format}";
      if (!string.IsNullOrEmpty(storeType))
      {
        url += $"&storeType={storeType}";
      }

      return await _httpClient.GetByteArrayAsync(url, cancellationToken);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to export configuration");
      return null;
    }
  }

  public async Task<bool> ImportConfigurationAsync(Stream fileStream, string fileName, string? targetStore = null, bool overwrite = true, CancellationToken cancellationToken = default)
  {
    try
    {
      using var content = new MultipartFormDataContent();
      var streamContent = new StreamContent(fileStream);
      content.Add(streamContent, "file", fileName);

      var url = $"/api/configuration/import?overwrite={overwrite}";
      if (!string.IsNullOrEmpty(targetStore))
      {
        url += $"&targetStore={targetStore}";
      }

      var response = await _httpClient.PostAsync(url, content, cancellationToken);
      return response.IsSuccessStatusCode;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to import configuration");
      return false;
    }
  }
}
