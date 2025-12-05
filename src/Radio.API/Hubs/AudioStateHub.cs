using Microsoft.AspNetCore.SignalR;
using Radio.API.Models;
using Radio.Core.Interfaces;

namespace Radio.API.Hubs;

/// <summary>
/// SignalR hub for real-time audio state updates.
/// Provides playback state, now playing info, queue updates, and radio state to connected clients.
/// </summary>
/// <remarks>
/// Note: The connected client count is tracked using a static field, which means in multi-instance
/// deployments each instance will maintain its own count. For accurate cross-instance metrics,
/// consider using a distributed counter service (e.g., Redis) or a scoped service.
/// </remarks>
public class AudioStateHub : Hub
{
  private readonly ILogger<AudioStateHub> _logger;
  private readonly IMetricsCollector? _metricsCollector;
  private static int _connectedClients = 0;
  private static readonly object _lockObject = new();

  /// <summary>
  /// Initializes a new instance of the AudioStateHub.
  /// </summary>
  /// <param name="logger">The logger instance.</param>
  /// <param name="metricsCollector">Optional metrics collector.</param>
  public AudioStateHub(
    ILogger<AudioStateHub> logger,
    IMetricsCollector? metricsCollector = null)
  {
    _logger = logger;
    _metricsCollector = metricsCollector;
  }

  /// <summary>
  /// Subscribes to queue updates.
  /// Adds the connection to the "Queue" group.
  /// </summary>
  public async Task SubscribeToQueue()
  {
    await Groups.AddToGroupAsync(Context.ConnectionId, "Queue");
    _logger.LogDebug("Client {ConnectionId} subscribed to queue updates", Context.ConnectionId);
  }

  /// <summary>
  /// Unsubscribes from queue updates.
  /// Removes the connection from the "Queue" group.
  /// </summary>
  public async Task UnsubscribeFromQueue()
  {
    await Groups.RemoveFromGroupAsync(Context.ConnectionId, "Queue");
    _logger.LogDebug("Client {ConnectionId} unsubscribed from queue updates", Context.ConnectionId);
  }

  /// <summary>
  /// Subscribes to radio state updates.
  /// Adds the connection to the "RadioState" group.
  /// </summary>
  public async Task SubscribeToRadioState()
  {
    await Groups.AddToGroupAsync(Context.ConnectionId, "RadioState");
    _logger.LogDebug("Client {ConnectionId} subscribed to radio state updates", Context.ConnectionId);
  }

  /// <summary>
  /// Unsubscribes from radio state updates.
  /// Removes the connection from the "RadioState" group.
  /// </summary>
  public async Task UnsubscribeFromRadioState()
  {
    await Groups.RemoveFromGroupAsync(Context.ConnectionId, "RadioState");
    _logger.LogDebug("Client {ConnectionId} unsubscribed from radio state updates", Context.ConnectionId);
  }

  /// <summary>
  /// Called when a client connects to the hub.
  /// </summary>
  public override async Task OnConnectedAsync()
  {
    lock (_lockObject)
    {
      _connectedClients++;
      _metricsCollector?.Gauge("websocket.connected_clients", _connectedClients);
    }

    _logger.LogInformation("Client {ConnectionId} connected to AudioStateHub (total: {Count})", 
      Context.ConnectionId, _connectedClients);
    await base.OnConnectedAsync();
  }

  /// <summary>
  /// Called when a client disconnects from the hub.
  /// </summary>
  /// <param name="exception">Exception that caused the disconnect, if any.</param>
  public override async Task OnDisconnectedAsync(Exception? exception)
  {
    lock (_lockObject)
    {
      _connectedClients--;
      _metricsCollector?.Gauge("websocket.connected_clients", _connectedClients);
    }

    if (exception != null)
    {
      _logger.LogWarning(exception, "Client {ConnectionId} disconnected with error (total: {Count})", 
        Context.ConnectionId, _connectedClients);
    }
    else
    {
      _logger.LogInformation("Client {ConnectionId} disconnected (total: {Count})", 
        Context.ConnectionId, _connectedClients);
    }
    await base.OnDisconnectedAsync(exception);
  }
}
