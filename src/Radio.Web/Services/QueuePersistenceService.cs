using System.Text.Json;
using Microsoft.Extensions.Logging;
using Radio.Web.Models;
using Radio.Web.Services.ApiClients;

namespace Radio.Web.Services;

/// <summary>
/// Service for persisting and restoring queue state across sessions.
/// </summary>
public class QueuePersistenceService
{
  private readonly ConfigurationApiService _configApi;
  private readonly QueueApiService _queueApi;
  private readonly ILogger<QueuePersistenceService> _logger;
  private const string QueueStateSection = "queue.state";

  public QueuePersistenceService(
    ConfigurationApiService configApi,
    QueueApiService queueApi,
    ILogger<QueuePersistenceService> logger)
  {
    _configApi = configApi;
    _queueApi = queueApi;
    _logger = logger;
  }

  /// <summary>
  /// Saves the current queue state to configuration.
  /// </summary>
  public async Task SaveQueueStateAsync(CancellationToken cancellationToken = default)
  {
    try
    {
      var queue = await _queueApi.GetQueueAsync(cancellationToken);
      if (queue == null || !queue.Any())
      {
        _logger.LogDebug("No queue to save");
        return;
      }

      // Store queue items as JSON
      var queueData = new Dictionary<string, object>
      {
        { "items", JsonSerializer.Serialize(queue) },
        { "savedAt", DateTimeOffset.UtcNow.ToString("O") },
        { "count", queue.Count }
      };

      var success = await _configApi.UpdateConfigurationAsync(QueueStateSection, queueData, cancellationToken);
      if (success)
      {
        _logger.LogInformation("Queue state saved: {Count} items", queue.Count);
      }
      else
      {
        _logger.LogWarning("Failed to save queue state");
      }
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error saving queue state");
    }
  }

  /// <summary>
  /// Attempts to restore the queue state from configuration.
  /// </summary>
  /// <returns>The restored queue items, or null if no saved state exists.</returns>
  public async Task<List<QueueItemDto>?> RestoreQueueStateAsync(CancellationToken cancellationToken = default)
  {
    try
    {
      var queueData = await _configApi.GetConfigurationAsync<Dictionary<string, object>>(QueueStateSection, cancellationToken);
      if (queueData == null || !queueData.ContainsKey("items"))
      {
        _logger.LogDebug("No saved queue state found");
        return null;
      }

      var itemsJson = queueData["items"].ToString();
      if (string.IsNullOrEmpty(itemsJson))
      {
        return null;
      }

      var items = JsonSerializer.Deserialize<List<QueueItemDto>>(itemsJson, new JsonSerializerOptions
      {
        PropertyNameCaseInsensitive = true
      });

      if (items != null && items.Any())
      {
        _logger.LogInformation("Restored queue state: {Count} items", items.Count);
      }

      return items;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error restoring queue state");
      return null;
    }
  }

  /// <summary>
  /// Clears the saved queue state.
  /// </summary>
  public async Task ClearQueueStateAsync(CancellationToken cancellationToken = default)
  {
    try
    {
      await _configApi.UpdateConfigurationAsync(QueueStateSection, new Dictionary<string, object>(), cancellationToken);
      _logger.LogInformation("Queue state cleared");
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error clearing queue state");
    }
  }
}
