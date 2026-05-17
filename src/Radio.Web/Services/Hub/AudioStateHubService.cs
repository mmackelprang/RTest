using System.Net.Sockets;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using Radio.Web.Models;

namespace Radio.Web.Services.Hub;

/// <summary>
/// SignalR hub service for real-time audio state updates
/// Handles 10 event types: PlaybackStateChanged, NowPlayingChanged, QueueChanged,
/// RadioStateChanged, VolumeChanged, SourceChanged, FingerprintStatusChanged,
/// PhoneCallStateChanged, VisualizationModeChanged, EncoderConnectionChanged
/// </summary>
public class AudioStateHubService : IAsyncDisposable
{
  private readonly ILogger<AudioStateHubService> _logger;
  private readonly IConfiguration _configuration;
  private HubConnection? _hubConnection;
  private bool _isDisposed;
  private readonly SemaphoreSlim _connectionLock = new(1, 1);

  // Events that components can subscribe to.
  // NowPlayingChanged and VolumeChanged pass the typed payload from SignalR
  // so subscribers can use it directly instead of making a redundant HTTP call.
  public event Func<Task>? PlaybackStateChanged;
  public event Func<NowPlayingDto?, Task>? NowPlayingChanged;
  public event Func<Task>? QueueChanged;
  // RadioStateChanged carries the full RadioStateDto payload (including
  // NowPlayingMatchId) so subscribers don't need to re-fetch via REST.
  // The REST hop drops NowPlayingMatchId because RadioController has no
  // access to AudioStateUpdateService._currentMatchId — the broadcast is
  // the only path that carries it.
  public event Func<RadioStateDto, Task>? RadioStateChanged;
  public event Func<VolumeDto?, Task>? VolumeChanged;
  public event Func<Task>? SourceChanged;
  public event Func<Task>? FingerprintStatusChanged;
  public event Func<Task>? PhoneCallStateChanged;
  public event Func<Task>? VisualizationModeChanged;
  public event Func<Task>? EncoderConnectionChanged;
  public event Func<bool, Task>? SleepStateChanged;

  // Throttle disconnect log messages to avoid spam when API is down
  private static DateTime _lastDisconnectLogUtc = DateTime.MinValue;
  private static readonly TimeSpan DisconnectLogInterval = TimeSpan.FromSeconds(10);

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
        _logger.LogDebug("Hub connection already initialized");
        return;
      }

      var apiBaseUrl = _configuration["ApiBaseUrl"] ?? WebConstants.DefaultApiBaseUrl;
      var hubUrl = $"{apiBaseUrl}{WebConstants.HubPaths.Audio}";

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
      // Server sends PlaybackStateChanged with a PlaybackStateDto payload —
      // accept and discard it so SignalR dispatches the message.
      _hubConnection.On<object>("PlaybackStateChanged", async (_) =>
      {
        _logger.LogDebug("Received PlaybackStateChanged event");
        if (PlaybackStateChanged != null)
        {
          await PlaybackStateChanged.Invoke();
        }
      });

      // Server sends NowPlayingChanged with a NowPlayingDto payload —
      // deserialize and pass through so subscribers can use it directly.
      _hubConnection.On<NowPlayingDto?>("NowPlayingChanged", async (dto) =>
      {
        _logger.LogDebug("Received NowPlayingChanged event");
        if (NowPlayingChanged != null)
        {
          await NowPlayingChanged.Invoke(dto);
        }
      });

      // Server sends QueueChanged with a list payload —
      // accept and discard it so SignalR dispatches the message.
      _hubConnection.On<object>("QueueChanged", async (_) =>
      {
        _logger.LogDebug("Received QueueChanged event");
        if (QueueChanged != null)
        {
          await QueueChanged.Invoke();
        }
      });

      // Server sends RadioStateChanged with a RadioStateDto payload —
      // deserialize and pass through so subscribers can read NowPlayingMatchId
      // directly. (Previously the payload was discarded and subscribers
      // re-fetched via REST, which strips NowPlayingMatchId and silently
      // broke the recognition stream's NOW-row anchor.)
      _hubConnection.On<RadioStateDto>("RadioStateChanged", async (dto) =>
      {
        _logger.LogDebug("Received RadioStateChanged event");
        if (RadioStateChanged != null)
        {
          await RadioStateChanged.Invoke(dto);
        }
      });

      // Server sends VolumeChanged with a VolumeDto payload —
      // deserialize and pass through so subscribers can update directly.
      _hubConnection.On<VolumeDto?>("VolumeChanged", async (dto) =>
      {
        _logger.LogDebug("Received VolumeChanged event");
        if (VolumeChanged != null)
        {
          await VolumeChanged.Invoke(dto);
        }
      });

      _hubConnection.On("SourceChanged", async () =>
      {
        _logger.LogDebug("Received SourceChanged event");
        if (SourceChanged != null)
        {
          await SourceChanged.Invoke();
        }
      });

      // Server sends FingerprintStatusChanged with a FingerprintStatusDto payload —
      // accept and discard it so SignalR dispatches the message.
      _hubConnection.On<object>("FingerprintStatusChanged", async (_) =>
      {
        _logger.LogDebug("Received FingerprintStatusChanged event");
        if (FingerprintStatusChanged != null)
        {
          await FingerprintStatusChanged.Invoke();
        }
      });

      // Server sends PhoneCallStateChanged with state payload
      _hubConnection.On<object>("PhoneCallStateChanged", async (_) =>
      {
        _logger.LogDebug("Received PhoneCallStateChanged event");
        if (PhoneCallStateChanged != null)
        {
          await PhoneCallStateChanged.Invoke();
        }
      });

      // Server sends VisualizationModeChanged with mode payload
      _hubConnection.On<object>("VisualizationModeChanged", async (_) =>
      {
        _logger.LogDebug("Received VisualizationModeChanged event");
        if (VisualizationModeChanged != null)
        {
          await VisualizationModeChanged.Invoke();
        }
      });

      // Server sends EncoderConnectionChanged when encoder device connects/disconnects
      _hubConnection.On("EncoderConnectionChanged", async () =>
      {
        _logger.LogDebug("Received EncoderConnectionChanged event");
        if (EncoderConnectionChanged != null)
        {
          await EncoderConnectionChanged.Invoke();
        }
      });

      // Server sends SleepStateChanged with bool payload (true=sleeping, false=awake)
      _hubConnection.On<bool>("SleepStateChanged", async (isSleeping) =>
      {
        _logger.LogDebug("Received SleepStateChanged event: IsSleeping={IsSleeping}", isSleeping);
        if (SleepStateChanged != null)
        {
          await SleepStateChanged.Invoke(isSleeping);
        }
      });

      // Connection lifecycle events — throttled to avoid log spam when API is down
      _hubConnection.Closed += (error) =>
      {
        if (error != null && IsConnectionRefused(error))
        {
          // Throttle connection-refused spam — the ApiConnectionLoggingHandler logs these
          var now = DateTime.UtcNow;
          if (now - _lastDisconnectLogUtc >= DisconnectLogInterval)
          {
            _lastDisconnectLogUtc = now;
            _logger.LogWarning("Audio hub connection lost — API unavailable");
          }
        }
        else if (error != null)
        {
          _logger.LogWarning(error, "Audio hub connection closed with error");
        }
        else
        {
          _logger.LogInformation("Audio hub connection closed");
        }

        return Task.CompletedTask;
      };

      _hubConnection.Reconnecting += (error) =>
      {
        if (error == null || !IsConnectionRefused(error))
        {
          _logger.LogWarning(error, "Audio hub reconnecting...");
        }

        return Task.CompletedTask;
      };

      _hubConnection.Reconnected += async (connectionId) =>
      {
        _lastDisconnectLogUtc = DateTime.MinValue; // Reset throttle
        _logger.LogInformation("Audio hub reconnected. ConnectionId: {ConnectionId}", connectionId);

        // Re-subscribe to group-based channels after reconnect
        try
        {
          await _hubConnection.InvokeAsync("SubscribeToRadioState");
          await _hubConnection.InvokeAsync("SubscribeToQueue");
        }
        catch (Exception ex)
        {
          _logger.LogWarning(ex, "Failed to re-subscribe to groups after reconnect");
        }
      };

      // Start the connection
      await _hubConnection.StartAsync(cancellationToken);
      _logger.LogInformation("SignalR connection established successfully");

      // Subscribe to group-based channels that require explicit opt-in
      try
      {
        await _hubConnection.InvokeAsync("SubscribeToRadioState", cancellationToken);
        await _hubConnection.InvokeAsync("SubscribeToQueue", cancellationToken);
        _logger.LogInformation("Subscribed to RadioState and Queue groups");
      }
      catch (Exception ex)
      {
        _logger.LogWarning(ex, "Failed to subscribe to SignalR groups");
      }
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

  /// <summary>
  /// Locally triggers the SourceChanged event without going through SignalR.
  /// Call after a source switch API call succeeds to immediately notify
  /// NowPlayingPanel and other subscribers (bypasses the 500ms polling delay).
  /// </summary>
  public async Task NotifySourceChangedAsync()
  {
    if (SourceChanged != null)
    {
      await SourceChanged.Invoke();
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
    {
      return;
    }

    _isDisposed = true;

    if (_hubConnection != null)
    {
      await _hubConnection.DisposeAsync();
      _hubConnection = null;
    }

    _connectionLock.Dispose();
    GC.SuppressFinalize(this);
  }

  private static bool IsConnectionRefused(Exception ex)
  {
    var current = ex;
    while (current != null)
    {
      if (current is SocketException { SocketErrorCode: SocketError.ConnectionRefused })
      {
        return true;
      }

      current = current.InnerException;
    }
    return false;
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
