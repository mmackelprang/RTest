using System.Text.Json;
using Microsoft.Extensions.Logging;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Models.Audio;
using Radio.Infrastructure.Configuration.Abstractions;
using Radio.Infrastructure.Configuration.Models;

namespace Radio.Infrastructure.Audio.Services;

/// <summary>
/// Queue persistence service that uses the configuration manager to save/restore queue state.
/// </summary>
public class QueuePersistenceService : IQueuePersistenceService
{
  private readonly ILogger<QueuePersistenceService> _logger;
  private readonly IConfigurationManager? _configurationManager;
  private const string QueueStatePrefix = "queue.state";

  private static readonly JsonSerializerOptions JsonOptions = new()
  {
    PropertyNameCaseInsensitive = true,
    WriteIndented = false
  };

  /// <summary>
  /// Initializes a new instance of the <see cref="QueuePersistenceService"/> class.
  /// </summary>
  /// <param name="logger">The logger instance.</param>
  /// <param name="configurationManager">Optional configuration manager for persistence.</param>
  public QueuePersistenceService(
    ILogger<QueuePersistenceService> logger,
    IConfigurationManager? configurationManager = null)
  {
    _logger = logger;
    _configurationManager = configurationManager;
  }

  /// <inheritdoc/>
  public async Task SaveQueueStateAsync(string sourceType, QueueState queueState, CancellationToken cancellationToken = default)
  {
    if (_configurationManager == null)
    {
      _logger.LogWarning("Configuration manager not available, cannot save queue state");
      return;
    }

    try
    {
      var storeId = GetStoreId(sourceType);
      
      // Serialize the queue state to JSON
      var json = JsonSerializer.Serialize(queueState, JsonOptions);
      
      // Save to configuration
      await _configurationManager.SetValueAsync(storeId, "state", json, cancellationToken);
      
      _logger.LogInformation("Saved queue state for {SourceType} with {Count} items", 
        sourceType, queueState.QueueItems.Count);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to save queue state for {SourceType}", sourceType);
    }
  }

  /// <inheritdoc/>
  public async Task<QueueState?> LoadQueueStateAsync(string sourceType, CancellationToken cancellationToken = default)
  {
    if (_configurationManager == null)
    {
      _logger.LogWarning("Configuration manager not available, cannot load queue state");
      return null;
    }

    try
    {
      var storeId = GetStoreId(sourceType);
      
      // Load from configuration
      var json = await _configurationManager.GetValueAsync<string>(storeId, "state", ConfigurationReadMode.Resolved, cancellationToken);
      
      if (string.IsNullOrEmpty(json))
      {
        _logger.LogDebug("No saved queue state found for {SourceType}", sourceType);
        return null;
      }
      
      // Deserialize the queue state
      var queueState = JsonSerializer.Deserialize<QueueState>(json, JsonOptions);
      
      if (queueState != null)
      {
        _logger.LogInformation("Loaded queue state for {SourceType} with {Count} items", 
          sourceType, queueState.QueueItems.Count);
      }
      
      return queueState;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to load queue state for {SourceType}", sourceType);
      return null;
    }
  }

  /// <inheritdoc/>
  public async Task ClearQueueStateAsync(string sourceType, CancellationToken cancellationToken = default)
  {
    if (_configurationManager == null)
    {
      _logger.LogWarning("Configuration manager not available, cannot clear queue state");
      return;
    }

    try
    {
      var storeId = GetStoreId(sourceType);
      
      // Delete from configuration
      await _configurationManager.DeleteValueAsync(storeId, "state", cancellationToken);
      
      _logger.LogInformation("Cleared queue state for {SourceType}", sourceType);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to clear queue state for {SourceType}", sourceType);
    }
  }

  private static string GetStoreId(string sourceType)
  {
    // Normalize source type to lowercase for consistent storage
    return $"{QueueStatePrefix}.{sourceType.ToLowerInvariant()}";
  }
}
