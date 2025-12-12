using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;

namespace Radio.Web.Services.Hub;

/// <summary>
/// SignalR hub service for real-time audio state updates
/// Handles 6 event types: PlaybackStateChanged, NowPlayingChanged, QueueChanged, 
/// RadioStateChanged, VolumeChanged, SourceChanged
/// </summary>
public class AudioStateHubService : IAsyncDisposable
{
  private readonly ILogger<AudioStateHubService> _logger;
  private readonly IConfiguration _configuration;
  private HubConnection? _hubConnection;
  private bool _isDisposed;
  private readonly SemaphoreSlim _connectionLock = new(1, 1);

  // Events that components can subscribe to
  public event Func<Task>? PlaybackStateChanged;
  public event Func<Task>? NowPlayingChanged;
  public event Func<Task>? QueueChanged;
  public event Func<Task>? RadioStateChanged;
  public event Func<Task>? VolumeChanged;
  public event Func<Task>? SourceChanged;

  public bool IsConnected => _hubConnection?.State == HubConnectionState.Connected;
  public HubConnectionState ConnectionState => _hubConnection?.State ?? HubConnectionState.Disconnected;

  public AudioStateHubService(ILogger<AudioStateHubService> logger, IConfiguration configuration)
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
      var hubUrl = $"{apiBaseUrl}/hubs/audiostate";

      _logger.LogInformation("Initializing SignalR connection to {HubUrl}", hubUrl);

      _hubConnection = new HubConnectionBuilder()
        .WithUrl(hubUrl)
        .WithAutomaticReconnect(new RetryPolicy())
        .ConfigureLogging(logging =>
        {
          logging.SetMinimumLevel(LogLevel.Information);
        })
        .Build();

      // Register event handlers
      _hubConnection.On("PlaybackStateChanged", async () =>
      {
        _logger.LogDebug("Received PlaybackStateChanged event");
        if (PlaybackStateChanged != null)
          await PlaybackStateChanged.Invoke();
      });

      _hubConnection.On("NowPlayingChanged", async () =>
      {
        _logger.LogDebug("Received NowPlayingChanged event");
        if (NowPlayingChanged != null)
          await NowPlayingChanged.Invoke();
      });

      _hubConnection.On("QueueChanged", async () =>
      {
        _logger.LogDebug("Received QueueChanged event");
        if (QueueChanged != null)
          await QueueChanged.Invoke();
      });

      _hubConnection.On("RadioStateChanged", async () =>
      {
        _logger.LogDebug("Received RadioStateChanged event");
        if (RadioStateChanged != null)
          await RadioStateChanged.Invoke();
      });

      _hubConnection.On("VolumeChanged", async () =>
      {
        _logger.LogDebug("Received VolumeChanged event");
        if (VolumeChanged != null)
          await VolumeChanged.Invoke();
      });

      _hubConnection.On("SourceChanged", async () =>
      {
        _logger.LogDebug("Received SourceChanged event");
        if (SourceChanged != null)
          await SourceChanged.Invoke();
      });

      // Connection lifecycle events
      _hubConnection.Closed += async (error) =>
      {
        if (error != null)
          _logger.LogError(error, "SignalR connection closed with error");
        else
          _logger.LogInformation("SignalR connection closed");

        await Task.CompletedTask;
      };

      _hubConnection.Reconnecting += async (error) =>
      {
        _logger.LogWarning(error, "SignalR connection lost, attempting to reconnect...");
        await Task.CompletedTask;
      };

      _hubConnection.Reconnected += async (connectionId) =>
      {
        _logger.LogInformation("SignalR reconnected successfully. ConnectionId: {ConnectionId}", connectionId);
        await Task.CompletedTask;
      };

      // Start the connection
      await _hubConnection.StartAsync(cancellationToken);
      _logger.LogInformation("SignalR connection established successfully");
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to establish SignalR connection");
      throw;
    }
    finally
    {
      _connectionLock.Release();
    }
  }

  public async Task StopAsync(CancellationToken cancellationToken = default)
  {
    await _connectionLock.WaitAsync(cancellationToken);
    try
    {
      if (_hubConnection != null)
      {
        _logger.LogInformation("Stopping SignalR connection");
        await _hubConnection.StopAsync(cancellationToken);
        _logger.LogInformation("SignalR connection stopped");
      }
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error stopping SignalR connection");
    }
    finally
    {
      _connectionLock.Release();
    }
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
    GC.SuppressFinalize(this);
  }

  /// <summary>
  /// Custom retry policy with exponential backoff
  /// </summary>
  private class RetryPolicy : IRetryPolicy
  {
    public TimeSpan? NextRetryDelay(RetryContext retryContext)
    {
      // Exponential backoff: 2s, 4s, 8s, 16s, 30s (max)
      var delay = TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, retryContext.PreviousRetryCount)));
      return delay;
    }
  }
}
