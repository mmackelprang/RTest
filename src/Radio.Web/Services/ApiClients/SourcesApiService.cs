using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Radio.Web.Models;

namespace Radio.Web.Services.ApiClients;

/// <summary>
/// API client service for audio source management endpoints (5 endpoints)
/// </summary>
public class SourcesApiService
{
  private readonly HttpClient _httpClient;
  private readonly ILogger<SourcesApiService> _logger;

  public SourcesApiService(HttpClient httpClient, ILogger<SourcesApiService> logger)
  {
    _httpClient = httpClient;
    _logger = logger;
  }

  public async Task<List<AudioSourceDto>?> GetSourcesAsync(CancellationToken cancellationToken = default)
  {
    try
    {
      return await _httpClient.GetFromJsonAsync<List<AudioSourceDto>>("/api/sources", cancellationToken);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to get sources");
      return null;
    }
  }

  public async Task<List<AudioSourceDto>?> GetActiveSourcesAsync(CancellationToken cancellationToken = default)
  {
    try
    {
      return await _httpClient.GetFromJsonAsync<List<AudioSourceDto>>("/api/sources/active", cancellationToken);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to get active sources");
      return null;
    }
  }

  public async Task<AudioSourceDto?> GetPrimarySourceAsync(CancellationToken cancellationToken = default)
  {
    try
    {
      return await _httpClient.GetFromJsonAsync<AudioSourceDto>("/api/sources/primary", cancellationToken);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to get primary source");
      return null;
    }
  }

  public async Task<bool> SwitchSourceAsync(string sourceId, CancellationToken cancellationToken = default)
  {
    try
    {
      var response = await _httpClient.PostAsync($"/api/sources/switch/{sourceId}", null, cancellationToken);
      return response.IsSuccessStatusCode;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to switch source");
      return false;
    }
  }

  public async Task<AudioSourceDto?> GetSourceByIdAsync(string sourceId, CancellationToken cancellationToken = default)
  {
    try
    {
      return await _httpClient.GetFromJsonAsync<AudioSourceDto>($"/api/sources/{sourceId}", cancellationToken);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to get source by ID");
      return null;
    }
  }
}
