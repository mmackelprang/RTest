using Microsoft.AspNetCore.SignalR;
using Radio.API.Models;

namespace Radio.API.Hubs;

/// <summary>
/// SignalR hub for real-time audio state updates.
/// Provides playback state, now playing info, queue updates, and radio state to connected clients.
/// </summary>
public class AudioStateHub : Hub
{
  private readonly ILogger<AudioStateHub> _logger;

  /// <summary>
  /// Initializes a new instance of the AudioStateHub.
  /// </summary>
  public AudioStateHub(ILogger<AudioStateHub> logger)
  {
    _logger = logger;
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
    _logger.LogInformation("Client {ConnectionId} connected to AudioStateHub", Context.ConnectionId);
    await base.OnConnectedAsync();
  }

  /// <summary>
  /// Called when a client disconnects from the hub.
  /// </summary>
  /// <param name="exception">Exception that caused the disconnect, if any.</param>
  public override async Task OnDisconnectedAsync(Exception? exception)
  {
    if (exception != null)
    {
      _logger.LogWarning(exception, "Client {ConnectionId} disconnected with error", Context.ConnectionId);
    }
    else
    {
      _logger.LogInformation("Client {ConnectionId} disconnected", Context.ConnectionId);
    }
    await base.OnDisconnectedAsync(exception);
  }
}
