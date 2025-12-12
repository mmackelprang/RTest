using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Radio.Web.Models;

namespace Radio.Web.Services.ApiClients;

/// <summary>
/// API client service for queue management endpoints (6 endpoints)
/// </summary>
public class QueueApiService
{
  private readonly HttpClient _httpClient;
  private readonly ILogger<QueueApiService> _logger;

  public QueueApiService(HttpClient httpClient, ILogger<QueueApiService> logger)
  {
    _httpClient = httpClient;
    _logger = logger;
  }

  public async Task<List<QueueItemDto>?> GetQueueAsync(CancellationToken cancellationToken = default)
  {
    try
    {
      return await _httpClient.GetFromJsonAsync<List<QueueItemDto>>("/api/queue", cancellationToken);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to get queue");
      return null;
    }
  }

  public async Task<bool> AddToQueueAsync(string itemId, CancellationToken cancellationToken = default)
  {
    try
    {
      var response = await _httpClient.PostAsJsonAsync("/api/queue", new { itemId }, cancellationToken);
      return response.IsSuccessStatusCode;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to add to queue");
      return false;
    }
  }

  public async Task<bool> RemoveFromQueueAsync(int index, CancellationToken cancellationToken = default)
  {
    try
    {
      var response = await _httpClient.DeleteAsync($"/api/queue/{index}", cancellationToken);
      return response.IsSuccessStatusCode;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to remove from queue");
      return false;
    }
  }

  public async Task<bool> ClearQueueAsync(CancellationToken cancellationToken = default)
  {
    try
    {
      var response = await _httpClient.DeleteAsync("/api/queue", cancellationToken);
      return response.IsSuccessStatusCode;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to clear queue");
      return false;
    }
  }

  public async Task<bool> MoveItemAsync(int fromIndex, int toIndex, CancellationToken cancellationToken = default)
  {
    try
    {
      var response = await _httpClient.PostAsJsonAsync("/api/queue/move", new { fromIndex, toIndex }, cancellationToken);
      return response.IsSuccessStatusCode;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to move queue item");
      return false;
    }
  }

  public async Task<bool> JumpToPositionAsync(int index, CancellationToken cancellationToken = default)
  {
    try
    {
      var response = await _httpClient.PostAsync($"/api/queue/jump/{index}", null, cancellationToken);
      return response.IsSuccessStatusCode;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to jump to position");
      return false;
    }
  }
}
