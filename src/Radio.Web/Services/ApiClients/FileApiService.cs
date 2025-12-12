using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Radio.Web.Models;

namespace Radio.Web.Services.ApiClients;

/// <summary>
/// API client service for file browser endpoints (3 endpoints)
/// </summary>
public class FileApiService
{
  private readonly HttpClient _httpClient;
  private readonly ILogger<FileApiService> _logger;

  public FileApiService(HttpClient httpClient, ILogger<FileApiService> logger)
  {
    _httpClient = httpClient;
    _logger = logger;
  }

  public async Task<FileListDto?> BrowseAsync(string? path = null, CancellationToken cancellationToken = default)
  {
    try
    {
      var url = string.IsNullOrEmpty(path) ? "/api/files" : $"/api/files?path={Uri.EscapeDataString(path)}";
      return await _httpClient.GetFromJsonAsync<FileListDto>(url, cancellationToken);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to browse files at path {Path}", path);
      return null;
    }
  }

  public async Task<bool> PlayFileAsync(string filePath, CancellationToken cancellationToken = default)
  {
    try
    {
      var response = await _httpClient.PostAsJsonAsync("/api/files/play", new { path = filePath }, cancellationToken);
      return response.IsSuccessStatusCode;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to play file {FilePath}", filePath);
      return false;
    }
  }

  public async Task<bool> AddFileToQueueAsync(string filePath, CancellationToken cancellationToken = default)
  {
    try
    {
      var response = await _httpClient.PostAsJsonAsync("/api/files/queue", new { path = filePath }, cancellationToken);
      return response.IsSuccessStatusCode;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to add file to queue {FilePath}", filePath);
      return false;
    }
  }
}
