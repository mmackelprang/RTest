using System.Net.Sockets;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Radio.Web.Models;

namespace Radio.Web.Services.Hub;

/// <summary>
/// SignalR hub service for real-time audio visualization data
/// Connects to /hubs/visualization and provides spectrum, waveform, and level meter data
/// </summary>
public class AudioVisualizationHubService : IAsyncDisposable
{
  private readonly ILogger<AudioVisualizationHubService> _logger;
  private readonly IConfiguration _configuration;
  private HubConnection? _hubConnection;
  private bool _isDisposed;
  private readonly SemaphoreSlim _connectionLock = new(1, 1);

  // Events that components can subscribe to
  public event Func<SpectrumDataDto, Task>? OnSpectrumData;
  public event Func<LevelDataDto, Task>? OnLevelData;
  public event Func<WaveformDataDto, Task>? OnWaveformData;
  public event Func<VisualizationDataDto, Task>? OnVisualizationData;

  // Throttle disconnect log messages to avoid spam when API is down
  private static DateTime _lastDisconnectLogUtc = DateTime.MinValue;
  private static readonly TimeSpan DisconnectLogInterval = TimeSpan.FromSeconds(10);

  public bool IsConnected => _hubConnection?.State == HubConnectionState.Connected;
  public HubConnectionState ConnectionState => _hubConnection?.State ?? HubConnectionState.Disconnected;

  public AudioVisualizationHubService(ILogger<AudioVisualizationHubService> logger, IConfiguration configuration)
  {
    _logger = logger;
    _configuration = configuration;
  }

  public async Task StartAsync(CancellationToken cancellationToken = default)
  {
    await _connectionLock.WaitAsync(cancellationToken);
    try
    {
      if (_hubConnection != null)
      {
        _logger.LogDebug("Hub connection already initialized");
        return;
      }

      var apiBaseUrl = _configuration["ApiBaseUrl"] ?? "http://localhost:5000";
      var hubUrl = $"{apiBaseUrl}/hubs/visualization";

      _logger.LogInformation("Initializing SignalR connection to {HubUrl}", hubUrl);

      _hubConnection = new HubConnectionBuilder()
        .WithUrl(hubUrl)
        .WithAutomaticReconnect(new RetryPolicy())
        .ConfigureLogging(logging =>
        {
          logging.SetMinimumLevel(LogLevel.Information);
        })
        .Build();

      // Register event handlers for broadcast events
      // Event names must match what VisualizationBroadcastService sends
      _hubConnection.On<SpectrumDataDto>("ReceiveSpectrum", async (data) =>
      {
        _logger.LogTrace("Received ReceiveSpectrum event");
        await InvokeEventHandlersAsync(OnSpectrumData, data, "OnSpectrumData");
      });

      _hubConnection.On<LevelDataDto>("ReceiveLevels", async (data) =>
      {
        _logger.LogTrace("Received ReceiveLevels event");
        await InvokeEventHandlersAsync(OnLevelData, data, "OnLevelData");
      });

      _hubConnection.On<WaveformDataDto>("ReceiveWaveform", async (data) =>
      {
        _logger.LogTrace("Received ReceiveWaveform event");
        await InvokeEventHandlersAsync(OnWaveformData, data, "OnWaveformData");
      });

      _hubConnection.On<VisualizationDataDto>("ReceiveVisualization", async (data) =>
      {
        _logger.LogTrace("Received ReceiveVisualization event");
        await InvokeEventHandlersAsync(OnVisualizationData, data, "OnVisualizationData");
      });

      // Connection lifecycle events — throttled to avoid log spam when API is down
      _hubConnection.Reconnecting += exception =>
      {
        if (exception == null || !IsConnectionRefused(exception))
          _logger.LogWarning(exception, "Visualization hub reconnecting");
        return Task.CompletedTask;
      };

      _hubConnection.Reconnected += connectionId =>
      {
        _lastDisconnectLogUtc = DateTime.MinValue; // Reset throttle
        _logger.LogInformation("Visualization hub reconnected with ID: {ConnectionId}", connectionId);
        return Task.CompletedTask;
      };

      _hubConnection.Closed += exception =>
      {
        if (exception != null && IsConnectionRefused(exception))
        {
          var now = DateTime.UtcNow;
          if (now - _lastDisconnectLogUtc >= DisconnectLogInterval)
          {
            _lastDisconnectLogUtc = now;
            _logger.LogWarning("Visualization hub connection lost — API unavailable");
          }
        }
        else
          _logger.LogWarning(exception, "Visualization hub connection closed");
        return Task.CompletedTask;
      };

      await _hubConnection.StartAsync(cancellationToken);
      _logger.LogInformation("Connected to AudioVisualizationHub");
    }
    finally
    {
      _connectionLock.Release();
    }
  }

  public async Task StopAsync()
  {
    await _connectionLock.WaitAsync();
    try
    {
      if (_hubConnection != null)
      {
        await _hubConnection.StopAsync();
        _logger.LogInformation("Disconnected from AudioVisualizationHub");
      }
    }
    finally
    {
      _connectionLock.Release();
    }
  }

  // Subscription methods
  public async Task SubscribeToSpectrumAsync()
  {
    if (_hubConnection != null && _hubConnection.State == HubConnectionState.Connected)
    {
      await _hubConnection.InvokeAsync("SubscribeToSpectrum");
      _logger.LogDebug("Subscribed to spectrum updates");
    }
    else
    {
      _logger.LogWarning("Cannot subscribe to spectrum: Hub not connected");
    }
  }

  public async Task UnsubscribeFromSpectrumAsync()
  {
    if (_hubConnection != null && _hubConnection.State == HubConnectionState.Connected)
    {
      await _hubConnection.InvokeAsync("UnsubscribeFromSpectrum");
      _logger.LogDebug("Unsubscribed from spectrum updates");
    }
  }

  public async Task SubscribeToLevelsAsync()
  {
    if (_hubConnection != null && _hubConnection.State == HubConnectionState.Connected)
    {
      await _hubConnection.InvokeAsync("SubscribeToLevels");
      _logger.LogDebug("Subscribed to level updates");
    }
    else
    {
      _logger.LogWarning("Cannot subscribe to levels: Hub not connected");
    }
  }

  public async Task UnsubscribeFromLevelsAsync()
  {
    if (_hubConnection != null && _hubConnection.State == HubConnectionState.Connected)
    {
      await _hubConnection.InvokeAsync("UnsubscribeFromLevels");
      _logger.LogDebug("Unsubscribed from level updates");
    }
  }

  public async Task SubscribeToWaveformAsync()
  {
    if (_hubConnection != null && _hubConnection.State == HubConnectionState.Connected)
    {
      await _hubConnection.InvokeAsync("SubscribeToWaveform");
      _logger.LogDebug("Subscribed to waveform updates");
    }
    else
    {
      _logger.LogWarning("Cannot subscribe to waveform: Hub not connected");
    }
  }

  public async Task UnsubscribeFromWaveformAsync()
  {
    if (_hubConnection != null && _hubConnection.State == HubConnectionState.Connected)
    {
      await _hubConnection.InvokeAsync("UnsubscribeFromWaveform");
      _logger.LogDebug("Unsubscribed from waveform updates");
    }
  }

  public async Task SubscribeToAllAsync()
  {
    if (_hubConnection != null && _hubConnection.State == HubConnectionState.Connected)
    {
      await _hubConnection.InvokeAsync("SubscribeToAll");
      _logger.LogDebug("Subscribed to all visualization updates");
    }
    else
    {
      _logger.LogWarning("Cannot subscribe to all: Hub not connected");
    }
  }

  public async Task UnsubscribeFromAllAsync()
  {
    if (_hubConnection != null && _hubConnection.State == HubConnectionState.Connected)
    {
      await _hubConnection.InvokeAsync("UnsubscribeFromAll");
      _logger.LogDebug("Unsubscribed from all visualization updates");
    }
  }

  // Get methods for on-demand data
  public async Task<SpectrumDataDto?> GetSpectrumAsync()
  {
    if (_hubConnection != null && _hubConnection.State == HubConnectionState.Connected)
    {
      try
      {
        return await _hubConnection.InvokeAsync<SpectrumDataDto>("GetSpectrum");
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Error getting spectrum data");
        return null;
      }
    }
    return null;
  }

  public async Task<LevelDataDto?> GetLevelsAsync()
  {
    if (_hubConnection != null && _hubConnection.State == HubConnectionState.Connected)
    {
      try
      {
        return await _hubConnection.InvokeAsync<LevelDataDto>("GetLevels");
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Error getting level data");
        return null;
      }
    }
    return null;
  }

  public async Task<WaveformDataDto?> GetWaveformAsync()
  {
    if (_hubConnection != null && _hubConnection.State == HubConnectionState.Connected)
    {
      try
      {
        return await _hubConnection.InvokeAsync<WaveformDataDto>("GetWaveform");
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Error getting waveform data");
        return null;
      }
    }
    return null;
  }

  public async Task<VisualizationDataDto?> GetVisualizationAsync()
  {
    if (_hubConnection != null && _hubConnection.State == HubConnectionState.Connected)
    {
      try
      {
        return await _hubConnection.InvokeAsync<VisualizationDataDto>("GetVisualization");
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Error getting visualization data");
        return null;
      }
    }
    return null;
  }

  public async ValueTask DisposeAsync()
  {
    if (_isDisposed)
      return;

    _isDisposed = true;

    if (_hubConnection != null)
    {
      await _hubConnection.DisposeAsync();
      _hubConnection = null;
    }

    _connectionLock.Dispose();
  }

  /// <summary>
  /// Safely invokes event handlers, catching exceptions from individual handlers
  /// to ensure all subscribers are notified even if one throws.
  /// </summary>
  private async Task InvokeEventHandlersAsync<T>(Func<T, Task>? eventHandler, T data, string eventName)
  {
    if (eventHandler == null)
      return;

    var handlers = eventHandler.GetInvocationList();
    foreach (var handler in handlers)
    {
      try
      {
        var func = (Func<T, Task>)handler;
        await func(data);
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Exception in {EventName} event handler", eventName);
      }
    }
  }

  private static bool IsConnectionRefused(Exception ex)
  {
    var current = ex;
    while (current != null)
    {
      if (current is SocketException { SocketErrorCode: SocketError.ConnectionRefused })
        return true;
      current = current.InnerException;
    }
    return false;
  }

  // Custom retry policy for automatic reconnection
  private class RetryPolicy : IRetryPolicy
  {
    public TimeSpan? NextRetryDelay(RetryContext retryContext)
    {
      // Exponential backoff: 0s, 2s, 5s, 10s, 30s, then 30s max
      return retryContext.PreviousRetryCount switch
      {
        0 => TimeSpan.Zero,
        1 => TimeSpan.FromSeconds(2),
        2 => TimeSpan.FromSeconds(5),
        3 => TimeSpan.FromSeconds(10),
        _ => TimeSpan.FromSeconds(30)
      };
    }
  }
}
