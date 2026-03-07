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

  public async Task<FileListDto?> BrowseAbsoluteAsync(string absolutePath, CancellationToken cancellationToken = default)
  {
    try
    {
      var url = $"/api/files?absolutePath={Uri.EscapeDataString(absolutePath)}";
      return await _httpClient.GetFromJsonAsync<FileListDto>(url, cancellationToken);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to browse absolute path {Path}", absolutePath);
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

  public async Task<(bool Success, string? Error)> AddFileToQueueAsync(string filePath, CancellationToken cancellationToken = default)
  {
    try
    {
      var response = await _httpClient.PostAsJsonAsync("/api/files/queue", new { Paths = new List<string> { filePath } }, cancellationToken);
      if (!response.IsSuccessStatusCode)
      {
        return (false, $"Server returned {response.StatusCode}");
      }

      var result = await response.Content.ReadFromJsonAsync<QueueFilesResponseDto>(cancellationToken: cancellationToken);
      if (result == null)
      {
        return (false, "Empty response from server");
      }

      if (result.AddedCount > 0)
      {
        return (true, null);
      }

      var failedPath = result.FailedPaths?.FirstOrDefault() ?? filePath;
      return (false, $"File not found or not supported: {failedPath}");
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to add file to queue {FilePath}", filePath);
      return (false, ex.Message);
    }
  }
  
  public async Task<FileListDto?> ListFilesAsync(string path, bool recursive, CancellationToken cancellationToken = default)
  {
    try
    {
      var url = $"/api/files?path={Uri.EscapeDataString(path)}&recursive={recursive}";
      return await _httpClient.GetFromJsonAsync<FileListDto>(url, cancellationToken);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to list files at path {Path}", path);
      return null;
    }
  }
  
  public async Task<(bool Success, int AddedCount, string? Error)> AddFilesToQueueAsync(List<string> filePaths, CancellationToken cancellationToken = default)
  {
    try
    {
      var response = await _httpClient.PostAsJsonAsync("/api/files/queue", new { Paths = filePaths }, cancellationToken);
      if (!response.IsSuccessStatusCode)
      {
        return (false, 0, $"Server returned {response.StatusCode}");
      }

      var result = await response.Content.ReadFromJsonAsync<QueueFilesResponseDto>(cancellationToken: cancellationToken);
      if (result == null)
      {
        return (false, 0, "Empty response from server");
      }

      if (result.AddedCount > 0)
      {
        return (true, result.AddedCount, result.FailedCount > 0 ? $"{result.FailedCount} files failed" : null);
      }

      return (false, 0, $"No files could be added ({result.FailedCount} failed)");
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to add files to queue");
      return (false, 0, ex.Message);
    }
  }

  public async Task<List<DriveInfoDto>?> GetDrivesAsync(CancellationToken cancellationToken = default)
  {
    try
    {
      return await _httpClient.GetFromJsonAsync<List<DriveInfoDto>>("/api/files/drives", cancellationToken);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to get drives");
      return null;
    }
  }
}
