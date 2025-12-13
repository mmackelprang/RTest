using Microsoft.AspNetCore.SignalR.Client;
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
        _logger.LogWarning("Hub connection already initialized");
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
      _hubConnection.On<SpectrumDataDto>("SpectrumData", async (data) =>
      {
        _logger.LogTrace("Received SpectrumData event");
        if (OnSpectrumData != null)
          await OnSpectrumData.Invoke(data);
      });

      _hubConnection.On<LevelDataDto>("LevelData", async (data) =>
      {
        _logger.LogTrace("Received LevelData event");
        if (OnLevelData != null)
          await OnLevelData.Invoke(data);
      });

      _hubConnection.On<WaveformDataDto>("WaveformData", async (data) =>
      {
        _logger.LogTrace("Received WaveformData event");
        if (OnWaveformData != null)
          await OnWaveformData.Invoke(data);
      });

      _hubConnection.On<VisualizationDataDto>("VisualizationData", async (data) =>
      {
        _logger.LogTrace("Received VisualizationData event");
        if (OnVisualizationData != null)
          await OnVisualizationData.Invoke(data);
      });

      _hubConnection.Reconnecting += exception =>
      {
        _logger.LogWarning(exception, "Hub connection reconnecting");
        return Task.CompletedTask;
      };

      _hubConnection.Reconnected += connectionId =>
      {
        _logger.LogInformation("Hub connection reconnected with ID: {ConnectionId}", connectionId);
        return Task.CompletedTask;
      };

      _hubConnection.Closed += exception =>
      {
        _logger.LogWarning(exception, "Hub connection closed");
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
    if (_hubConnection != null)
    {
      await _hubConnection.StopAsync();
      _logger.LogInformation("Disconnected from AudioVisualizationHub");
    }
  }

  // Subscription methods
  public async Task SubscribeToSpectrumAsync()
  {
    if (_hubConnection != null)
    {
      await _hubConnection.InvokeAsync("SubscribeToSpectrum");
      _logger.LogDebug("Subscribed to spectrum updates");
    }
  }

  public async Task UnsubscribeFromSpectrumAsync()
  {
    if (_hubConnection != null)
    {
      await _hubConnection.InvokeAsync("UnsubscribeFromSpectrum");
      _logger.LogDebug("Unsubscribed from spectrum updates");
    }
  }

  public async Task SubscribeToLevelsAsync()
  {
    if (_hubConnection != null)
    {
      await _hubConnection.InvokeAsync("SubscribeToLevels");
      _logger.LogDebug("Subscribed to level updates");
    }
  }

  public async Task UnsubscribeFromLevelsAsync()
  {
    if (_hubConnection != null)
    {
      await _hubConnection.InvokeAsync("UnsubscribeFromLevels");
      _logger.LogDebug("Unsubscribed from level updates");
    }
  }

  public async Task SubscribeToWaveformAsync()
  {
    if (_hubConnection != null)
    {
      await _hubConnection.InvokeAsync("SubscribeToWaveform");
      _logger.LogDebug("Subscribed to waveform updates");
    }
  }

  public async Task UnsubscribeFromWaveformAsync()
  {
    if (_hubConnection != null)
    {
      await _hubConnection.InvokeAsync("UnsubscribeFromWaveform");
      _logger.LogDebug("Unsubscribed from waveform updates");
    }
  }

  public async Task SubscribeToAllAsync()
  {
    if (_hubConnection != null)
    {
      await _hubConnection.InvokeAsync("SubscribeToAll");
      _logger.LogDebug("Subscribed to all visualization updates");
    }
  }

  public async Task UnsubscribeFromAllAsync()
  {
    if (_hubConnection != null)
    {
      await _hubConnection.InvokeAsync("UnsubscribeFromAll");
      _logger.LogDebug("Unsubscribed from all visualization updates");
    }
  }

  // Get methods for on-demand data
  public async Task<SpectrumDataDto?> GetSpectrumAsync()
  {
    if (_hubConnection != null)
    {
      return await _hubConnection.InvokeAsync<SpectrumDataDto>("GetSpectrum");
    }
    return null;
  }

  public async Task<LevelDataDto?> GetLevelsAsync()
  {
    if (_hubConnection != null)
    {
      return await _hubConnection.InvokeAsync<LevelDataDto>("GetLevels");
    }
    return null;
  }

  public async Task<WaveformDataDto?> GetWaveformAsync()
  {
    if (_hubConnection != null)
    {
      return await _hubConnection.InvokeAsync<WaveformDataDto>("GetWaveform");
    }
    return null;
  }

  public async Task<VisualizationDataDto?> GetVisualizationAsync()
  {
    if (_hubConnection != null)
    {
      return await _hubConnection.InvokeAsync<VisualizationDataDto>("GetVisualization");
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
